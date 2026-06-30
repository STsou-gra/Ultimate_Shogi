using UnityEngine;
using UnityEngine.InputSystem;
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

    // 持ち駒のデータ
    private Dictionary<PieceType, int> player1Hand = new Dictionary<PieceType, int>()
    {
        { PieceType.Pawn, 0 },
        { PieceType.Lance, 0 },
        { PieceType.Knight, 0 },
        { PieceType.SilverGeneral, 0 },
        { PieceType.GoldGeneral, 0 },
        { PieceType.Bishop, 0 },
        { PieceType.Rook, 0 }
    };

    private Dictionary<PieceType, int> player2Hand = new Dictionary<PieceType, int>()
    {
        { PieceType.Pawn, 0 },
        { PieceType.Lance, 0 },
        { PieceType.Knight, 0 },
        { PieceType.SilverGeneral, 0 },
        { PieceType.GoldGeneral, 0 },
        { PieceType.Bishop, 0 },
        { PieceType.Rook, 0 }
    };

    // 持ち駒打つ系の状態変数
    private bool isSelectingHand = false; // 持ち駒の種類を選択中か
    private bool isDroppingHand = false;  // 持ち駒を打つ位置を選択中か
    private PieceType selectedHandType;   // 選択した持ち駒の種類
    private List<PieceType> availableHandTypes = new List<PieceType>(); // 現在のプレイヤーが持っている駒のリスト
    private int selectedHandIndex = 0;    // 選択中のインデックス

    public bool IsSelectingHand => isSelectingHand;

    [Header("Promotion UI Reference")]
    [SerializeField] private GameObject promoteConfirmPanel; // 成り確認ダイアログパネル
    private bool isWaitingForPromoteChoice = false;
    private int pendingFromX, pendingFromY, pendingToX, pendingToY;

    public bool IsWaitingForPromoteChoice => isWaitingForPromoteChoice;

    // ルールエンジン用のルールリスト
    private List<IGameRule> gameRules = new List<IGameRule>();

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentState = GameState.Preparation;

        gameBoard = Object.FindObjectsByType<GameBoard>(FindObjectsSortMode.None)[0];
        gameCamera = Object.FindObjectsByType<GameCamera>(FindObjectsSortMode.None)[0];
        //遷移画面の管理
        sceneManager = FindAnyObjectByType<GameSceneManager>();

        gameCursor = Object.FindObjectsByType<GameCursor>(FindObjectsSortMode.None)[0];

        // ルールの登録
        gameRules.Add(new BaseMoveRule());
        gameRules.Add(new EmptyCellDropRule());
        gameRules.Add(new NoLegalMoveDropRule());
        gameRules.Add(new NifuDropRule());
    }

    // Update is called once per frame
    void Update()
    {
        //TODO: ターン管理
        switch (currentState)
        {
            case GameState.Preparation:
                DecideTurn();
                if (HandUIManager.Instance != null)
                {
                    PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
                    var activeHand = IsPlayer1Turn() ? player1Hand : player2Hand;
                    HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, activeHand);
                }
                break;
            case GameState.Player1Turn:
            case GameState.Player2Turn:
                HandleTurnUpdate();
                break;
            case GameState.GameOver:
                // ゲームオーバーの処理
                gameBoard.AllDeactivePanel();
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText("");
                }
                if (winner == PlayerType.Player1)
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

    void HandleTurnUpdate()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 成り確認ダイアログの応答待ち中はキー入力を無視する
        if (isWaitingForPromoteChoice) return;

        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

        // オンライン対戦時は相手のターンの操作を制限する
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch)
        {
            if (activePlayer != NetworkManager.Instance.MyPlayerType)
            {
                return;
            }
        }

        if (isSelectingHand)
        {
            // 持ち駒選択モード
            bool selectPrev = keyboard.aKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame || 
                              keyboard.leftArrowKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
            bool selectNext = keyboard.dKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame || 
                              keyboard.rightArrowKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;

            if (selectPrev)
            {
                selectedHandIndex--;
                UpdateSelectingHandUI();
            }
            else if (selectNext)
            {
                selectedHandIndex++;
                UpdateSelectingHandUI();
            }
            else if (gameController.IsOkTrigger())
            {
                if (availableHandTypes.Count > 0)
                {
                    isSelectingHand = false;
                    isDroppingHand = true;
                    string jpName = HandUIManager.GetJapaneseName(selectedHandType);
                    HandUIManager.Instance.SetStatusText($"【打つ場所を選択】 {jpName} (決定: F / 取消: Space)");
                    ShowDroppablePanels(selectedHandType);
                }
            }
            else if (gameController.IsCancelTrigger())
            {
                isSelectingHand = false;
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText("");
                    HandUIManager.Instance.HidePanel();
                }
                gameBoard.AllDeactivePanel();
            }
        }
        else if (isDroppingHand)
        {
            // 持ち駒打つ位置選択モード
            if (gameController.IsOkTrigger())
            {
                int x = gameCursor.X;
                int y = gameCursor.Y;
                if (CanDropPieceTo(activePlayer, selectedHandType, x, y))
                {
                    if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch)
                    {
                        NetworkManager.Instance.SendDropRequest(selectedHandType, x, y);
                        isDroppingHand = false;
                        isSelectingHand = false;
                        gameBoard.AllDeactivePanel();
                    }
                    else
                    {
                        DropPiece(selectedHandType, x, y);
                        NextState();
                    }
                }
                else
                {
                    Debug.Log("そこには配置できません（将棋ルール違反、またはすでに駒があります）");
                }
            }
            else if (gameController.IsCancelTrigger())
            {
                isDroppingHand = false;
                isSelectingHand = true;
                UpdateHandList();
                UpdateSelectingHandUI();
            }
        }
        else
        {
            // 通常モード
            if (keyboard.hKey.wasPressedThisFrame)
            {
                UpdateHandList();
                if (availableHandTypes.Count > 0)
                {
                    isSelectingHand = true;
                    selectedHandIndex = 0;
                    UpdateSelectingHandUI();
                    if (HandUIManager.Instance != null)
                    {
                        var hand = IsPlayer1Turn() ? player1Hand : player2Hand;
                        HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
                        HandUIManager.Instance.ShowPanel();
                    }
                }
                else
                {
                    Debug.Log("持ち駒がありません。");
                }
            }
            else if (gameController.IsOkTrigger())
            {
                HandlePieceSelection();
            }
            else if (gameController.IsCancelTrigger())
            {
                selectedPiece = null;
                gameBoard.AllDeactivePanel();
            }
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
                // 成り判定
                if (CanPromote(selectedPiece, selectedPiece.X, x))
                {
                    if (MustPromote(selectedPiece, x))
                    {
                        // 強制成り
                        ConfirmMove(selectedPiece.X, selectedPiece.Y, x, y, true);
                    }
                    else
                    {
                        // 任意成り：選択ダイアログ表示へ
                        StartPromotionChoice(selectedPiece.X, selectedPiece.Y, x, y);
                    }
                }
                else
                {
                    // 成りなしで移動を確定
                    ConfirmMove(selectedPiece.X, selectedPiece.Y, x, y, false);
                }
            }
            else
            {
                Debug.Log("そこには移動できません");
            }
        }
    }

    void ConfirmMove(int fromX, int fromY, int toX, int toY, bool promote)
    {
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch)
        {
            NetworkManager.Instance.SendMoveRequest(fromX, fromY, toX, toY, promote);
            selectedPiece = null;
            gameBoard.AllDeactivePanel();
        }
        else
        {
            GamePiece piece = gameBoard.GetPieceAt(fromX, fromY);
            if (piece != null)
            {
                ExecuteLocalMove(piece, toX, toY, promote);
            }
        }
    }

    void StartPromotionChoice(int fromX, int fromY, int toX, int toY)
    {
        isWaitingForPromoteChoice = true;
        pendingFromX = fromX;
        pendingFromY = fromY;
        pendingToX = toX;
        pendingToY = toY;

        if (promoteConfirmPanel != null)
        {
            promoteConfirmPanel.SetActive(true);
        }
        else
        {
            // パネルがアタッチされていない場合のフォールバック（自動成り）
            ResolvePromotionChoice(true);
        }
    }

    public void OnPromoteConfirmYes()
    {
        if (!isWaitingForPromoteChoice) return;
        ResolvePromotionChoice(true);
    }

    public void OnPromoteConfirmNo()
    {
        if (!isWaitingForPromoteChoice) return;
        ResolvePromotionChoice(false);
    }

    void ResolvePromotionChoice(bool promote)
    {
        isWaitingForPromoteChoice = false;
        if (promoteConfirmPanel != null)
        {
            promoteConfirmPanel.SetActive(false);
        }
        ConfirmMove(pendingFromX, pendingFromY, pendingToX, pendingToY, promote);
    }

    bool CanPromote(GamePiece piece, int fromX, int toX)
    {
        if (piece.isPromoted || piece.type == PieceType.King || piece.type == PieceType.GoldGeneral)
        {
            return false;
        }

        // 先手敵陣: X >= 6, 後手敵陣: X <= 2
        bool fromInEnemy = (piece.player == PlayerType.Player1) ? (fromX >= 6) : (fromX <= 2);
        bool toInEnemy = (piece.player == PlayerType.Player1) ? (toX >= 6) : (toX <= 2);

        return fromInEnemy || toInEnemy;
    }

    bool MustPromote(GamePiece piece, int toX)
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

    void ExecuteLocalMove(GamePiece piece, int targetX, int targetY, bool promote)
    {
        bool isGameOver = false;
        GamePiece targetPiece = gameBoard.GetPieceAt(targetX, targetY);
        if (targetPiece != null)
        {
            if (targetPiece.player != piece.player)
            {
                AddPieceToHand(piece.player, targetPiece.type);
                isGameOver = gameBoard.RemovePieceAt(targetX, targetY);
            }
        }
        gameBoard.UpdateBoardData(piece.X, piece.Y, targetX, targetY);

        piece.X = targetX;
        piece.Y = targetY;
        piece.MoveTo(GameHelper.CalcPanelLocation(targetX, targetY));

        if (promote)
        {
            piece.Promote();
        }

        if (isGameOver)
        {
            winner = piece.player;
            currentState = GameState.GameOver;
            return;
        }
        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        NextState();
    }

    public void OnServerMovePiece(int fromX, int fromY, int toX, int toY, PlayerType activePlayer, bool promote)
    {
        GamePiece piece = gameBoard.GetPieceAt(fromX, fromY);
        if (piece == null) return;

        bool isGameOver = false;
        GamePiece targetPiece = gameBoard.GetPieceAt(toX, toY);
        if (targetPiece != null)
        {
            if (targetPiece.player != piece.player)
            {
                AddPieceToHand(piece.player, targetPiece.type);
                isGameOver = gameBoard.RemovePieceAt(toX, toY);
            }
        }
        gameBoard.UpdateBoardData(fromX, fromY, toX, toY);

        piece.X = toX;
        piece.Y = toY;
        piece.MoveTo(GameHelper.CalcPanelLocation(toX, toY));

        if (promote)
        {
            piece.Promote();
        }

        if (isGameOver)
        {
            winner = piece.player;
            currentState = GameState.GameOver;
            return;
        }

        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        NextState();
    }

    public void OnServerDropPiece(PieceType type, int toX, int toY, PlayerType activePlayer)
    {
        var hand = (activePlayer == PlayerType.Player1) ? player1Hand : player2Hand;

        if (hand.ContainsKey(type) && hand[type] > 0)
        {
            hand[type]--;
        }

        gameBoard.SpawnPiece(type, activePlayer, toX, toY);

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
            HandUIManager.Instance.SetStatusText("");
            HandUIManager.Instance.HidePanel();
        }

        isDroppingHand = false;
        isSelectingHand = false;
        gameBoard.AllDeactivePanel();
        NextState();
    }

    public void OnServerGameOver(PlayerType serverWinner)
    {
        winner = serverWinner;
        currentState = GameState.GameOver;
    }

    void AddPieceToHand(PlayerType player, PieceType type)
    {
        if (type == PieceType.King) return;

        var hand = (player == PlayerType.Player1) ? player1Hand : player2Hand;
        if (hand.ContainsKey(type))
        {
            hand[type]++;
        }
        if (HandUIManager.Instance != null)
        {
            PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
            var activeHand = IsPlayer1Turn() ? player1Hand : player2Hand;
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, activeHand);
        }
    }

    private void UpdateHandList()
    {
        availableHandTypes.Clear();
        var hand = IsPlayer1Turn() ? player1Hand : player2Hand;
        foreach (var kvp in hand)
        {
            if (kvp.Value > 0)
            {
                availableHandTypes.Add(kvp.Key);
            }
        }
    }

    private void UpdateSelectingHandUI()
    {
        if (availableHandTypes.Count == 0)
        {
            isSelectingHand = false;
            if (HandUIManager.Instance != null)
            {
                HandUIManager.Instance.SetStatusText("");
            }
            return;
        }

        if (selectedHandIndex < 0) selectedHandIndex = availableHandTypes.Count - 1;
        if (selectedHandIndex >= availableHandTypes.Count) selectedHandIndex = 0;

        selectedHandType = availableHandTypes[selectedHandIndex];
        string jpName = HandUIManager.GetJapaneseName(selectedHandType);

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【持ち駒選択中】 {jpName} \n(決定: F / 取消: Space / 選択: A・D または W・S)");
        }
    }

    private bool CanDropPieceTo(PlayerType player, PieceType type, int targetX, int targetY)
    {
        foreach (var rule in gameRules)
        {
            if (!rule.IsLegalDrop(type, player, targetX, targetY, gameBoard))
            {
                return false;
            }
        }
        return true;
    }

    private void ShowDroppablePanels(PieceType type)
    {
        gameBoard.AllDeactivePanel();
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

        for (int tx = 0; tx < 9; tx++)
        {
            for (int ty = 0; ty < 9; ty++)
            {
                if (CanDropPieceTo(activePlayer, type, tx, ty))
                {
                    gameBoard.ActivePanel(GameHelper.CalcPanelNum(tx, ty));
                }
            }
        }
    }

    private void DropPiece(PieceType type, int x, int y)
    {
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        var hand = IsPlayer1Turn() ? player1Hand : player2Hand;

        if (hand.ContainsKey(type) && hand[type] > 0)
        {
            hand[type]--;
        }

        gameBoard.SpawnPiece(type, activePlayer, x, y);

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
            HandUIManager.Instance.SetStatusText("");
            HandUIManager.Instance.HidePanel();
        }

        isDroppingHand = false;
        isSelectingHand = false;
        gameBoard.AllDeactivePanel();
    }

    bool IsMyPiece(GamePiece piece)
    {
        if (currentState == GameState.Player1Turn && piece.player == PlayerType.Player1) return true;
        if (currentState == GameState.Player2Turn && piece.player == PlayerType.Player2) return true;
        return false;
    }

    private bool CanPieceMoveTo(GamePiece piece, int targetX, int targetY)
    {
        foreach (var rule in gameRules)
        {
            if (!rule.IsLegalMove(piece, targetX, targetY, gameBoard))
            {
                return false;
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
        // ターン切り替え時に状態をクリア
        isSelectingHand = false;
        isDroppingHand = false;
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText("");
            // 交代後のプレイヤーの手持ち表示に更新
            PlayerType activePlayer = (currentState == GameState.Player1Turn) ? PlayerType.Player2 : PlayerType.Player1;
            var hand = (activePlayer == PlayerType.Player1) ? player1Hand : player2Hand;
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
        }

        switch (currentState)
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
        if (Random.value < 0.5f)
        {
            currentState = GameState.Player1Turn;
            Debug.Log("プレイヤー1のターンです！");
            gameCamera.RotateToPlayer(PlayerType.Player1);
        }
        else
        {
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
