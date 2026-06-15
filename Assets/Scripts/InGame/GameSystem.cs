using UnityEngine;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;

// ターン管理やゲームの準備を行う
public class GameSystem : MonoBehaviour
{
    private GameState currentState;
    private PlayerType winner; // 勝者のプレイヤータイプを保存する変数
    private GameBoard gameBoard;
    private GameCamera gameCamera;
    private GameCursor gameCursor;
    private GamePiece selectedPiece; // 現在選択されている駒
    public GameSceneManager sceneManager;
    [SerializeField] private GameController gameController;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentState = GameState.Preparation;

        gameBoard = Object.FindObjectsByType<GameBoard>(FindObjectsSortMode.None)[0];
        gameCamera = Object.FindObjectsByType<GameCamera>(FindObjectsSortMode.None)[0];
        //遷移画面の管理
        sceneManager = FindAnyObjectByType<GameSceneManager>();
        
        gameCursor = Object.FindObjectsByType<GameCursor>(FindObjectsSortMode.None)[0];
    }

    // Update is called once per frame
    void Update()
    {
        //TODO: ターン管理
        switch(currentState)
        {
            case GameState.Preparation:
                DecideTurn();
                break;
            case GameState.Player1Turn:
                
                // プレイヤー1のターンの処理
                // ターン終了条件を満たしたらプレイヤー2のターンに移行
                // カーソルを移動させて駒を選択
                // 駒を移動完了フラグが立ったらターン終了
                if(gameController.IsOkTrigger())
                {
                    HandlePieceSelection();
                }

                if(gameController.IsCancelTrigger())
                {
                    // キャンセル入力があった場合、選択を解除して再度選択させる
                    selectedPiece = null;
                    gameBoard.AllDeactivePanel();
                }
                break;
            case GameState.Player2Turn:
                // プレイヤー2のターンの処理
                // ターン終了条件を満たしたらプレイヤー1のターンに移行
                // カーソルを移動させて駒を選択
                // 駒を移動完了フラグが立ったらターン終了
                if(gameController.IsOkTrigger())
                {
                    HandlePieceSelection();
                }

                if(gameController.IsCancelTrigger())
                {
                    // キャンセル入力があった場合、選択を解除して再度選択させる
                    selectedPiece = null;
                    gameBoard.AllDeactivePanel();
                }
                break;
            case GameState.GameOver:
                // ゲームオーバーの処理
                gameBoard.AllDeactivePanel();
                if(winner == PlayerType.Player1)
                {
                    Debug.Log("プレイヤー1の勝利！");
                }
                else if (winner == PlayerType.Player2)
                {
                    Debug.Log("プレイヤー2の勝利！");
                }
                else
                {
                    Debug.Log("引き分け！");
                }
                gameBoard.ChangeAbilityOnOff(0);
                sceneManager.GameOver();
                break;
        }
    }

    void HandlePieceSelection()
    {
        int x = gameCursor.X;
        int y = gameCursor.Y;
        bool isGameOver = false;

        if (selectedPiece == null)
        {
            // --- 駒を選択するフェーズ ---
            GamePiece piece = gameBoard.GetPieceAt(x, y);

            // 自分の駒なら選択
            if (piece != null && IsMyPiece(piece))
            {
                selectedPiece = piece;
                gameBoard.ActivePanel(GameHelper.CalcPanelNum(x, y)); // 選択した足元を光らせる
                ShowMovablePanels(selectedPiece); // 移動可能なマスを光らせる
            }
        }
        else
        {
            // --- 駒を移動させるフェーズ ---
            if (CanPieceMoveTo(selectedPiece, x, y))
            {
                // --- 移動成功の処理 ---
                GamePiece targetPiece = gameBoard.GetPieceAt(x, y);
                if (targetPiece != null)
                {   
                    if(targetPiece.player != selectedPiece.player)
                    {
                        isGameOver = gameBoard.RemovePieceAt(x, y);
                    }
                }
                gameBoard.UpdateBoardData(selectedPiece.X, selectedPiece.Y, x, y);

                selectedPiece.X = x;
                selectedPiece.Y = y;
                selectedPiece.MoveTo(GameHelper.CalcPanelLocation(x, y));
                if (isGameOver)
                {
                    winner = selectedPiece.player;
                    currentState = GameState.GameOver;
                    return;
                }
                selectedPiece = null;
                gameBoard.AllDeactivePanel();
                NextState();
            }
            else
            {
                Debug.Log("そこには移動できません");
            }
        }
    }

    bool IsMyPiece(GamePiece piece)
    {
        if (currentState == GameState.Player1Turn && piece.player == PlayerType.Player1) return true;
        if (currentState == GameState.Player2Turn && piece.player == PlayerType.Player2) return true;
        return false;
    }

    private bool CanPieceMoveTo(GamePiece piece, int targetX, int targetY)
    {
        // 基本の動き（各駒の子クラスの CanMove）
        if (!piece.CanMove(targetX, targetY)) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight)
        {
            if (GameHelper.IsPathBlocked(gameBoard.GetBoardData(), piece.X, piece.Y, targetX, targetY))
            {
                return false;
            }
        }

        // 移動先に自分の駒があるか
        GamePiece targetPiece = gameBoard.GetPieceAt(targetX, targetY);
        if (targetPiece != null)
        {
            if (targetPiece.player == piece.player)
            {
                return false; // 自分の駒があるので移動できない
            }
        }
        return true;
    }

    void ShowMovablePanels(GamePiece piece)
    {
        gameBoard.AllDeactivePanel();

        for (int tx = 0; tx < 9; tx++)
        {
            for (int ty = 0; ty < 9; ty++)
            {
                if (CanPieceMoveTo(piece, tx, ty))
                {
                    gameBoard.ActivePanel(GameHelper.CalcPanelNum(tx, ty));
                }
            }
        }
    }


    void NextState()
    {
        switch(currentState)
        {
            case GameState.Preparation:
                
                break;
            case GameState.Player1Turn:
                currentState = GameState.Player2Turn;
                Debug.Log("プレイヤー2にターンが移りました！");
                gameCamera.RotateToPlayer(PlayerType.Player2);
                break;
            case GameState.Player2Turn:
                currentState = GameState.Player1Turn;
                Debug.Log("プレイヤー1にターンが移りました！");
                gameCamera.RotateToPlayer(PlayerType.Player1);
                break;
            case GameState.GameOver:
                // ゲームオーバーの処理
                break;
        }
    }

    void DecideTurn()
    {
        // ランダムにプレイヤー1かプレイヤー2のどちらが先攻かを決定する処理
        if (Random.value < 0.5f)        {
            currentState = GameState.Player1Turn;
            Debug.Log("プレイヤー1のターンです！");
            gameCamera.RotateToPlayer(PlayerType.Player1);
        } else {
            currentState = GameState.Player2Turn;
            Debug.Log("プレイヤー2のターンです！");
            gameCamera.RotateToPlayer(PlayerType.Player2);
        }
    }
    
    public bool IsPlayer1Turn()
    {
        return currentState == GameState.Player1Turn;
    }
}

enum GameState
{
    Preparation, // ゲームの準備中
    Player1Turn, // プレイヤー1のターン
    Player2Turn,  // プレイヤー2のターン
    GameOver    // ゲームオーバー
}
