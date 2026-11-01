using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using TMPro;

// ターン管理やゲームの準備を行う
public class GameSystem : MonoBehaviour
{
    private GameState currentState;
    private PlayerType winner; // 勝者のプレイヤータイプを保存する変数
    private bool isDraw = false; // 千日手などによる引き分け
    private bool isResultLoading = false; // 結果画面への遷移を開始したか
    private GameBoard gameBoard;
    private GameCamera gameCamera;
    private GameCursor gameCursor;
    private GamePiece selectedPiece; // 現在選択されている駒
    public GameSceneManager sceneManager;
    [SerializeField] private GameController gameController;
    [SerializeField] private PieceMoveAnimator moveAnimator; // 未設定ならシーンから探す（無ければ自動で作る）

    [Header("Players")]
    [SerializeField] private Player player1;
    [SerializeField] private Player player2;

    public Player CurrentPlayer => IsPlayer1Turn() ? player1 : player2;

    // 新しいルールの状態（ターン数・エナジー・倒された初期配置の駒）。MonoBehaviour から切り離したプレーンなクラス
    private MatchState match;
    public MatchState Match => match;

    // 後手の手番が終わったとき（1ターンの終わり）。引数は終わったターン数。手番交代の前に呼ばれる
    public event System.Action<int> RoundEnded;

    // 駒が倒された（取り・消失）あと。駒の GameObject はもう破棄されているので、PieceDefeat の Type・X・Y などを使う
    public event System.Action<PieceDefeat> PieceDefeated;

    // 駒を倒す処理への割り込み（盾・時間停止・アンデッドなど）
    private readonly List<IPieceDefeatInterceptor> defeatInterceptors = new List<IPieceDefeatInterceptor>();

    // 手番の始まりに出すお知らせ（敵陣への打ち込み解禁など）
    private string turnNotice = "";

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

    // 王手の状態（王将が1枚のときのみ発生）
    private bool isActivePlayerInCheck = false;

    // 千日手判定用の局面の記録
    private struct PositionRecord
    {
        public string key;
        public PlayerType sideToMove;
        public bool inCheck;
    }
    private List<PositionRecord> positionHistory = new List<PositionRecord>();
    private Dictionary<string, int> positionCounts = new Dictionary<string, int>();
    private static readonly PieceType[] HandOrder =
    {
        PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral,
        PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook
    };

    // スキルの対象選択（爆発の中心・駒入替の駒など）
    private bool isSelectingSkillTarget = false;
    private SkillDefinition pendingSkill;
    private SkillContext pendingSkillContext;
    private Vector2Int lastPreviewCursor;

    public bool IsSelectingSkillTarget => isSelectingSkillTarget;

    [System.Serializable]
    public struct SkillButtonUI
    {
        public UnityEngine.UI.Button button;
        public TextMeshProUGUI nameText;
        public TextMeshProUGUI costText;
    }
    [Header("Shared UI References")]
    [SerializeField] private SkillButtonUI[] skillSlotUIs; // 要素数5の共通UIスロット

    // 毎フレーム演出（GameTickEffects）用
    public IEnumerable<UnityEngine.UI.Button> SkillSlotButtons
    {
        get
        {
            if (skillSlotUIs == null) yield break;
            foreach (var slot in skillSlotUIs) yield return slot.button;
        }
    }
    public bool IsInTurn => currentState == GameState.Player1Turn || currentState == GameState.Player2Turn;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        currentState = GameState.Preparation;
        GameResult.Winner = null;
        GameResult.IsDraw = false;

        gameBoard = Object.FindObjectsByType<GameBoard>(FindObjectsSortMode.None)[0];
        gameCamera = Object.FindObjectsByType<GameCamera>(FindObjectsSortMode.None)[0];
        //遷移画面の管理
        sceneManager = FindAnyObjectByType<GameSceneManager>();

        gameCursor = Object.FindObjectsByType<GameCursor>(FindObjectsSortMode.None)[0];

        if (moveAnimator == null) moveAnimator = FindAnyObjectByType<PieceMoveAnimator>();
        if (moveAnimator == null) moveAnimator = new GameObject("PieceMoveAnimator").AddComponent<PieceMoveAnimator>();

        // 毎フレームの演出（シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<GameTickEffects>() == null)
        {
            gameObject.AddComponent<GameTickEffects>();
        }

        // 陣地の色分けと境界線（シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<BoardZoneView>() == null)
        {
            gameObject.AddComponent<BoardZoneView>();
        }

        // マスの状態（破壊・炎上・落とし穴）の見た目と、動作確認用のデバッグキー（シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<BoardCellView>() == null)
        {
            gameObject.AddComponent<BoardCellView>();
        }
        if (FindAnyObjectByType<CellDebugTool>() == null)
        {
            gameObject.AddComponent<CellDebugTool>();
        }

        // 対局の状態（ターン数・エナジー）
        match = new MatchState(MatchConfig.FirstPlayer);
        match.Turn.RoundEnded += OnRoundEnd;
        if (player1 != null) player1.BindEnergy(match.Energy);
        if (player2 != null) player2.BindEnergy(match.Energy);

        // ルールの登録
        gameRules.Add(new BaseMoveRule());
        gameRules.Add(new EmptyCellDropRule());
        gameRules.Add(new CellStateDropRule()); // 破壊マス・落とし穴には打てない
        gameRules.Add(new NoLegalMoveDropRule());
        gameRules.Add(new NifuDropRule());
        gameRules.Add(new EnemyZoneDropLockRule(match.Turn));
        gameRules.Add(new UchifuzumeRule()); // 他のルールを満たした手だけを調べるので最後に置く

        // デッキの初期化
        if (DeckTransferManager.Player1SelectedDeck != null && player1 != null)
        {
            player1.SetSkillDeck(DeckTransferManager.Player1SelectedDeck);
        }
        if (DeckTransferManager.Player2SelectedDeck != null && player2 != null)
        {
            player2.SetSkillDeck(DeckTransferManager.Player2SelectedDeck);
        }

        // スキルボタンのクリックイベントを登録
        for (int i = 0; i < skillSlotUIs.Length; i++)
        {
            int index = i; // ラムダ式のクロージャ用
            if (skillSlotUIs[i].button != null)
            {
                skillSlotUIs[i].button.onClick.AddListener(() => OnSkillSlotClicked(index));
            }
        }
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
                    var activeHand = CurrentPlayer.Hand;
                    HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, activeHand);
                }
                break;
            case GameState.Player1Turn:
            case GameState.Player2Turn:
                HandleTurnUpdate();
                break;
            case GameState.GameOver:
                // ゲームオーバーの処理（シーン遷移は1回だけ行う）
                if (isResultLoading) break;
                isResultLoading = true;

                gameBoard.AllDeactivePanel();
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText("");
                }
                if (isDraw)
                {
                    Debug.Log("引き分け！");
                }
                else if (winner == PlayerType.Player1)
                {
                    Debug.Log("プレイヤー1の勝利！");
                }
                else
                {
                    Debug.Log("プレイヤー2の勝利！");
                }
                // 結果画面に勝敗を渡す
                GameResult.IsDraw = isDraw;
                GameResult.Winner = isDraw ? (PlayerType?)null : winner;
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
            else if (keyboard.jKey.wasPressedThisFrame)
            {
                // J で「成らない」を選ぶ
                ResolvePromotionChoice(false);
            }
            else if (gameController.IsCancelTrigger())
            {
                // Space でこの駒の移動自体を取り消す
                CancelPromotionChoice();
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

        if (isSelectingSkillTarget)
        {
            HandleSkillTargetSelection();
            return;
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
                    HandUIManager.Instance.HidePanel();
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
                    HandUIManager.Instance.SetStatusText(IdleStatusText);
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
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.ShowPanel();
                }
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
                    var hand = CurrentPlayer.Hand;
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
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText("【成りますか？】 決定: F / 成らない: J / 取消: Space / 選択: 矢印キー・WASD");
        }

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

    // 成り確認を閉じて、駒の選択前の状態に戻す（移動しない）
    void CancelPromotionChoice()
    {
        isWaitingForPromoteChoice = false;
        if (promoteConfirmPanel != null)
        {
            promoteConfirmPanel.SetActive(false);
        }
        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText(IdleStatusText);
        }
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

        // 敵陣（先手: X >= 6, 後手: X <= 2）に入る・敵陣から出る
        bool fromInEnemy = BoardZones.IsEnemyZone(piece.player, fromX);
        bool toInEnemy = BoardZones.IsEnemyZone(piece.player, toX);

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
        if (ApplyMove(piece, targetX, targetY, promote)) return; // 対局が終わった

        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
        NextState();
    }

    // 駒の移動（相手の駒がいれば取り）。盤面のデータを変え、見た目の移動を始める。対局が終わったら true
    bool ApplyMove(GamePiece piece, int targetX, int targetY, bool promote)
    {
        GamePiece targetPiece = gameBoard.GetPieceAt(targetX, targetY);
        if (targetPiece != null && targetPiece.player != piece.player)
        {
            if (!CapturePiece(targetPiece, piece.player))
            {
                // 取りが防がれた（盾など）：駒は元のマスに残り、手番は消費する
                return false;
            }
        }
        gameBoard.UpdateBoardData(piece.X, piece.Y, targetX, targetY);

        piece.X = targetX;
        piece.Y = targetY;
        AnimatePieceMove(piece, GameHelper.CalcPanelLocation(targetX, targetY));

        // 炎上・落とし穴のマスに入ったら消失する（成りも起きない）
        if (!EnterCell(piece, targetX, targetY))
        {
            return ResolveKingLoss();
        }

        if (promote)
        {
            piece.Promote();
        }

        return ResolveKingLoss();
    }

    // 指し手の見た目の移動（盤面データは即時に更新済み。アニメーションは見た目だけ）
    void AnimatePieceMove(GamePiece piece, Vector2 location)
    {
        if (moveAnimator == null)
        {
            piece.MoveTo(location);
            return;
        }
        // 成る前の状態で動き方を決める（成っていない桂馬は跳ぶ）
        moveAnimator.PlayMove(piece, location, PieceMoveAnimator.StyleFor(piece));
    }

    public void OnServerMovePiece(int fromX, int fromY, int toX, int toY, PlayerType activePlayer, bool promote)
    {
        GamePiece piece = gameBoard.GetPieceAt(fromX, fromY);
        if (piece == null) return;

        if (ApplyMove(piece, toX, toY, promote)) return; // 対局が終わった

        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
        NextState();
    }

    public void OnServerDropPiece(PieceType type, int toX, int toY, PlayerType activePlayer)
    {
        Player targetPlayer = (activePlayer == PlayerType.Player1) ? player1 : player2;
        targetPlayer.RemovePieceFromHand(type);
        var hand = targetPlayer.Hand;

        GamePiece dropped = gameBoard.SpawnPiece(type, activePlayer, toX, toY);
        if (dropped != null) EnterCell(dropped, toX, toY); // 炎上のマスに打ったら消失する

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

        Player targetPlayer = (player == PlayerType.Player1) ? player1 : player2;
        targetPlayer.AddPieceToHand(type);
        if (HandUIManager.Instance != null)
        {
            PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
            var activeHand = CurrentPlayer.Hand;
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, activeHand);
        }
    }

    // エナジー（旧ポイント）の加算。手番の始まり・駒が取られたとき・スキル（GainCostEffect）から呼ばれる
    public void AddPoint(PlayerType player, int amount)
    {
        if (match == null || amount <= 0) return;
        match.Energy.Gain(player, amount); // 表示は Player.BindEnergy で PointManager に反映される
        UpdateActivePlayerSkillUI();
    }

    // ------------------------------------------------------------
    //  駒を倒す処理の共通入口（取り・消失）
    //  盤上の駒を倒すときは、必ずこの2つを通すこと（盾・時間停止・アンデッドがここで割り込む）
    // ------------------------------------------------------------

    public void AddDefeatInterceptor(IPieceDefeatInterceptor interceptor)
    {
        if (interceptor != null && !defeatInterceptors.Contains(interceptor)) defeatInterceptors.Add(interceptor);
    }

    public void RemoveDefeatInterceptor(IPieceDefeatInterceptor interceptor)
    {
        defeatInterceptors.Remove(interceptor);
    }

    // 取り：by が piece を取る。エナジーは取られた側が駒の価値の全額、取った側が半分（切り捨て）を得る。
    // 取った駒は by の持ち駒になる（王将は持ち駒にならない）。取れたら true（盾などで防がれたら false）。
    // 王将が全部取られたかの判定は呼び出し側で行う（ResolveKingLoss）
    public bool CapturePiece(GamePiece piece, PlayerType by)
    {
        if (!IsOnBoard(piece)) return false;

        PieceDefeat defeat = new PieceDefeat(piece, DefeatKind.Capture, by, "取り");
        if (IsDefeatIntercepted(defeat)) return false;

        AddPoint(defeat.Owner, PieceValues.CapturedSideGain(defeat.Type));
        AddPoint(by, PieceValues.CapturerGain(defeat.Type));
        RemoveDefeatedPiece(defeat);
        AddPieceToHand(by, defeat.Type); // 成った駒も元の駒として持ち駒になる
        return true;
    }

    // 消失：エナジー・持ち駒化なしで盤から消える（爆発など）。消えたら true（割り込みで止められたら false）
    public bool VanishPiece(GamePiece piece, string reason)
    {
        if (!IsOnBoard(piece)) return false;

        PieceDefeat defeat = new PieceDefeat(piece, DefeatKind.Vanish, null, reason);
        if (IsDefeatIntercepted(defeat)) return false;

        RemoveDefeatedPiece(defeat);
        return true;
    }

    bool IsOnBoard(GamePiece piece)
    {
        return piece != null && gameBoard != null && gameBoard.GetPieceAt(piece.X, piece.Y) == piece;
    }

    bool IsDefeatIntercepted(PieceDefeat defeat)
    {
        // 割り込みの中で登録が増減しても大丈夫なように、写しを回す
        foreach (IPieceDefeatInterceptor interceptor in defeatInterceptors.ToArray())
        {
            if (interceptor.TryIntercept(defeat)) return true;
        }
        return false;
    }

    // 取り・消失の共通処理：初期配置の駒なら記録 → 盤から取り除く → 通知
    void RemoveDefeatedPiece(PieceDefeat defeat)
    {
        if (defeat.WasInitial)
        {
            match.RecordFallenInitial(defeat.Owner, defeat.Type, defeat.Kind);
        }
        gameBoard.RemovePieceAt(defeat.X, defeat.Y);
        PieceDefeated?.Invoke(defeat);
    }

    // ------------------------------------------------------------
    //  マスの状態（破壊・炎上・落とし穴）の共通入口
    //  Prototype の effects.js の breakCell / setBurning / setTrap / enterCell。
    //  データは GameBoard.Cells（BoardCells）が持ち、見た目は BoardCellView が Cells.Changed を受けて更新する
    // ------------------------------------------------------------

    public BoardCells Cells => gameBoard != null ? gameBoard.Cells : null;

    // マスを破壊する（対局終了まで戻らない）。そこにいた駒は消失する。破壊したら true。
    // 王将が消えることもあるので、呼び出し側で ResolveKingLoss を呼ぶこと（スキルなら ExecuteSkill が呼ぶ）
    public bool BreakCell(int x, int y)
    {
        BoardCells cells = Cells;
        if (cells == null) return false;
        GamePiece piece = gameBoard.GetPieceAt(x, y);
        if (!cells.Break(x, y)) return false;
        if (piece != null) VanishPiece(piece, BoardCells.ReasonBroken);
        return true;
    }

    // マスを turns ターンのあいだ炎上させる（今のターンを 1 ターン目として数え、最後のターンの終わりに鎮火）。
    // すでにいる駒には影響せず、あとから入った駒が消失する。破壊マスは炎上しない。炎上させたら true
    public bool SetBurning(int x, int y, int turns)
    {
        BoardCells cells = Cells;
        if (cells == null || match == null) return false;
        return cells.SetBurning(x, y, turns, match.Turn.TurnNumber);
    }

    // 落とし穴を仕掛ける（owner は仕掛けたプレイヤー。穴は両プレイヤーに見える）。
    // 駒がいるマス・破壊マス・炎上のマスには置けない。仕掛けたら true
    public bool SetTrap(int x, int y, PlayerType owner)
    {
        BoardCells cells = Cells;
        if (cells == null || gameBoard.HasPieceAt(x, y)) return false;
        return cells.SetTrap(x, y, owner);
    }

    // 駒 piece が (x, y) に入ったときのマスの効果（移動・持ち駒を打つ・スキルで動かしたあとに呼ぶ）。
    //  落とし穴：敵味方に関係なく消失し、穴も消える
    //  炎上    ：消失する
    //  破壊    ：消失する（通常は入れない。念のため）
    // そのマスに残れたら true（false = 消失した）。消失は VanishPiece を通すので、盾などの割り込みで残ることもある
    public bool EnterCell(GamePiece piece, int x, int y)
    {
        BoardCells cells = Cells;
        BoardCell cell = cells != null ? cells.At(x, y) : null;
        if (piece == null || cell == null) return true;

        string reason = null;
        if (cell.HasTrap)
        {
            cells.SpringTrap(x, y);
            reason = BoardCells.ReasonTrap;
        }
        else if (cell.IsBurning)
        {
            reason = BoardCells.ReasonBurning;
        }
        else if (cell.IsBroken)
        {
            reason = BoardCells.ReasonBroken;
        }
        if (reason == null) return true;

        VanishPiece(piece, reason);
        return IsOnBoard(piece);
    }

    // 指し手・スキル以外で盤面が変わったあと（デバッグでマスを変えたときなど）に、勝敗・王手の表示を更新する
    public void RefreshAfterBoardChange()
    {
        if (!IsInTurn) return;
        if (ResolveKingLoss()) return;
        PlayerType side = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, side);
    }

    // 操作中でないときのステータス表示に、一時的なお知らせを添えて出す（デバッグ表示など）
    public void ShowIdleStatus(string message)
    {
        if (HandUIManager.Instance == null) return;
        string idle = IdleStatusText;
        HandUIManager.Instance.SetStatusText(string.IsNullOrEmpty(idle) ? message : message + "\n" + idle);
    }

    // 王将が全部いなくなったプレイヤーがいれば対局を終える（両方なら引き分け）。終わったら true。
    // 指し手・スキルのあとは GameSystem が呼ぶ。それ以外で駒を消したとき（マスの破壊など）は呼び出し側で呼ぶ
    public bool ResolveKingLoss()
    {
        bool p1Alive = IsKingAlive(PlayerType.Player1);
        bool p2Alive = IsKingAlive(PlayerType.Player2);
        if (p1Alive && p2Alive) return false;

        if (!p1Alive && !p2Alive)
        {
            isDraw = true;
            currentState = GameState.GameOver;
        }
        else
        {
            EndGame(p1Alive ? PlayerType.Player1 : PlayerType.Player2);
        }
        return true;
    }

    private void UpdateHandList()
    {
        availableHandTypes.Clear();
        var hand = CurrentPlayer.Hand;
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
        CurrentPlayer.RemovePieceFromHand(type);
        var hand = CurrentPlayer.Hand;

        GamePiece dropped = gameBoard.SpawnPiece(type, activePlayer, x, y);
        if (dropped != null) EnterCell(dropped, x, y); // 炎上のマスに打ったら消失する

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
        SetDarkOverlayActive(false);
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
            UpdateActivePlayerSkillUI(); // 獲得したポイントをスキルボタンに反映
            return; // ターンを切り替えずに同じプレイヤーのターンを続ける
        }
        // ターン切り替え時に状態をクリア
        isSelectingHand = false;
        isDroppingHand = false;
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText("");
            // 交代後のプレイヤーの手持ち表示に更新
            PlayerType activePlayer = (currentState == GameState.Player1Turn) ? PlayerType.Player2 : PlayerType.Player1;
            Player nextPlayer = (activePlayer == PlayerType.Player1) ? player1 : player2;
            var hand = nextPlayer.Hand;
            HandUIManager.Instance.UpdateActiveHandDisplay(activePlayer, hand);
        }

        // 手番を相手に渡す（後手の手番が終わったら、ここで 1ターンの終わり OnRoundEnd が呼ばれる）
        if (IsInTurn)
        {
            match.Turn.EndTurn();
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
        UpdateActivePlayerSkillUI();
        OnTurnStarted();
    }

    void DecideTurn()
    {
        // 先手は MatchConfig.FirstPlayer（Player1）で固定（Prototype と同じく、盤の手前側が先手）
        if (match.Turn.FirstPlayer == PlayerType.Player1)
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
        UpdateActivePlayerSkillUI();
        OnTurnStarted();
    }

    // 手番が始まったときの処理（ターン数・エナジー）と判定（詰み・千日手・王手の表示）
    void OnTurnStarted()
    {
        PlayerType side = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

        // 先手の番ならターン数を進める → エナジー +PointsPerTurn
        match.Turn.BeginTurn();
        turnNotice = "";
        if (side == match.Turn.FirstPlayer && match.Turn.IsDropUnlockTurn)
        {
            turnNotice = $"第{match.Turn.TurnNumber}ターン：敵陣への持ち駒の打ち込みが解禁された！";
            Debug.Log(turnNotice);
        }
        AddPoint(side, MatchConfig.PointsPerTurn);
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetTurnInfo(match.Turn.Label);
        }

        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, side);

        // 詰み（王将が1枚のときのみ。スキルは回避手に含めない）
        if (isActivePlayerInCheck && IsCheckmated(side))
        {
            Debug.Log($"詰み！ {side} の負け");
            EndGame(CheckRules.Opponent(side));
            return;
        }

        if (RecordPositionAndCheckRepetition(side, isActivePlayerInCheck))
        {
            return;
        }

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText(IdleStatusText);
        }
    }

    // 操作中でないときのステータス表示（王手中なら知らせる。手番の始まりのお知らせもここに出す）
    string IdleStatusText
    {
        get
        {
            string check = isActivePlayerInCheck ? "【王手！】 放置すると次の手で王将を取られます" : "";
            if (string.IsNullOrEmpty(turnNotice)) return check;
            if (string.IsNullOrEmpty(check)) return turnNotice;
            return check + "\n" + turnNotice;
        }
    }

    // 1ターンの終わり（後手の手番が終わったとき。手番交代の前）。
    // 期限切れの炎上を鎮火する。世界の終わりなど「ターンの終わりに起きること」はここに足す
    void OnRoundEnd(int turnNumber)
    {
        Debug.Log($"第{turnNumber}ターン終了");
        if (Cells != null)
        {
            List<Vector2Int> ended = Cells.ExtinguishExpired(turnNumber);
            if (ended.Count > 0) Debug.Log($"{ended.Count} マスの炎が鎮火した");
        }
        RoundEnded?.Invoke(turnNumber);
    }

    // 王手を回避できる手（駒の移動・持ち駒を打つ）が1つもないか。スキルは判定に含めない
    // 王手放置は禁止していないので、「指せる手」のうち王将が取られなくなる手があるかを調べる
    bool IsCheckmated(PlayerType side)
    {
        GamePiece[] cells = gameBoard.GetBoardData();
        for (int i = 0; i < cells.Length; i++)
        {
            GamePiece piece = cells[i];
            if (piece == null || piece.player != side) continue;

            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    if (CanPieceMoveTo(piece, x, y) && CheckRules.IsMoveSafe(gameBoard, piece, x, y)) return false;
                }
            }
        }

        Player sidePlayer = (side == PlayerType.Player1) ? player1 : player2;
        if (sidePlayer != null)
        {
            foreach (var kvp in sidePlayer.Hand)
            {
                if (kvp.Value <= 0) continue;
                for (int x = 0; x < 9; x++)
                {
                    for (int y = 0; y < 9; y++)
                    {
                        if (CanDropPieceTo(side, kvp.Key, x, y) && CheckRules.IsDropSafe(gameBoard, side, x, y)) return false;
                    }
                }
            }
        }
        return true;
    }

    // 局面を記録し、千日手（同じ局面が4回）になったら対局を終える
    bool RecordPositionAndCheckRepetition(PlayerType sideToMove, bool inCheck)
    {
        string key = BuildPositionKey(sideToMove);
        positionHistory.Add(new PositionRecord { key = key, sideToMove = sideToMove, inCheck = inCheck });

        int count;
        positionCounts.TryGetValue(key, out count);
        count++;
        positionCounts[key] = count;
        if (count < 4) return false;

        // 連続王手の千日手: 繰り返しの間、片方の手番の局面がすべて王手されていたら、王手をかけ続けた側の負け
        int first = positionHistory.FindIndex(r => r.key == key);
        foreach (PlayerType checkedSide in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            bool found = false;
            bool allInCheck = true;
            for (int i = first; i < positionHistory.Count; i++)
            {
                if (positionHistory[i].sideToMove != checkedSide) continue;
                found = true;
                if (!positionHistory[i].inCheck)
                {
                    allInCheck = false;
                    break;
                }
            }
            if (found && allInCheck)
            {
                Debug.Log("連続王手の千日手：王手をかけ続けた側の負け");
                EndGame(checkedSide);
                return true;
            }
        }

        Debug.Log("千日手：引き分け");
        isDraw = true;
        currentState = GameState.GameOver;
        return true;
    }

    // 盤面・持ち駒・手番を1つの文字列にする（ポイントは含めない）
    string BuildPositionKey(PlayerType sideToMove)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (GamePiece piece in gameBoard.GetBoardData())
        {
            if (piece == null)
            {
                sb.Append('.');
                continue;
            }
            sb.Append(piece.player == PlayerType.Player1 ? 'a' : 'b');
            sb.Append((int)piece.type);
            sb.Append(piece.isPromoted ? '+' : '-');
        }
        foreach (Player p in new[] { player1, player2 })
        {
            sb.Append('|');
            if (p == null) continue;
            foreach (PieceType type in HandOrder)
            {
                int num;
                p.Hand.TryGetValue(type, out num);
                sb.Append(num).Append(',');
            }
        }
        sb.Append(sideToMove == PlayerType.Player1 ? '1' : '2');
        gameBoard.Cells.AppendKey(sb); // マスの状態（破壊・炎上・落とし穴）も局面に含める
        return sb.ToString();
    }

    void EndGame(PlayerType winnerPlayer)
    {
        winner = winnerPlayer;
        isDraw = false;
        currentState = GameState.GameOver;
    }

    public bool IsPlayer1Turn()
    {
        return currentState == GameState.Player1Turn;
    }

    public void UseSkill(PlayerType player, int slotIndex)
    {
        // 成りの選択中・スキルの対象選択中は使えない
        if (isWaitingForPromoteChoice || isSelectingSkillTarget) return;

        // 自分のターンであるかチェック
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        if (player != activePlayer)
        {
            Debug.LogWarning("相手のターンにはスキルを使用できません。");
            return;
        }
        Player targetPlayer = (player == PlayerType.Player1) ? player1 : player2;
        if (targetPlayer == null || targetPlayer.SkillDeck == null)
        {
            return;
        }
        SkillDeck deck = targetPlayer.SkillDeck;
        if (slotIndex < 0 || slotIndex >= deck.skills.Count)
        {
            return;
        }

        SkillDefinition skill = deck.skills[slotIndex];
        if (skill == null)
        {
            return;
        }

        //コスト検証
        if (!targetPlayer.HasEnoughPoint(skill.cost))
        {
            Debug.LogWarning($"スキル「{skill.skillName}」を使用するためのポイントが不足しています。必要: {skill.cost}");
            return;
        }

        SkillContext context = new SkillContext(this, gameBoard, player);

        // 対象を選ぶスキルは、選び終わってから発動する（ポイントもその時に消費）
        if (skill.effect != null && skill.effect.TargetCount > 0)
        {
            BeginSkillTargetSelection(skill, context);
            return;
        }
        ExecuteSkill(skill, context);
    }

    void ExecuteSkill(SkillDefinition skill, SkillContext context)
    {
        PlayerType player = context.Player;
        Player targetPlayer = (player == PlayerType.Player1) ? player1 : player2;

        //コスト消費・効果実行
        targetPlayer.ConsumePoint(skill.cost);
        skill.Use(context);
        UpdateActivePlayerSkillUI();
        if (ResolveKingLoss()) return; // スキルで王将が全部いなくなった（今のスキルでは起きない）
        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, player);

        Debug.Log($"プレイヤー {(player == PlayerType.Player1 ? "1" : "2")} がスキル「{skill.skillName}」を使用しました。");

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【スキル発動！】 {skill.skillName}");
        }

        if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch)
        {
            // オンライン対戦時はサーバーへ通知
            NetworkManager.Instance.SendUseSkillRequest();
        }
        UpdateActivePlayerSkillUI();
    }

    void BeginSkillTargetSelection(SkillDefinition skill, SkillContext context)
    {
        // 駒の選択・持ち駒の選択を解除してから対象選択に入る
        selectedPiece = null;
        isSelectingHand = false;
        isDroppingHand = false;
        currentMovablePositions.Clear();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.HidePanel();
        }

        isSelectingSkillTarget = true;
        pendingSkill = skill;
        pendingSkillContext = context;
        lastPreviewCursor = new Vector2Int(-1, -1);
        UpdateSkillTargetPrompt();
    }

    void UpdateSkillTargetPrompt()
    {
        if (HandUIManager.Instance == null) return;
        string prompt = pendingSkill.effect.GetTargetPrompt(pendingSkillContext.Targets.Count);
        HandUIManager.Instance.SetStatusText($"【{pendingSkill.skillName}】 {prompt} (決定: F / 取消: Space)");
    }

    void HandleSkillTargetSelection()
    {
        SkillEffect effect = pendingSkill.effect;
        Vector2Int cursor = new Vector2Int(gameCursor.X, gameCursor.Y);

        // カーソルが動いたら、光らせるマスを更新する
        if (cursor != lastPreviewCursor)
        {
            lastPreviewCursor = cursor;
            gameBoard.AllDeactivePanel();
            foreach (Vector2Int pos in effect.GetPreviewCells(pendingSkillContext, cursor))
            {
                gameBoard.ActivePanel(GameHelper.CalcPanelNum(pos.x, pos.y));
            }
        }

        if (gameController.IsOkTrigger())
        {
            if (!effect.IsValidTarget(pendingSkillContext, cursor))
            {
                Debug.Log("そのマスは選べません");
                return;
            }

            pendingSkillContext.Targets.Add(cursor);
            lastPreviewCursor = new Vector2Int(-1, -1); // 選択状態が変わったので光らせ直す
            if (pendingSkillContext.Targets.Count < effect.TargetCount)
            {
                UpdateSkillTargetPrompt();
                return;
            }

            SkillDefinition skill = pendingSkill;
            SkillContext context = pendingSkillContext;
            EndSkillTargetSelection();
            ExecuteSkill(skill, context);
        }
        else if (gameController.IsCancelTrigger())
        {
            // 取り消した場合はポイントを消費しない
            EndSkillTargetSelection();
            if (HandUIManager.Instance != null)
            {
                HandUIManager.Instance.SetStatusText(IdleStatusText);
            }
        }
    }

    void EndSkillTargetSelection()
    {
        isSelectingSkillTarget = false;
        pendingSkill = null;
        pendingSkillContext = null;
        gameBoard.AllDeactivePanel();
    }

    public void OnServerUseSkill(PlayerType activePlayer)
    {
        Debug.Log($"プレイヤー {(activePlayer == PlayerType.Player1 ? "1" : "2")} がスキルを使用しました！");
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【スキル発動！】 プレイヤー{(activePlayer == PlayerType.Player1 ? "1" : "2")}がスキルを使用しました");
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

    public void UpdateActivePlayerSkillUI()
    {
        Player activePlayer = CurrentPlayer;
        if (activePlayer == null) return;

        SkillDeck activeDeck = activePlayer.SkillDeck;
        PointManager activePointManager = activePlayer.PointManager;

        if (activeDeck == null || skillSlotUIs == null) return;

        for (int i = 0; i < skillSlotUIs.Length; i++)
        {
            if (i < activeDeck.skills.Count)
            {
                SkillDefinition skill = activeDeck.skills[i];
                if (skill != null)
                {
                    skillSlotUIs[i].button.gameObject.SetActive(true);
                    skillSlotUIs[i].nameText.text = skill.skillName;
                    skillSlotUIs[i].costText.text = $"{skill.cost} Pt";
                    skillSlotUIs[i].button.interactable = activePlayer.HasEnoughPoint(skill.cost);
                }
                else
                {
                    skillSlotUIs[i].button.gameObject.SetActive(false);
                }
            }
            else
            {
                skillSlotUIs[i].button.gameObject.SetActive(false);
            }
        }
    }

    public void OnSkillSlotClicked(int slotIndex)
    {
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        UseSkill(activePlayer, slotIndex);
        UpdateActivePlayerSkillUI();
    }
}

enum GameState
{
    Preparation, // ゲームの準備中
    Player1Turn, // プレイヤー1のターン
    Player2Turn,  // プレイヤー2のターン
    GameOver    // ゲームオーバー
}
