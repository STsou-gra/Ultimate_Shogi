using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace ShogiServer
{
    public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn }
    public enum PlayerType { Player1, Player2 }

    public class GamePieceData
    {
        public PieceType type;
        public PlayerType player;
        public int X;
        public int Y;
        public bool isPromoted;
    }

    class Program
    {
        private static TcpListener listener = null!;
        private static List<ClientHandler> clients = new List<ClientHandler>();
        private static object clientsLock = new object();

        private static GamePieceData?[] boardData = new GamePieceData?[81];
        private static Dictionary<PieceType, int> player1Hand = new Dictionary<PieceType, int>();
        private static Dictionary<PieceType, int> player2Hand = new Dictionary<PieceType, int>();
        private static PlayerType currentTurn = PlayerType.Player1;
        private static bool isGameActive = false;

        static void Main(string[] args)
        {
            InitializeGame();

            listener = new TcpListener(IPAddress.Any, 7777);
            listener.Start();
            Console.WriteLine("サーバー起動ポート: 7777. プレイヤー接続待ち...");

            while (clients.Count < 2)
            {
                TcpClient socket = listener.AcceptTcpClient();
                lock (clientsLock)
                {
                    int playerType = clients.Count; // 0 = Player1, 1 = Player2
                    ClientHandler handler = new ClientHandler(socket, (PlayerType)playerType);
                    clients.Add(handler);
                    Console.WriteLine($"プレイヤー {(PlayerType)playerType} が接続しました。 ({socket.Client.RemoteEndPoint})");
                    
                    Thread t = new Thread(handler.Listen);
                    t.IsBackground = true;
                    t.Start();
                }
            }

            // 両方の接続が完了したらゲーム開始
            StartGame();
        }

        private static void InitializeGame()
        {
            currentTurn = PlayerType.Player1;
            isGameActive = false;

            // 持ち駒辞書の初期化
            foreach (PieceType type in Enum.GetValues(typeof(PieceType)))
            {
                if (type == PieceType.King) continue;
                player1Hand[type] = 0;
                player2Hand[type] = 0;
            }

            // 初期盤面レイアウト作成 (GameBoard.cs.CreateLayout と同一)
            for (int i = 0; i < 9; i++)
            {
                AddPiece(PieceType.Pawn, PlayerType.Player1, 2, i);
                AddPiece(PieceType.Pawn, PlayerType.Player2, 6, i);
            }

            AddPiece(PieceType.King, PlayerType.Player1, 0, 4);
            AddPiece(PieceType.King, PlayerType.Player2, 8, 4);

            AddPiece(PieceType.Rook, PlayerType.Player1, 1, 7);
            AddPiece(PieceType.Rook, PlayerType.Player2, 7, 1);
            AddPiece(PieceType.Bishop, PlayerType.Player1, 1, 1);
            AddPiece(PieceType.Bishop, PlayerType.Player2, 7, 7);

            AddPiece(PieceType.GoldGeneral, PlayerType.Player1, 0, 3);
            AddPiece(PieceType.GoldGeneral, PlayerType.Player1, 0, 5);
            AddPiece(PieceType.GoldGeneral, PlayerType.Player2, 8, 3);
            AddPiece(PieceType.GoldGeneral, PlayerType.Player2, 8, 5);

            AddPiece(PieceType.SilverGeneral, PlayerType.Player1, 0, 2);
            AddPiece(PieceType.SilverGeneral, PlayerType.Player1, 0, 6);
            AddPiece(PieceType.SilverGeneral, PlayerType.Player2, 8, 2);
            AddPiece(PieceType.SilverGeneral, PlayerType.Player2, 8, 6);

            AddPiece(PieceType.Knight, PlayerType.Player1, 0, 1);
            AddPiece(PieceType.Knight, PlayerType.Player1, 0, 7);
            AddPiece(PieceType.Knight, PlayerType.Player2, 8, 1);
            AddPiece(PieceType.Knight, PlayerType.Player2, 8, 7);

            AddPiece(PieceType.Lance, PlayerType.Player1, 0, 0);
            AddPiece(PieceType.Lance, PlayerType.Player1, 0, 8);
            AddPiece(PieceType.Lance, PlayerType.Player2, 8, 8);
            AddPiece(PieceType.Lance, PlayerType.Player2, 8, 0);
        }

        private static void AddPiece(PieceType type, PlayerType player, int x, int y)
        {
            int index = x + y * 9;
            boardData[index] = new GamePieceData { type = type, player = player, X = x, Y = y, isPromoted = false };
        }

        private static void StartGame()
        {
            isGameActive = true;
            Console.WriteLine("ゲームを開始します。");

            // 各クライアントに部屋情報を通知
            BroadcastRoomInfo();
        }

        private static void BroadcastRoomInfo()
        {
            lock (clientsLock)
            {
                foreach (var client in clients)
                {
                    RoomInfoPayload payload = new RoomInfoPayload { assignedPlayerType = (int)client.Role };
                    client.Send("RoomInfo", payload);
                }
            }
        }

        public static void Broadcast(string type, object payload)
        {
            lock (clientsLock)
            {
                foreach (var client in clients)
                {
                    client.Send(type, payload);
                }
            }
        }

        // --- ルールチェック ---
        public static bool HandleMoveRequest(PlayerType player, int fromX, int fromY, int toX, int toY, bool promote)
        {
            if (!isGameActive) return false;
            if (player != currentTurn) return false;

            int fromIndex = fromX + fromY * 9;
            GamePieceData? piece = boardData[fromIndex];
            if (piece == null || piece.player != player) return false;

            // 移動先が範囲内か
            if (toX < 0 || toX > 8 || toY < 0 || toY > 8) return false;

            // 移動の検証 (BaseMoveRuleと同等のロジック)
            if (!CanMovePiece(piece, toX, toY)) return false;

            // 成りの検証
            if (promote)
            {
                // すでに成っている駒、または王や金は成れない
                if (piece.isPromoted || piece.type == PieceType.King || piece.type == PieceType.GoldGeneral)
                {
                    return false;
                }

                // 移動前か移動後が敵陣に入っているか
                bool fromInEnemy = (player == PlayerType.Player1) ? (fromX >= 6) : (fromX <= 2);
                bool toInEnemy = (player == PlayerType.Player1) ? (toX >= 6) : (toX <= 2);
                if (!fromInEnemy && !toInEnemy)
                {
                    return false; // 成り条件を満たしていない
                }
            }
            else
            {
                // 強制成りのチェック
                if (MustPromote(piece, toX))
                {
                    return false; // 強制的に成る必要がある
                }
            }

            // --- 移動処理の実行 ---
            int toIndex = toX + toY * 9;
            GamePieceData? targetPiece = boardData[toIndex];
            bool isGameOver = false;

            if (targetPiece != null)
            {
                if (targetPiece.player != player)
                {
                    // 駒を取る
                    if (targetPiece.type == PieceType.King)
                    {
                        isGameOver = true;
                    }
                    AddPieceToHand(player, targetPiece.type);
                }
                else
                {
                    return false; // 自駒の上には乗れない
                }
            }

            // 盤面更新
            boardData[toIndex] = piece;
            boardData[fromIndex] = null;
            piece.X = toX;
            piece.Y = toY;

            if (promote)
            {
                piece.isPromoted = true;
            }

            // イベントブロードキャスト
            GameEventPayload ev = new GameEventPayload
            {
                eventType = "Move",
                fromX = fromX,
                fromY = fromY,
                toX = toX,
                toY = toY,
                pieceType = (int)piece.type,
                activePlayer = (int)player,
                promote = promote
            };
            Broadcast("GameEvent", ev);

            if (isGameOver)
            {
                isGameActive = false;
                Broadcast("GameOver", new GameOverPayload { winner = (int)player });
                Console.WriteLine($"ゲームオーバー。勝者: {player}");
            }
            else
            {
                // ターン交代
                currentTurn = (currentTurn == PlayerType.Player1) ? PlayerType.Player2 : PlayerType.Player1;
            }

            return true;
        }

        private static bool MustPromote(GamePieceData piece, int toX)
        {
            if (piece.type == PieceType.Pawn || piece.type == PieceType.Lance)
            {
                return (piece.player == PlayerType.Player1 && toX == 8) || (piece.player == PlayerType.Player2 && toX == 0);
            }
            if (piece.type == PieceType.Knight)
            {
                return (piece.player == PlayerType.Player1 && toX >= 7) || (piece.player == PlayerType.Player2 && toX <= 1);
            }
            return false;
        }

        public static bool HandleDropRequest(PlayerType player, PieceType type, int toX, int toY)
        {
            if (!isGameActive) return false;
            if (player != currentTurn) return false;

            // 所持しているか
            var hand = (player == PlayerType.Player1) ? player1Hand : player2Hand;
            if (!hand.ContainsKey(type) || hand[type] <= 0) return false;

            // ドロップ制限の検証
            if (boardData[toX + toY * 9] != null) return false; // 空きマスであること

            // 行き所のない駒
            if (type == PieceType.Pawn || type == PieceType.Lance)
            {
                if (player == PlayerType.Player1 && toX == 8) return false;
                if (player == PlayerType.Player2 && toX == 0) return false;
            }
            else if (type == PieceType.Knight)
            {
                if (player == PlayerType.Player1 && toX >= 7) return false;
                if (player == PlayerType.Player2 && toX <= 1) return false;
            }

            // 二歩
            if (type == PieceType.Pawn)
            {
                for (int x = 0; x < 9; x++)
                {
                    GamePieceData? p = boardData[x + toY * 9];
                    if (p != null && p.player == player && p.type == PieceType.Pawn)
                    {
                        return false;
                    }
                }
            }

            // --- 配置処理の実行 ---
            hand[type]--;
            AddPiece(type, player, toX, toY);

            // イベントブロードキャスト
            GameEventPayload ev = new GameEventPayload
            {
                eventType = "Drop",
                toX = toX,
                toY = toY,
                pieceType = (int)type,
                activePlayer = (int)player
            };
            Broadcast("GameEvent", ev);

            // ターン交代
            currentTurn = (currentTurn == PlayerType.Player1) ? PlayerType.Player2 : PlayerType.Player1;

            return true;
        }

        private static void AddPieceToHand(PlayerType player, PieceType type)
        {
            if (type == PieceType.King) return;
            var hand = (player == PlayerType.Player1) ? player1Hand : player2Hand;
            hand[type]++;
        }

        private static bool CanMovePiece(GamePieceData piece, int toX, int toY)
        {
            int dx = toX - piece.X;
            int dy = toY - piece.Y;

            // 各駒の移動ロジック
            if (piece.isPromoted)
            {
                if (piece.type == PieceType.Rook)
                {
                    // 竜王：飛車 ＋ 周囲1マス
                    return (dx == 0 || dy == 0) || (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1);
                }
                if (piece.type == PieceType.Bishop)
                {
                    // 竜馬：角行 ＋ 周囲1マス
                    return (Math.Abs(dx) == Math.Abs(dy)) || (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1);
                }

                // その他の成駒（と金、成香、成桂、成銀）は金将と同じ
                int gForwardX = (piece.player == PlayerType.Player1) ? -dx : dx;
                int gForwardY = (piece.player == PlayerType.Player1) ? -dy : dy;
                return (Math.Abs(gForwardX) <= 1 && Math.Abs(gForwardY) <= 1) && !(gForwardX == 1 && Math.Abs(gForwardY) == 1);
            }

            switch (piece.type)
            {
                case PieceType.Pawn:
                    int forwardX = (piece.player == PlayerType.Player1) ? 1 : -1;
                    if (!(dx == forwardX && dy == 0)) return false;
                    break;

                case PieceType.Lance:
                    int lForwardX = (piece.player == PlayerType.Player1) ? -dx : dx;
                    int lForwardY = (piece.player == PlayerType.Player1) ? -dy : dy;
                    if (!(lForwardX < 0 && lForwardY == 0)) return false;
                    break;

                case PieceType.Knight:
                    int kForwardX = (piece.player == PlayerType.Player1) ? -dx : dx;
                    int kForwardY = (piece.player == PlayerType.Player1) ? -dy : dy;
                    if (!(kForwardX == -2 && Math.Abs(kForwardY) == 1)) return false;
                    break;

                case PieceType.King:
                    if (!(Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)) return false;
                    break;

                case PieceType.Rook:
                    if (!(dx == 0 || dy == 0)) return false;
                    break;

                case PieceType.Bishop:
                    if (Math.Abs(dx) != Math.Abs(dy)) return false;
                    break;

                case PieceType.GoldGeneral:
                    int ggForwardX = (piece.player == PlayerType.Player1) ? -dx : dx;
                    int ggForwardY = (piece.player == PlayerType.Player1) ? -dy : dy;
                    if (!((Math.Abs(ggForwardX) <= 1 && Math.Abs(ggForwardY) <= 1) && !(ggForwardX == 1 && Math.Abs(ggForwardY) == 1))) return false;
                    break;

                case PieceType.SilverGeneral:
                    int sForwardX = (piece.player == PlayerType.Player1) ? -dx : dx;
                    int sForwardY = (piece.player == PlayerType.Player1) ? -dy : dy;
                    if (!((sForwardX == -1 && Math.Abs(sForwardY) <= 1) || (sForwardX == 1 && Math.Abs(sForwardY) == 1))) return false;
                    break;
            }

            // 障害物チェック（桂馬以外）
            if (piece.type != PieceType.Knight)
            {
                if (IsPathBlocked(piece.X, piece.Y, toX, toY)) return false;
            }

            return true;
        }

        private static bool IsPathBlocked(int fromX, int fromY, int toX, int toY)
        {
            int dx = toX - fromX;
            int dy = toY - fromY;

            if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1) return false;

            int stepX = Math.Sign(dx);
            int stepY = Math.Sign(dy);

            int checkX = fromX + stepX;
            int checkY = fromY + stepY;

            while (checkX != toX || checkY != toY)
            {
                if (checkX < 0 || checkX > 8 || checkY < 0 || checkY > 8) break;
                if (boardData[checkX + checkY * 9] != null)
                {
                    return true; // 障害物あり
                }
                checkX += stepX;
                checkY += stepY;
            }
            return false;
        }
    }

    public class ClientHandler
    {
        private TcpClient socket;
        private NetworkStream stream;
        private StreamReader reader;
        private StreamWriter writer;
        public PlayerType Role { get; private set; }

        public ClientHandler(TcpClient socket, PlayerType role)
        {
            this.socket = socket;
            this.stream = socket.GetStream();
            this.reader = new StreamReader(stream, Encoding.UTF8);
            this.writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
            this.Role = role;
        }

        public void Listen()
        {
            try
            {
                while (socket.Connected)
                {
                    string? line = reader.ReadLine();
                    if (line == null) break;

                    ProcessMessage(line);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"接続エラー: {e.Message}");
            }
            finally
            {
                Console.WriteLine($"プレイヤー {Role} が切断されました。");
                socket.Close();
            }
        }

        private void ProcessMessage(string json)
        {
            try
            {
                NetworkMessage? msg = JsonSerializer.Deserialize<NetworkMessage>(json);
                if (msg == null) return;

                switch (msg.Type)
                {
                    case "MoveRequest":
                        MoveRequestPayload? move = JsonSerializer.Deserialize<MoveRequestPayload>(msg.Payload);
                        if (move != null)
                        {
                            bool success = Program.HandleMoveRequest(Role, move.fromX, move.fromY, move.toX, move.toY, move.promote);
                            if (!success) SendError("無効な指し手です。");
                        }
                        break;

                    case "DropRequest":
                        DropRequestPayload? drop = JsonSerializer.Deserialize<DropRequestPayload>(msg.Payload);
                        if (drop != null)
                        {
                            bool success = Program.HandleDropRequest(Role, (PieceType)drop.pieceType, drop.toX, drop.toY);
                            if (!success) SendError("無効な配置要求です。");
                        }
                        break;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"メッセージ解析エラー: {e.Message}");
            }
        }

        public void Send(string type, object payload)
        {
            try
            {
                NetworkMessage msg = new NetworkMessage
                {
                    Type = type,
                    Payload = JsonSerializer.Serialize(payload)
                };
                string json = JsonSerializer.Serialize(msg);
                writer.WriteLine(json);
            }
            catch (Exception e)
            {
                Console.WriteLine($"送信失敗: {e.Message}");
            }
        }

        private void SendError(string message)
        {
            Send("Error", new ErrorPayload { message = message });
        }
    }

    // メッセージモデル
    public class NetworkMessage
    {
        public string Type { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
    }

    public class RoomInfoPayload
    {
        public int assignedPlayerType { get; set; }
    }

    public class MoveRequestPayload
    {
        public int fromX { get; set; }
        public int fromY { get; set; }
        public int toX { get; set; }
        public int toY { get; set; }
        public bool promote { get; set; }
    }

    public class DropRequestPayload
    {
        public int pieceType { get; set; }
        public int toX { get; set; }
        public int toY { get; set; }
    }

    public class GameEventPayload
    {
        public string eventType { get; set; } = string.Empty;
        public int fromX { get; set; }
        public int fromY { get; set; }
        public int toX { get; set; }
        public int toY { get; set; }
        public int pieceType { get; set; }
        public int activePlayer { get; set; }
        public bool promote { get; set; }
    }

    public class GameOverPayload
    {
        public int winner { get; set; }
    }

    public class ErrorPayload
    {
        public string message { get; set; } = string.Empty;
    }
}
