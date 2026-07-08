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

    [Header("Visual & Interaction Constraints Settings")]
    [SerializeField] private GameObject darkOverlay; // 画面を暗くする半透明パネル
    [SerializeField] private RectTransform promoteYesButtonRect; // 成る(Yes)ボタン
    [SerializeField] private RectTransform promoteNoButtonRect;  // 成らない(No)ボタン
    private bool promoteChoice = true;

    private List<Vector2Int> currentMovablePositions = new List<Vector2Int>();
    public List<Vector2Int> CurrentMovablePositions => currentMovablePositions;
    public bool IsDroppingHand => isDroppingHand;
    public GamePiece SelectedPiece => selectedPiece;

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
                gameBoard.ChangeSkillOnOff(0);
                sceneManager.GameOver();
                break;
        }
    }

    void HandleTurnUpdate()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 成り確認ダイアログの応答待ち中のキー操作
        if (isWaitingForPromoteChoice)
        {
            bool toggleChoice = keyboard.aKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame ||
                                keyboard.sKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame ||
                                keyboard.leftArrowKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame ||
                                keyboard.downArrowKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;

            if (toggleChoice)
            {
                promoteChoice = !promoteChoice;
                UpdatePromoteChoiceVisuals();
            }

            if (gameController.IsOkTrigger())
            {
                ResolvePromotionChoice(promoteChoice);
            }
            return;
        }

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

            if (availableHandTypes.Count > 0)
            {
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
                    isSelectingHand = false;
                    isDroppingHand = true;
                    string jpName = HandUIManager.GetJapaneseName(selectedHandType);
                    HandUIManager.Instance.SetStatusText($"【打つ場所を選択】 {jpName} (決定: F / 取消: Space)");
                    ShowDroppablePanels(selectedHandType);
                }
            }

            if (gameController.IsCancelTrigger())
            {
                isSelectingHand = false;
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText("");
                    HandUIManager.Instance.HidePanel();
                }
                gameBoard.AllDeactivePanel();
                SetDarkOverlayActive(false);
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
                ClearBoardHighlight();
                currentMovablePositions.Clear();
            }
        }
        else
        {
            // 通常モード
            if (keyboard.hKey.wasPressedThisFrame)
            {
                UpdateHandList();
                isSelectingHand = true;
                selectedHandIndex = 0;
                UpdateSelectingHandUI();
                if (HandUIManager.Instance != null)
                {
                    var hand = IsPlayer1Turn() ? player1Hand : player2Hand;
                    HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
                    HandUIManager.Instance.ShowPanel();
                }
                SetDarkOverlayActive(true);
            }
            else if (gameController.IsOkTrigger())
            {
                HandlePieceSelection();
            }
            else if (gameController.IsCancelTrigger())
            {
                selectedPiece = null;
                gameBoard.AllDeactivePanel();
                ClearBoardHighlight();
                SetDarkOverlayActive(false);
                currentMovablePositions.Clear();
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
            ClearBoardHighlight();
            SetDarkOverlayActive(false);
            currentMovablePositions.Clear();
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

        promoteChoice = true; // 初期選択は「成る(Yes)」
        UpdatePromoteChoiceVisuals();

        // 盤面全体のハイライトとマスのハイライトを一旦クリア
        ClearBoardHighlight();

        // 成りになれる駒（選択されている駒）だけを光らせる
        if (selectedPiece != null)
        {
            selectedPiece.SetHighlight(true);
            highlightedPieces.Add(selectedPiece);
        }

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

    // 追加ターン（2回連続行動）のフラグ。初期値は false
    private bool hasExtraTurn = false;

    // スキル（DoubleTurnEffect）から呼ばれるメソッド
    public void GrantExtraTurn(PlayerType player)
    {
        hasExtraTurn = true;
        Debug.Log($"{player} に追加行動権が与えられました。");
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

                PlayerType opponent = targetPiece.player;
                if (!IsKingAlive(opponent))
                {
                    //盤面に相手の王が一枚も残っていない場合、ゲームオーバーとする
                    isGameOver = true;
                }
                else
                {
                    isGameOver = false;
                }
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
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
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


                PlayerType opponent = targetPiece.player;
                if (!IsKingAlive(opponent))
                {
                    //盤面に相手の王が一枚も残っていない場合、ゲームオーバーとする
                    isGameOver = true;
                }
                else
                {
                    isGameOver = false;
                }
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
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
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
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
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
            if (HandUIManager.Instance != null)
            {
                HandUIManager.Instance.SetStatusText("持ち駒がありません \n(取消: Space)");
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
        currentMovablePositions.Clear();
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

        for (int tx = 0; tx < 9; tx++)
        {
            for (int ty = 0; ty < 9; ty++)
            {
                if (CanDropPieceTo(activePlayer, type, tx, ty))
                {
                    gameBoard.ActivePanel(GameHelper.CalcPanelNum(tx, ty));
                    currentMovablePositions.Add(new Vector2Int(tx, ty));
                }
            }
        }

        // 画面を暗くし、打てるマスをハイライト（打つ時はselectedはnull）
        ApplyBoardHighlight(null, currentMovablePositions);

        // カーソル初期位置を最初の打てるマスへ移動
        if (currentMovablePositions.Count > 0 && gameCursor != null)
        {
            gameCursor.SetPosition(currentMovablePositions[0].x, currentMovablePositions[0].y);
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
        ClearBoardHighlight();
        currentMovablePositions.Clear();
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
        currentMovablePositions.Clear();

        // 元のマスもカーソルが戻れるよう移動可能リストに追加する
        currentMovablePositions.Add(new Vector2Int(piece.X, piece.Y));

        for (int tx = 0; tx < 9; tx++)
        {
            for (int ty = 0; ty < 9; ty++)
            {
                if (CanPieceMoveTo(piece, tx, ty))
                {
                    gameBoard.ActivePanel(GameHelper.CalcPanelNum(tx, ty));
                    currentMovablePositions.Add(new Vector2Int(tx, ty));
                }
            }
        }

        // 画面を暗くし、選択した駒と敵の駒を光らせる
        ApplyBoardHighlight(piece, currentMovablePositions);
    }


    void NextState()
    {
        if (hasExtraTurn)
        {
            hasExtraTurn = false;
            isSelectingHand = false;
            isDroppingHand = false;
            Debug.Log("追加行動権により、同じプレイヤーのターンが続きます。");
            return; // ターンを切り替えずに同じプレイヤーのターンを続ける
        }
        // ターン切り替え時に状態をクリア
        isSelectingHand = false;
        isDroppingHand = false;
        if (gameBoard != null)
        {
            gameBoard.ChangeSkillOnOff(0); // スキルをリセット
        }
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

    public void UseSkill(PlayerType player)
    {
        // 自分のターンであるかチェック
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        if (player != activePlayer)
        {
            Debug.LogWarning("相手のターンにはスキルを使用できません。");
            return;
        }

        if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch)
        {
            // オンライン対戦時はサーバーへ要求を送信
            NetworkManager.Instance.SendUseSkillRequest();
        }
        else
        {
            // ローカル対戦時は直接発動
            if (gameBoard != null)
            {
                gameBoard.ChangeSkillOnOff(1);
            }
            Debug.Log($"ローカルスキル発動: {player}");
            if (HandUIManager.Instance != null)
            {
                HandUIManager.Instance.SetStatusText($"【スキル発動！】 相手の飛車を取れば勝利します");
            }
        }
    }

    public void OnServerUseSkill(PlayerType activePlayer)
    {
        if (gameBoard != null)
        {
            gameBoard.ChangeSkillOnOff(1);
        }
        Debug.Log($"プレイヤー {(activePlayer == PlayerType.Player1 ? "1" : "2")} がスキルを使用しました！ (飛車が王将扱いになります)");
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【スキル発動中！】 相手の飛車を取れば勝利します");
        }
    }



    private Color originalAmbientColor;
    private bool isAmbientSaved = false;
    private List<GamePiece> highlightedPieces = new List<GamePiece>();

    void SetDarkOverlayActive(bool active)
    {
        if (darkOverlay != null)
        {
            darkOverlay.SetActive(active);
        }
        else if (active)
        {
            if (!isAmbientSaved)
            {
                originalAmbientColor = RenderSettings.ambientLight;
                isAmbientSaved = true;
                RenderSettings.ambientLight = Color.gray * 0.25f; // 暗転
            }
        }
        else
        {
            if (isAmbientSaved)
            {
                RenderSettings.ambientLight = originalAmbientColor;
                isAmbientSaved = false;
            }
        }
    }

    // 画面を暗くし、関係する駒を光らせる
    void ApplyBoardHighlight(GamePiece selected, List<Vector2Int> movablePos)
    {
        // 1. 暗転
        SetDarkOverlayActive(true);

        // 2. 選択された駒を光らせる
        if (selected != null)
        {
            selected.SetHighlight(true);
            highlightedPieces.Add(selected);
        }

        // 3. 移動可能マスの敵の駒を光らせる
        foreach (var pos in movablePos)
        {
            GamePiece target = gameBoard.GetPieceAt(pos.x, pos.y);
            if (target != null && selected != null && target.player != selected.player)
            {
                target.SetHighlight(true);
                highlightedPieces.Add(target);
            }
        }
    }

    // 指定したプレイヤーの王将が盤面にまだ残っているかをチェックする
    private bool IsKingAlive(PlayerType player)
    {
        // 9x9の盤面をすべてループして探す
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 9; y++)
            {
                GamePiece p = gameBoard.GetPieceAt(x, y);
                // 自分の駒、かつ王将(King)が見つかったら生存！
                if (p != null && p.player == player && p.type == PieceType.King)
                {
                    return true;
                }
            }
        }
        return false; // 1枚も見つからなければ死亡
    }

    // 暗転とハイライトを解除する
    void ClearBoardHighlight()
    {
        // 駒のハイライト解除
        foreach (var piece in highlightedPieces)
        {
            if (piece != null)
            {
                piece.SetHighlight(false);
            }
        }
        highlightedPieces.Clear();
    }

    private void UpdatePromoteChoiceVisuals()
    {
        if (promoteYesButtonRect != null)
        {
            promoteYesButtonRect.localScale = promoteChoice ? Vector3.one * 1.15f : Vector3.one;
        }
        if (promoteNoButtonRect != null)
        {
            promoteNoButtonRect.localScale = !promoteChoice ? Vector3.one * 1.15f : Vector3.one;
        }
    }
}

enum GameState
{
    Preparation, // ゲームの準備中
    Player1Turn, // プレイヤー1のターン
    Player2Turn,  // プレイヤー2のターン
    GameOver    // ゲームオーバー
}
