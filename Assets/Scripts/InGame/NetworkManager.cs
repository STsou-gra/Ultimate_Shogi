using UnityEngine;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Collections.Generic;

public class NetworkManager : MonoBehaviour
{
    private static NetworkManager instance;
    public static NetworkManager Instance => instance;

    [Header("Network Settings")]
    [SerializeField] private string serverAddress = "127.0.0.1";
    [SerializeField] private int serverPort = 7777;

    private TcpClient socket;
    private NetworkStream stream;
    private StreamReader reader;
    private StreamWriter writer;
    private Thread receiveThread;
    private bool isConnected = false;

    // メインスレッドで処理するためのメッセージキュー
    private Queue<string> messageQueue = new Queue<string>();
    private object queueLock = new object();

    public PlayerType MyPlayerType { get; private set; } = PlayerType.Player1; // サーバーから割り当てられる
    public bool IsOnlineMatch { get; set; } = true; // オンライン対戦フラグ

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        // 開発・テスト用に自動接続。不要な場合はUIなどからConnect()を呼び出します。
        ConnectToServer();
    }

    public void ConnectToServer()
    {
        try
        {
            socket = new TcpClient();
            socket.Connect(serverAddress, serverPort);
            stream = socket.GetStream();
            reader = new StreamReader(stream, Encoding.UTF8);
            writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };

            isConnected = true;
            Debug.Log("サーバーに接続しました。");

            // 受信スレッドの開始
            receiveThread = new Thread(ReceiveLoop);
            receiveThread.IsBackground = true;
            receiveThread.Start();

            // 参加リクエストの送信
            SendJoinRequest();
        }
        catch (Exception e)
        {
            Debug.LogError($"接続エラー: {e.Message}");
            isConnected = false;
        }
    }

    void Update()
    {
        // メインスレッドで受信メッセージを処理
        string message = null;
        lock (queueLock)
        {
            if (messageQueue.Count > 0)
            {
                message = messageQueue.Dequeue();
            }
        }

        if (message != null)
        {
            ProcessMessage(message);
        }
    }

    private void ReceiveLoop()
    {
        try
        {
            while (isConnected && socket != null && socket.Connected)
            {
                string line = reader.ReadLine();
                if (line == null)
                {
                    Debug.Log("サーバーから切断されました。");
                    break;
                }

                lock (queueLock)
                {
                    messageQueue.Enqueue(line);
                }
            }
        }
        catch (Exception e)
        {
            Debug.Log($"受信終了: {e.Message}");
        }
        finally
        {
            Disconnect();
        }
    }

    private void SendMessageToServer(string messageType, object payload)
    {
        if (!isConnected || writer == null) return;

        try
        {
            NetworkMessage msg = new NetworkMessage
            {
                type = messageType,
                payload = JsonUtility.ToJson(payload)
            };
            string json = JsonUtility.ToJson(msg);
            writer.WriteLine(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"送信エラー: {e.Message}");
        }
    }

    private void SendJoinRequest()
    {
        SendMessageToServer("Join", new JoinPayload { playerName = "UnityPlayer" });
    }

    public void SendMoveRequest(int fromX, int fromY, int toX, int toY, bool promote)
    {
        SendMessageToServer("MoveRequest", new MoveRequestPayload { fromX = fromX, fromY = fromY, toX = toX, toY = toY, promote = promote });
    }

    public void SendDropRequest(PieceType pieceType, int toX, int toY)
    {
        SendMessageToServer("DropRequest", new DropRequestPayload { pieceType = (int)pieceType, toX = toX, toY = toY });
    }

    private void ProcessMessage(string jsonMessage)
    {
        try
        {
            NetworkMessage msg = JsonUtility.FromJson<NetworkMessage>(jsonMessage);
            switch (msg.type)
            {
                case "RoomInfo":
                    RoomInfoPayload roomInfo = JsonUtility.FromJson<RoomInfoPayload>(msg.payload);
                    MyPlayerType = (PlayerType)roomInfo.assignedPlayerType;
                    Debug.Log($"マッチング成功! あなたは: {MyPlayerType}");
                    break;

                case "GameEvent":
                    GameEventPayload gameEvent = JsonUtility.FromJson<GameEventPayload>(msg.payload);
                    HandleServerGameEvent(gameEvent);
                    break;

                case "GameOver":
                    GameOverPayload gameOver = JsonUtility.FromJson<GameOverPayload>(msg.payload);
                    HandleServerGameOver(gameOver);
                    break;
                
                case "Error":
                    ErrorPayload error = JsonUtility.FromJson<ErrorPayload>(msg.payload);
                    Debug.LogWarning($"サーバーからのエラー: {error.message}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"メッセージ処理エラー: {e.Message}");
        }
    }

    private void HandleServerGameEvent(GameEventPayload ev)
    {
        GameSystem gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameSystem == null) return;

        // サーバーからイベント（移動または打つ）を受けて盤面を同期
        if (ev.eventType == "Move")
        {
            gameSystem.OnServerMovePiece(ev.fromX, ev.fromY, ev.toX, ev.toY, (PlayerType)ev.activePlayer, ev.promote);
        }
        else if (ev.eventType == "Drop")
        {
            gameSystem.OnServerDropPiece((PieceType)ev.pieceType, ev.toX, ev.toY, (PlayerType)ev.activePlayer);
        }
    }

    private void HandleServerGameOver(GameOverPayload payload)
    {
        GameSystem gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameSystem != null)
        {
            gameSystem.OnServerGameOver((PlayerType)payload.winner);
        }
    }

    public void Disconnect()
    {
        isConnected = false;
        if (reader != null) reader.Close();
        if (writer != null) writer.Close();
        if (stream != null) stream.Close();
        if (socket != null) socket.Close();
        Debug.Log("切断しました。");
    }

    void OnApplicationQuit()
    {
        Disconnect();
    }
}

// ネットワークシリアライズ用クラス群
[System.Serializable]
public class NetworkMessage
{
    public string type;
    public string payload;
}

[System.Serializable]
public class JoinPayload
{
    public string playerName;
}

[System.Serializable]
public class RoomInfoPayload
{
    public int assignedPlayerType; // 0 = Player1, 1 = Player2
}

[System.Serializable]
public class MoveRequestPayload
{
    public int fromX;
    public int fromY;
    public int toX;
    public int toY;
    public bool promote;
}

[System.Serializable]
public class DropRequestPayload
{
    public int pieceType;
    public int toX;
    public int toY;
}

[System.Serializable]
public class GameEventPayload
{
    public string eventType; // "Move" または "Drop"
    public int fromX;
    public int fromY;
    public int toX;
    public int toY;
    public int pieceType;
    public int activePlayer; // 0 = Player1, 1 = Player2
    public bool promote;
}

[System.Serializable]
public class GameOverPayload
{
    public int winner;
}

[System.Serializable]
public class ErrorPayload
{
    public string message;
}
