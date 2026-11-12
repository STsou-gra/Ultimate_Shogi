using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using TMPro;

// ターン管理やゲームの準備を行う
public partial class GameSystem : MonoBehaviour
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

    // 盤のマスを選ぶモード（スキルの対象：爆発の中心・駒入替の駒・盾をかける駒など／必殺技の対象：天馬跳躍の跳ぶ先）。
    // スキルと必殺技で同じ操作（カーソル＋F で決定・Space で取消・R で範囲の向きの切り替え）を使う
    private class TargetSelection
    {
        public string Title;                                       // 案内文の【】に出す名前
        public int Count;                                          // 選ぶマスの数
        public List<Vector2Int> Chosen;                            // 選んだマス
        public System.Func<Vector2Int, bool> IsValid;              // そのマスを次に選べるか
        public System.Func<int, string> Prompt;                    // 何個目を選んでいるときの案内文
        public System.Func<Vector2Int, SkillPreview> Preview;      // カーソル位置で光らせるマス・塗る範囲
        public System.Action OnComplete;                           // 選び終わった
        public GamePiece Caster;                                   // 必殺技を使う駒（スキルなら null）

        // 範囲の表示（スキルのみ。SkillEffect の GetAreaCells など）
        public Color AreaColor = Color.white;                      // 範囲を塗る色（選べない範囲は赤）
        public string BlockedText = "その範囲は選べません";         // 選べない範囲の説明
        public int OrientCount = 1;                                // 向きの数（2つ以上なら R キーで切り替え）
        public int Orient;                                         // 今の向き
        public System.Action<int> SetOrient;                       // 向きを変えた
        public System.Func<int, string> OrientLabel;               // 向きの名前
    }
    private TargetSelection targetSelection;
    private Vector2Int lastPreviewCursor;
    private SkillAreaView skillAreaView; // 範囲の表示（盤を塗る）

    public bool IsSelectingSkillTarget => targetSelection != null;
    public bool IsSelectingSpecialTarget => targetSelection != null && targetSelection.Caster != null;

    // ------------------------------------------------------------
    //  駒錬成・火薬・狙撃手（Prototype の forge.js / effects.js の explode / play.js の snipe）
    // ------------------------------------------------------------
    [Header("Forge")]
    [SerializeField] private ForgeRecipeBook forgeRecipeBook; // 未設定なら Resources/ForgeRecipeBook を読む

    private static readonly List<ForgeRecipe> NoRecipes = new List<ForgeRecipe>();
    public IReadOnlyList<ForgeRecipe> ForgeRecipes
    {
        get
        {
            if (forgeRecipeBook == null) forgeRecipeBook = ForgeRecipeBook.LoadDefault();
            return forgeRecipeBook != null ? forgeRecipeBook.recipes : NoRecipes;
        }
    }

    // 錬成した（演出用）
    public event System.Action<PlayerType, ForgeRecipe> Forged;
    // 爆発が起きた（範囲の駒が消失する前に呼ばれる。演出用）
    public event System.Action<ExplosionInfo> Exploded;
    // 狙撃した（撃たれた駒が取られる・爆発する前に呼ばれる。演出用）
    public event System.Action<SnipeShot> Sniped;

    public const string ReasonExplosion = "爆発";

    // 演出の長さ（秒）。盤面はすぐ変わるので、手番の交代をこの分だけ遅らせて演出を見せる
    public const float SnipeImpactDelay = 0.85f;  // 照準が合ってビームが着弾するまで
    public const float SnipeAfterDelay = 0.6f;    // 着弾してから手番が替わるまで（「SNIPE!」を見せる）
    public const float ExplosionFxDuration = 0.9f; // 爆発が始まってから手番が替わるまで

    // 狙撃の対象選択
    private bool isSelectingSnipeTarget = false;
    private GamePiece snipingPiece;
    private readonly List<Vector2Int> snipeTargets = new List<Vector2Int>();
    public bool IsSelectingSnipeTarget => isSelectingSnipeTarget;

    // ------------------------------------------------------------
    //  必殺技（Prototype の src/defs/specials・src/core/play.js の special）
    // ------------------------------------------------------------
    [Header("Special Moves")]
    [SerializeField] private SpecialMoveBook specialMoveBook; // 未設定なら Resources/SpecialMoveBook を読む
    private SpecialMoveFx specialFx;

    public SpecialMoveBook SpecialMoves
    {
        get
        {
            if (specialMoveBook == null) specialMoveBook = SpecialMoveBook.LoadDefault();
            return specialMoveBook;
        }
    }

    // 移動回数がちょうど必要数に達した（「必殺技 解放！」の演出用）
    public event System.Action<GamePiece> SpecialReady;

    // 発動中の必殺技・魔法の時間軸（効果を処理している間だけ。演出が倒された駒の飛ばし方・盾が割れる時刻を読む）
    public SpecialContext ActiveSpecial { get; private set; }

    // ------------------------------------------------------------
    //  魔法スキル（Prototype の src/defs/magic・src/core/play.js の castMagic）
    // ------------------------------------------------------------
    private MagicFx magicFx;

    // 盾状態になった・盾が取りを防いで割れた・ファランクス状態になった（演出用）
    public event System.Action<GamePiece> ShieldGiven;
    public event System.Action<ShieldBreak> ShieldBroken;
    public event System.Action<GamePiece> PhalanxGiven;

    // ------------------------------------------------------------
    //  超常魔法（Prototype の turn.js の checkTranscendentUnlock・skills.js の magicStatus）
    //  解放条件（TranscendentRules）を満たすと解放され（一度解放されたらそのまま）、1 対局に 1 回だけ使える。
    //  解放したら「超常魔法 解放！」のカットインを出す（その間は操作できない）
    // ------------------------------------------------------------

    // 超常魔法が解放された（カットインのあと。スキルボタンの「解放！」の演出用）
    public event System.Action<PlayerType> TranscendentUnlocked;
    // 超常魔法「世界の終わり」が始まった（盤の下の「進行中」表示・背景の赤み）
    public event System.Action<PlayerType> WorldEndStarted;
    // 超常魔法「アンデッド・パラダイス」で駒がアンデッドとして復活した（データはもう盤にある。見た目は MagicFx.UndeadRise）
    public event System.Action<GamePiece> UndeadRisen;

    // アンデッドが倒されたときの消失の理由（取られても持ち駒にならず、エナジーも動かない）
    public const string ReasonUndeadRest = "成仏";

    private readonly Queue<PlayerType> unlockQueue = new Queue<PlayerType>(); // 解放の演出待ち
    private bool isShowingUnlock = false;

    // ------------------------------------------------------------
    //  手番交代の前の自動行動（Prototype の turn.js の afterAction：ファランクス → アンデッド → …）
    //  手番を終える行動（移動・打つ・手番を使う魔法・必殺技・狙撃）のあと、手番を相手に渡す前に、
    //  手番側の自動行動をこの順に 1 つずつ行う（演出を見せ終わってから次へ）。2回行動で手番が続くときは行わない
    // ------------------------------------------------------------
    private readonly List<TurnEndAction> turnEndActions = new List<TurnEndAction> { new PhalanxAdvance(), new UndeadMarch() };

    // 1 ターンの終わり（後手の手番の終わり）に起きること（Prototype の turn.js の roundEnd のうち、演出を見せるもの）。
    // 手番側の自動行動（ファランクス・アンデッド …）のあと、OnRoundEnd（手番交代）の直前に、同じ仕組みで 1 つずつ行う。
    // 世界の終わりの崩壊は「赤く点滅 → 崩落」を見せてから手番を渡したいので、OnRoundEnd の中ではなくここで行う
    private readonly List<TurnEndAction> roundEndActions = new List<TurnEndAction> { new WorldEndCollapse() };

    // 手番交代の前に行う自動行動を、行う順に並べたもの（手番側の自動行動 → 1 ターンの終わり）
    List<TurnEndAction> TurnEndActionsInOrder()
    {
        List<TurnEndAction> all = new List<TurnEndAction>(turnEndActions);
        all.AddRange(roundEndActions);
        return all;
    }
    private bool isRunningTurnEnd = false;
    public bool IsRunningTurnEnd => isRunningTurnEnd;

    // 自動行動を足す（足した順に、ファランクス・アンデッドのあとに動く）
    public void AddTurnEndAction(TurnEndAction action)
    {
        if (action != null && !turnEndActions.Contains(action)) turnEndActions.Add(action);
    }

    // 盾が割れるまでの秒数（移動・狙撃で取ろうとしたとき、攻撃が届くまで。CapturePiece を呼ぶ側が入れて、呼んだら 0 に戻す）
    private float captureFxDelay = 0f;
    // 今の取りの攻撃が届くまでの秒数（取り・消失の通知の中で演出が読む。必殺技・自動行動の中では ActiveSpecial.HitTime を使う）
    public float CaptureFxDelay => captureFxDelay;
    // 狙撃の取りの通知中か（狙撃の演出は NewPieceFx が出すので、普段の取りの演出 BasicMoveFx は出さない）
    private bool isSnipeCapture = false;
    public bool IsSnipeCapture => isSnipeCapture;
    private BasicMoveFx basicMoveFx;
    // マスの効果（炎上・落とし穴など）で消える駒が、そのマスに着くまでの秒数（EnterCell が消失の通知の間だけ入れる。演出が読む）
    private float enterCellFxDelay = 0f;
    public float EnterCellFxDelay => enterCellFxDelay;
    public const float ShieldBreakFxDuration = 0.6f; // 盾が割れてから手番が替わるまで（「GUARD!」を見せる）

    // 行動の演出を見せている間（手番の交代待ち）は操作できない
    private bool isResolvingAction = false;
    private float pendingActionFxTime = 0f; // この行動の演出があと何秒かかるか（爆発・狙撃で増える）
    public bool IsResolvingAction => IsBusy;

    // 演出の途中（行動の演出・超常魔法の解放の演出）で、操作を受け付けないか
    bool IsBusy => isResolvingAction || isShowingUnlock;

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
        // 行動の合法手を確かめるデバッグキー（L。エディタと Development Build のみ）
        if (FindAnyObjectByType<ActionDebugTool>() == null)
        {
            gameObject.AddComponent<ActionDebugTool>();
        }

        // 駒錬成のレシピ、錬成・狙撃のボタン、爆発・狙撃の演出（シーンに置かれていなければ自動で追加する）
        if (ForgeRecipes.Count == 0) Debug.LogWarning("駒錬成のレシピ（Resources/ForgeRecipeBook）が見つかりません");
        if (FindAnyObjectByType<ForgePanelView>() == null)
        {
            gameObject.AddComponent<ForgePanelView>();
        }
        if (FindAnyObjectByType<NewPieceFx>() == null)
        {
            gameObject.AddComponent<NewPieceFx>();
        }
        // 取り・打つ・成り・王手の演出
        basicMoveFx = FindAnyObjectByType<BasicMoveFx>();
        if (basicMoveFx == null) basicMoveFx = gameObject.AddComponent<BasicMoveFx>();

        // 必殺技の演出（シーンに置かれていなければ自動で追加する）。カットインは CutInEffect.Instance が自動で作る
        specialFx = FindAnyObjectByType<SpecialMoveFx>();
        if (specialFx == null) specialFx = gameObject.AddComponent<SpecialMoveFx>();
        if (SpecialMoves == null) Debug.LogWarning("必殺技の一覧が読めません");

        // 魔法スキルの演出・範囲の表示（シーンに置かれていなければ自動で追加する）
        magicFx = FindAnyObjectByType<MagicFx>();
        if (magicFx == null) magicFx = gameObject.AddComponent<MagicFx>();
        skillAreaView = FindAnyObjectByType<SkillAreaView>();
        if (skillAreaView == null) skillAreaView = gameObject.AddComponent<SkillAreaView>();

        // 超常魔法の解放条件の表示と、世界の終わりの「進行中」表示（シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<TranscendentStatusView>() == null)
        {
            gameObject.AddComponent<TranscendentStatusView>();
        }
        if (FindAnyObjectByType<WorldEndView>() == null)
        {
            gameObject.AddComponent<WorldEndView>();
        }

        // 時間停止（停止世界）の見た目：色の反転・予定の表示・残り回数と「解除」ボタン（シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<TimeStopView>() == null)
        {
            gameObject.AddComponent<TimeStopView>();
        }

        // ルール説明のオーバーレイ（「ルール」ボタン・Tab キー。シーンに置かれていなければ自動で追加する）
        if (FindAnyObjectByType<RulesOverlayView>() == null)
        {
            gameObject.AddComponent<RulesOverlayView>();
        }

        // 対局の状態（ターン数・エナジー）
        // 乱数のシードは対局ごとに決める（後で棋譜に残し、オンラインでは両者で同じ値を使う）
        uint seed = GameRng.NewSeed();
        match = new MatchState(MatchConfig.FirstPlayer, seed);
        // エナジーの増減の「+N エナジー」（BasicMoveFx は Start で作られる。対局の準備の前に作られていなければ探す）
        match.Energy.Changed += (player, value, delta) =>
        {
            if (basicMoveFx == null) basicMoveFx = FindAnyObjectByType<BasicMoveFx>();
            if (basicMoveFx != null) basicMoveFx.Energy(player, delta);
        };
        Debug.Log($"対局の乱数のシード: {seed}");
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
        // 盤面のシミュレーションの照合（エディタと Development Build のみ。行動が終わって操作できるようになったら）
        UpdateSimulationCheck();
        // 前の行動の処理が終わっていれば、その時点の盤面のハッシュを棋譜に残す（入力を受け付ける前・結果画面へ移る前に）
        UpdateActionHash();

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

        // 爆発・狙撃の演出が終わって手番が替わるまで・超常魔法の解放の演出の間は操作しない
        if (IsBusy) return;

        // ルール説明を開いている間（と閉じた直後）は操作しない
        if (RulesOverlayView.IsBlockingInput) return;

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

        if (IsSelectingSkillTarget)
        {
            HandleTargetSelection();
            return;
        }

        if (isSelectingSnipeTarget)
        {
            HandleSnipeTargetSelection();
            return;
        }

        // 錬成のキー操作（Z・X …：レシピの順。ボタンでも錬成できる）
        HandleForgeKeys(keyboard);

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
            // 持ち駒打つ位置選択モード：決定したら「打つ」行動を Dispatch（オンラインならサーバーへ送る）
            if (gameController.IsOkTrigger())
            {
                MatchAction drop = MatchAction.Drop(selectedHandType, new Vector2Int(gameCursor.X, gameCursor.Y));
                if (!Dispatch(drop))
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
            if (keyboard.qKey.wasPressedThisFrame && CanReleaseTimeStop)
            {
                // 時間停止の「解除」（Q キー・「解除」ボタン）
                ReleaseTimeStop();
            }
            else if (keyboard.hKey.wasPressedThisFrame)
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
            else if (keyboard.gKey.wasPressedThisFrame && selectedPiece != null && PieceRules.IsSniper(selectedPiece.type))
            {
                BeginSnipe();
            }
            else if (keyboard.eKey.wasPressedThisFrame && selectedPiece != null)
            {
                BeginSpecial();
            }
            else if (gameController.IsOkTrigger())
            {
                HandlePieceSelection();
            }
            else if (gameController.IsCancelTrigger())
            {
                selectedPiece = null;
                if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
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

        if (selectedPiece == null)
        {
            // --- 駒を選択するフェーズ ---
            GamePiece piece = gameBoard.GetPieceAt(x, y);

            // 自分のアンデッドは操作できない（持ち主の手番の終わりに自動で動く）
            if (piece != null && IsMyPiece(piece) && piece.isUndead)
            {
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText($"【{HandUIManager.GetJapaneseName(piece.type)}・アンデッド】 操作できない。手番の終わりに自動で1手動き、相手の王将へ向かう。" +
                        "必殺技は使えず、自分の魔法の対象にも選べない。倒されると成仏する（持ち駒にならない）");
                }
                return;
            }

            // 時間停止中、消失・爆発の予定ができた自分の駒は、もう動かせない（必殺技・狙撃も使えない）
            if (piece != null && IsMyPiece(piece) && IsFrozenInTimeStop(piece))
            {
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText($"【{HandUIManager.GetJapaneseName(piece.type)}】 消失・爆発の予定があるので、もう動かせない（時間が動き出すと消える）");
                }
                return;
            }

            // 自分の駒なら選択
            if (piece != null && IsMyPiece(piece))
            {
                selectedPiece = piece;
                gameBoard.ActivePanel(GameHelper.CalcPanelNum(x, y)); // 選択した足元を光らせる
                ShowMovablePanels(selectedPiece); // 移動可能なマスを光らせる
                if (PieceRules.IsSniper(piece.type) && HandUIManager.Instance != null)
                {
                    // 狙撃手は動けない。「狙撃」ボタン（または G・もう一度 F）で狙撃の対象選択へ
                    HandUIManager.Instance.SetStatusText("【狙撃手】 狙撃: G / もう一度 F /「狙撃」ボタン (取消: Space)");
                }
                else if (GetSpecialMove(piece) != null && HandUIManager.Instance != null)
                {
                    // 必殺技のある駒：移動回数と必殺技の状態（オーラが出ていれば E・「必殺技」ボタンで発動）
                    HandUIManager.Instance.SetStatusText(SpecialStatusText(piece));
                }
            }
        }
        else if (PieceRules.IsSniper(selectedPiece.type) && x == selectedPiece.X && y == selectedPiece.Y)
        {
            // 選んだ狙撃手の上でもう一度決定 → 狙撃
            BeginSnipe();
        }
        else
        {
            // --- 駒を移動させるフェーズ ---
            if (CanPieceMoveTo(selectedPiece, x, y))
            {
                // 成り判定（火薬のマスに入って吹き飛ぶ駒には聞かない）
                if (CanPromote(selectedPiece, selectedPiece.X, x) && !WillBeDestroyedEntering(selectedPiece, x, y))
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

    // 移動先（と成るか）が決まった：「移動」行動を Dispatch（オンラインならサーバーへ送る）
    void ConfirmMove(int fromX, int fromY, int toX, int toY, bool promote)
    {
        MatchAction move = MatchAction.Move(new Vector2Int(fromX, fromY), new Vector2Int(toX, toY), promote);
        if (!Dispatch(move))
        {
            Debug.Log($"そこには移動できません：{move}");
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

    // fromX → toX の移動で成れるか（敵陣に入る・敵陣から出る。成れる駒で、まだ成っていない）。
    // 必殺技・ファランクスの前進など、自動で成るところからも使う
    public bool CanPromote(GamePiece piece, int fromX, int toX)
    {
        if (piece.isPromoted || !PieceRules.CanPromote(piece.type))
        {
            return false;
        }
        // 時間停止中は成りも予定になる（1 つの駒につき 1 回。予定ができたら、時間が動き出すまで成る前の動きのまま）
        if (timeStop != null && timeStop.HasPendingPromote(piece))
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
        EndActionTurn();
    }

    // 駒の移動（相手の駒がいれば取り）。盤面のデータを変え、見た目の移動を始める。対局が終わったら true
    bool ApplyMove(GamePiece piece, int targetX, int targetY, bool promote)
    {
        GamePiece targetPiece = gameBoard.GetPieceAt(targetX, targetY);
        RecordTimeStopMove(piece, targetX, targetY); // 時間停止中は 1 マスごとの移動を記録する（盾で押し戻すときに使う）
        if (targetPiece != null && targetPiece != piece && PieceRules.IsExplosive(targetPiece.type))
        {
            return EnterGunpowder(piece, targetX, targetY, promote);
        }
        if (targetPiece != null && targetPiece.player != piece.player)
        {
            float reach = MoveArrivalDelay(piece) * 0.55f; // 盾の駒にはバリアの手前でぶつかる
            captureFxDelay = reach;
            bool captured = CapturePiece(targetPiece, piece.player, piece);
            captureFxDelay = 0f;
            if (!captured)
            {
                // 取りが防がれた（盾など）：盾にぶつかって元のマスへ跳ね返る。移動回数は数えず、手番は消費する
                if (magicFx != null) magicFx.Bounce(piece, targetX, targetY, reach);
                return ResolveKingLoss();
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
            PromotePiece(piece); // 時間停止中は成りの予定にする
        }
        CountMove(piece);

        return ResolveKingLoss();
    }

    // 火薬のマスに入る（Prototype の play.js の stepPiece）：重なった瞬間に爆発する。
    // 取りではないので、エナジー・持ち駒化はなし。入った駒も吹き飛ぶ（王将だけは爆発に耐えて、そのマスに残る）。対局が終わったら true
    bool EnterGunpowder(GamePiece piece, int x, int y, bool promote)
    {
        Explode(x, y, MoveArrivalDelay(piece), piece);

        // 爆発に耐えた（王将）：火薬が消えたマスへ進む
        if (IsOnBoard(piece) && !gameBoard.HasPieceAt(x, y))
        {
            gameBoard.UpdateBoardData(piece.X, piece.Y, x, y);
            piece.X = x;
            piece.Y = y;
            AnimatePieceMove(piece, GameHelper.CalcPanelLocation(x, y));
            if (EnterCell(piece, x, y))
            {
                if (promote) PromotePiece(piece);
                CountMove(piece);
            }
        }
        return ResolveKingLoss();
    }

    // 駒 piece が (x, y) に入ると、火薬の爆発で吹き飛ぶか（成りの確認を出さないために使う）
    bool WillBeDestroyedEntering(GamePiece piece, int x, int y)
    {
        GamePiece target = gameBoard.GetPieceAt(x, y);
        return target != null && target != piece && PieceRules.IsExplosive(target.type) && PieceRules.IsDestroyedByExplosion(piece.type);
    }

    // 駒が移動先に着くまでの秒数（爆発の見た目を合わせる。PieceMoveAnimator の既定の長さ）
    static float MoveArrivalDelay(GamePiece piece)
    {
        return PieceMoveAnimator.StyleFor(piece) == PieceMoveStyle.Jump ? 0.35f : 0.28f;
    }

    // 爆発（Prototype の effects.js の explode）：中心 (cx, cy) の 3×3（MatchConfig.ExplosionRadius）の駒をすべて消失させる。
    // 消失なので、エナジー・持ち駒化はなし。王将は爆発では倒せない（MatchConfig.ExplosionSparesKing）。
    // entering は火薬のマスに入った駒（範囲の外から来ても一緒に吹き飛ぶ）。fxDelay は見た目を始めるまでの秒数。
    // 火薬・爆発スキル共通。王将が消えることはないが、呼び出し側で ResolveKingLoss を呼ぶこと
    // held は盤のデータにはいないが、その場に残っている駒（時間停止の「取られる予定」の駒など。範囲内なら一緒に消失する）。
    // 時間停止中は爆発の予定にする（火薬はその場に残り、火薬に入った駒はもう動かせない）
    public void Explode(int cx, int cy, float fxDelay = 0f, GamePiece entering = null, IEnumerable<GamePiece> held = null)
    {
        if (timeStop != null)
        {
            DeferExplode(cx, cy, entering);
            return;
        }
        Debug.Log($"({cx}, {cy}) で爆発！");
        Exploded?.Invoke(new ExplosionInfo(cx, cy, fxDelay, entering));
        pendingActionFxTime = Mathf.Max(pendingActionFxTime, fxDelay + ExplosionFxDuration);

        List<GamePiece> victims = new List<GamePiece>();
        int r = MatchConfig.ExplosionRadius;
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                GamePiece piece = gameBoard.GetPieceAt(x, y);
                if (piece != null) victims.Add(piece);
            }
        }
        if (entering != null && !victims.Contains(entering)) victims.Add(entering);
        if (held != null)
        {
            foreach (GamePiece q in held)
            {
                if (q == null || victims.Contains(q) || !IsOnBoard(q)) continue;
                if (Mathf.Abs(q.X - cx) <= r && Mathf.Abs(q.Y - cy) <= r) victims.Add(q);
            }
        }

        List<Vector2Int> chain = new List<Vector2Int>();
        foreach (GamePiece piece in victims)
        {
            if (!IsOnBoard(piece)) continue;
            if (!PieceRules.IsDestroyedByExplosion(piece.type))
            {
                Debug.Log($"({piece.X}, {piece.Y}) の王将は爆発に耐えた。");
                continue;
            }
            bool isOtherBomb = PieceRules.IsExplosive(piece.type) && (piece.X != cx || piece.Y != cy);
            int px = piece.X;
            int py = piece.Y;
            if (VanishPiece(piece, ReasonExplosion) && MatchConfig.ChainExplosion && isOtherBomb)
            {
                chain.Add(new Vector2Int(px, py));
            }
        }

        // 誘爆（MatchConfig.ChainExplosion のときだけ）
        foreach (Vector2Int c in chain)
        {
            Explode(c.x, c.y, fxDelay + 0.3f);
        }
    }

    // ------------------------------------------------------------
    //  狙撃（Prototype の play.js の snipe）
    //  狙撃手を選んで「狙撃」→ 王将以外の相手の駒（火薬を含む）を選ぶ。射程は無制限で、間の駒も無視する。
    //  撃った駒は「取り」（エナジー配分・持ち駒化あり）、火薬ならその場で爆発。手番を消費する
    // ------------------------------------------------------------

    // 狙撃手 sniper が撃てるマス
    public List<Vector2Int> GetSnipeTargets(GamePiece sniper)
    {
        List<Vector2Int> targets = new List<Vector2Int>();
        if (sniper == null || !PieceRules.IsSniper(sniper.type)) return targets;
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 9; y++)
            {
                GamePiece q = gameBoard.GetPieceAt(x, y);
                if (q != null && q.player != sniper.player && q.type != PieceType.King) targets.Add(new Vector2Int(x, y));
            }
        }
        return targets;
    }

    // 選んでいる狙撃手で狙撃できるか（「狙撃」ボタンを出すか）
    public bool CanBeginSnipe
    {
        get
        {
            return CanActNow && !isSelectingHand && !isDroppingHand && !isSelectingSnipeTarget
                && selectedPiece != null && IsMyPiece(selectedPiece) && PieceRules.IsSniper(selectedPiece.type)
                && !IsFrozenInTimeStop(selectedPiece) && !IsOnlineMatch;
        }
    }

    // 「狙撃」ボタン・G キー：狙撃の対象選択を始める
    public void BeginSnipe()
    {
        if (!CanBeginSnipe) return;
        List<Vector2Int> targets = GetSnipeTargets(selectedPiece);
        if (targets.Count == 0)
        {
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText("【狙撃】 撃てる相手の駒がありません (取消: Space)");
            return;
        }

        snipingPiece = selectedPiece;
        snipeTargets.Clear();
        snipeTargets.AddRange(targets);
        isSelectingSnipeTarget = true;

        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(true);
        currentMovablePositions.Clear();
        currentMovablePositions.AddRange(snipeTargets);
        gameBoard.ActivePanel(GameHelper.CalcPanelNum(snipingPiece.X, snipingPiece.Y));
        snipingPiece.SetHighlight(true);
        highlightedPieces.Add(snipingPiece);
        foreach (Vector2Int t in snipeTargets)
        {
            gameBoard.ActivePanel(GameHelper.CalcPanelNum(t.x, t.y));
            GamePiece q = gameBoard.GetPieceAt(t.x, t.y);
            if (q != null)
            {
                q.SetHighlight(true);
                highlightedPieces.Add(q);
            }
        }
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText("【狙撃】 撃つ駒を選択（王将以外。火薬は撃つと爆発） (決定: F / 取消: Space)");
        }
    }

    // 「狙撃をやめる」ボタン・Space：駒を選ぶ前の状態に戻す
    public void CancelSnipe()
    {
        if (!isSelectingSnipeTarget) return;
        EndSnipeTargetSelection();
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
    }

    void EndSnipeTargetSelection()
    {
        isSelectingSnipeTarget = false;
        snipingPiece = null;
        snipeTargets.Clear();
        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        currentMovablePositions.Clear();
    }

    void HandleSnipeTargetSelection()
    {
        if (gameController.IsOkTrigger())
        {
            Vector2Int cursor = new Vector2Int(gameCursor.X, gameCursor.Y);
            if (!snipeTargets.Contains(cursor))
            {
                Debug.Log("そこは狙撃できません");
                return;
            }
            // 対象が決まった：「狙撃」行動を Dispatch
            MatchAction snipe = MatchAction.Snipe(new Vector2Int(snipingPiece.X, snipingPiece.Y), cursor);
            EndSnipeTargetSelection();
            Dispatch(snipe);
        }
        else if (gameController.IsCancelTrigger())
        {
            CancelSnipe();
        }
    }

    // 狙撃の実行（Dispatch の ExecutePlan から）
    void ExecuteSnipe(GamePiece sniper, int tx, int ty)
    {
        GamePiece target = gameBoard.GetPieceAt(tx, ty);
        if (sniper == null || target == null || !IsOnBoard(sniper)) return;

        bool explosive = PieceRules.IsExplosive(target.type);
        Debug.Log($"狙撃！ ({sniper.X}, {sniper.Y}) → ({tx}, {ty}) の {HandUIManager.GetJapaneseName(target.type)}");
        pendingActionFxTime = Mathf.Max(pendingActionFxTime, SnipeImpactDelay + SnipeAfterDelay);
        bool guarded = !explosive && target.hasShield && target.player != sniper.player && timeStop == null; // 時間停止中は盾の判定も時間が動き出してから
        Sniped?.Invoke(new SnipeShot(sniper.player, sniper, new Vector2Int(sniper.X, sniper.Y), new Vector2Int(tx, ty), target.type, explosive, SnipeImpactDelay, guarded));

        string targetName = HandUIManager.GetJapaneseName(target.type);
        bool captured = true;
        if (explosive)
        {
            Explode(tx, ty, SnipeImpactDelay);
        }
        else
        {
            captureFxDelay = SnipeImpactDelay;
            isSnipeCapture = true;
            captured = CapturePiece(target, sniper.player, sniper); // 盾なら防がれる
            isSnipeCapture = false;
            captureFxDelay = 0f;
        }
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText(timeStop != null ? (explosive ? "【狙撃】 火薬を撃ち抜いた！（時間が動き出すと爆発する）" : $"【狙撃】 {targetName}を撃ち抜いた！（時間が動き出すと取れる）")
                : explosive ? "【狙撃】 火薬を撃ち抜いた！ 大爆発！"
                : captured ? $"【狙撃】 {targetName}を撃ち抜いた！"
                : $"【狙撃】 {targetName}の盾が弾を防いだ！（盾は割れた）");
        }
        if (ResolveKingLoss()) return;
        EndActionTurn();
    }

    // ------------------------------------------------------------
    //  必殺技（Prototype の play.js の special・skills.js の canUseSpecial / specialTargets）
    //  駒ごとに移動回数を数え、技ごとの回数に達するとオーラが出て発動できる（成った駒も同じ技）。
    //  オーラの出ている自分の駒を選ぶと「必殺技」ボタン（E キー）→ 対象を選ぶ技なら対象選択 → カットイン → 効果。
    //  手番を消費する（A-5）。発動後は移動回数が 0 に戻る。取った駒は持ち駒になる（A-7）。王将も取れる（R-4）。
    //  オンライン対戦中はまだ使えない（未同期）。詰みの判定にも含めない
    // ------------------------------------------------------------

    // 駒の必殺技（なければ null。アンデッドは必殺技を使えない）
    public SpecialMove GetSpecialMove(GamePiece piece)
    {
        if (piece == null || piece.isUndead || SpecialMoves == null) return null;
        return SpecialMoves.For(piece.type);
    }

    // 通常の移動を 1 回数える（Prototype の play.js の move の moveCount++ と noteSpecialReady）
    void CountMove(GamePiece piece)
    {
        if (!IsOnBoard(piece)) return;
        piece.moveCount++;
        MarkTimeStopMoveCounted(piece); // 盾で押し戻されたら数え直す
        bool wasReady = piece.isSpecialReady;
        RefreshSpecialReady(piece);
        if (piece.isSpecialReady && !wasReady)
        {
            Debug.Log($"{piece.player} の{HandUIManager.GetJapaneseName(piece.type)}：必殺技「{GetSpecialMove(piece).moveName}」が使えるようになった！");
            SpecialReady?.Invoke(piece);
        }
    }

    // 移動回数から isSpecialReady（オーラ）を設定し直す
    public void RefreshSpecialReady(GamePiece piece)
    {
        if (piece == null) return;
        SpecialMove move = GetSpecialMove(piece);
        piece.isSpecialReady = move != null && piece.moveCount >= move.requiredMoves;
    }

    // 駒を選んだときの案内（Prototype の PlayerPanel.js の駒の情報）
    string SpecialStatusText(GamePiece piece)
    {
        SpecialMove move = GetSpecialMove(piece);
        if (move == null) return IdleStatusText;
        string name = HandUIManager.GetJapaneseName(piece.type);
        if (!piece.isSpecialReady)
        {
            int rest = Mathf.Max(0, move.requiredMoves - piece.moveCount);
            return $"【{name}】 移動 {piece.moveCount} 回　必殺技「{move.moveName}」はあと {rest} 回移動で使える (取消: Space)";
        }
        if (IsOnlineMatch) return $"【{name}】 必殺技「{move.moveName}」★発動可能★（オンライン対戦ではまだ使えません）";
        string usage = move.CanUse(new SpecialContext(this, gameBoard, piece, specialFx))
            ? "発動: E /「必殺技」ボタン"
            : "今は効果の対象がありません";
        return $"【{name}】 必殺技「{move.moveName}」★発動可能★ {usage} (取消: Space)\n{move.description}";
    }

    // 選んでいる駒で必殺技のボタンを出すか（自分の手番に、オーラの出ている自分の駒を選んでいる）
    public bool CanBeginSpecial
    {
        get
        {
            return CanActNow && !isSelectingHand && !isDroppingHand && !isSelectingSnipeTarget
                && selectedPiece != null && IsMyPiece(selectedPiece) && selectedPiece.isSpecialReady
                && !IsFrozenInTimeStop(selectedPiece) && GetSpecialMove(selectedPiece) != null && !IsOnlineMatch;
        }
    }

    // 選んでいる駒の必殺技（ボタンの表示用。対象選択中はその技）
    public SpecialMove SelectedSpecial
    {
        get
        {
            if (IsSelectingSpecialTarget) return GetSpecialMove(targetSelection.Caster);
            return selectedPiece != null ? GetSpecialMove(selectedPiece) : null;
        }
    }

    // 選んでいる駒の必殺技を今の盤面で使えるか（効果の対象があるか）
    public bool CanUseSelectedSpecial
    {
        get
        {
            if (!CanBeginSpecial) return false;
            return GetSpecialMove(selectedPiece).CanUse(new SpecialContext(this, gameBoard, selectedPiece, specialFx));
        }
    }

    // 「必殺技」ボタン・E キー：対象を選ぶ技なら対象選択へ、選ばない技はすぐ発動する
    public void BeginSpecial()
    {
        if (!CanBeginSpecial) return;
        GamePiece caster = selectedPiece;
        SpecialMove move = GetSpecialMove(caster);
        SpecialContext probe = new SpecialContext(this, gameBoard, caster, specialFx);
        if (!move.CanUse(probe))
        {
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【{move.moveName}】 今は効果の対象がありません (取消: Space)");
            return;
        }

        Vector2Int from = new Vector2Int(caster.X, caster.Y);
        List<Vector2Int> targets = move.GetTargets(probe);
        if (targets == null)
        {
            Dispatch(MatchAction.Special(from)); // 対象を選ばない技はすぐ発動
            return;
        }

        // 対象を選ぶ技（天馬跳躍）：スキルと同じ対象選択モードで 1 マス選ぶ → 「必殺技」行動を Dispatch
        List<Vector2Int> chosen = new List<Vector2Int>();
        BeginTargetSelection(new TargetSelection
        {
            Title = move.moveName,
            Count = 1,
            Chosen = chosen,
            IsValid = pos => targets.Contains(pos),
            Prompt = index => move.GetTargetPrompt(),
            Preview = cursor => SkillPreview.FromPanels(targets),
            OnComplete = () => Dispatch(MatchAction.Special(from, chosen[0])),
            Caster = caster,
        });
    }

    // 必殺技の実行（Dispatch の ExecutePlan から）
    void StartSpecial(GamePiece caster, SpecialMove move, Vector2Int? target)
    {
        ResetSelectionState();
        ExecuteSpecial(caster, move, target);
    }

    // 発動（Prototype の play.js の special）：カットイン → 効果 → 移動回数を 0 に → 成り → 演出を待って手番交代
    async void ExecuteSpecial(GamePiece caster, SpecialMove move, Vector2Int? target)
    {
        if (!IsOnBoard(caster)) return;
        isResolvingAction = true;
        PlayerType owner = caster.player;
        string pieceName = HandUIManager.GetJapaneseName(caster.type);
        Debug.Log($"{owner} の{pieceName}が必殺技「{move.moveName}」を発動！");
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【必殺技】 {pieceName}の「{move.moveName}」！");

        try
        {
            if (specialFx != null) specialFx.PlayCast(caster, move);
            await Awaitable.WaitForSecondsAsync(0.25f, destroyCancellationToken);
            string side = owner == PlayerType.Player1 ? "先手" : "後手";
            await CutInEffect.Instance.PlayAsync(move.moveName, $"{side}・{pieceName}の必殺技", move.color, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }

        if (!IsInTurn || !IsOnBoard(caster))
        {
            isResolvingAction = false;
            return;
        }
        if (specialFx != null) specialFx.PopName(caster, move);

        // 効果（盤面はすぐ変わり、演出は SpecialMoveFx が時刻どおりに再生する）
        SpecialContext context = new SpecialContext(this, gameBoard, caster, specialFx) { Target = target };
        int fromX = caster.X;
        pendingActionFxTime = 0f;
        ActiveSpecial = context;
        try
        {
            move.Apply(context);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            ActiveSpecial = null;
        }

        // 発動後は移動回数（チャージ）を 0 に戻す。敵陣に入る・出ると成る（Prototype と同じく自動で成る）
        if (IsOnBoard(caster))
        {
            caster.moveCount = 0;
            RefreshSpecialReady(caster);
            if (CanPromote(caster, fromX, caster.X))
            {
                if (timeStop != null)
                {
                    DeferPromote(caster); // 時間停止中は成りの予定にする
                }
                else
                {
                    caster.isPromoted = true;
                    context.Schedule(caster.UpdateVisual);
                }
            }
        }

        // 演出を見せ終わってから、勝敗の判定と手番の交代
        float wait = Mathf.Max(pendingActionFxTime, context.Time + 0.5f);
        pendingActionFxTime = 0f;
        try
        {
            await Awaitable.WaitForSecondsAsync(wait, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return;
        }
        isResolvingAction = false;
        UpdateActivePlayerSkillUI();
        if (ResolveKingLoss()) return;
        if (IsInTurn) NextState();
    }

    // ------------------------------------------------------------
    //  駒錬成（Prototype の effects.js の forge）
    //  エナジーを払って駒を作り、持ち駒に加える。MatchConfig.ForgeEndsTurn が false なら手番は続く
    // ------------------------------------------------------------

    // 今、手番のプレイヤーが操作できるか（演出中・成りの確認中・オンラインの相手の手番は不可）
    bool CanActNow
    {
        get
        {
            if (!IsInTurn || IsBusy || isWaitingForPromoteChoice || IsSelectingSkillTarget) return false;
            if (IsOnlineMatch && CurrentPlayer.PlayerType != NetworkManager.Instance.MyPlayerType) return false;
            return true;
        }
    }

    // オンライン対戦中か（錬成・狙撃はまだ通信に乗せていないので、オンラインでは使えない）
    bool IsOnlineMatch => NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch;

    // 錬成のボタンを押せるか（操作できるとき・狙撃の対象選択中でない・ルールで錬成できる）
    public bool CanForge(ForgeRecipe recipe)
    {
        if (recipe == null || !CanActNow || isSelectingSnipeTarget || CurrentPlayer == null) return false;
        return FindRecipe(recipe.pieceType) == recipe && ForgeBlockReason(CurrentSide, recipe) == null;
    }

    // 錬成する（錬成ボタン・Z・X キー）：「錬成」行動を Dispatch。できたら true
    public bool Forge(ForgeRecipe recipe)
    {
        if (!CanForge(recipe)) return false;
        return Dispatch(MatchAction.Forge(recipe.pieceType));
    }

    // 錬成の実行（Dispatch の ExecutePlan から）
    void ExecuteForge(ForgeRecipe recipe)
    {
        Player player = CurrentPlayer;
        PlayerType side = player.PlayerType;
        player.ConsumePoint(recipe.cost);
        player.AddPieceToHand(recipe.pieceType);
        Debug.Log($"{side}：エナジーを {recipe.cost} 消費して「{recipe.label}」を錬成した！");

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(side, player.Hand);
        }
        if (isSelectingHand)
        {
            UpdateHandList();
            UpdateSelectingHandUI();
        }
        else if (!isDroppingHand && selectedPiece == null && HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【錬成】 {recipe.label}を持ち駒に加えた（エナジー -{recipe.cost}）" +
                (string.IsNullOrEmpty(IdleStatusText) ? "" : "\n" + IdleStatusText));
        }
        UpdateActivePlayerSkillUI();
        Forged?.Invoke(side, recipe);

        if (MatchConfig.ForgeEndsTurn)
        {
            ResetSelectionState();
            EndActionTurn();
        }
    }

    // キーボードで錬成（Z = 1つ目のレシピ、X = 2つ目、C = 3つ目）
    static readonly Key[] ForgeKeys = { Key.Z, Key.X, Key.C, Key.V };
    public static string ForgeKeyLabel(int index)
    {
        return index >= 0 && index < ForgeKeys.Length ? ForgeKeys[index].ToString() : "";
    }

    void HandleForgeKeys(Keyboard keyboard)
    {
        IReadOnlyList<ForgeRecipe> recipes = ForgeRecipes;
        for (int i = 0; i < recipes.Count && i < ForgeKeys.Length; i++)
        {
            if (keyboard[ForgeKeys[i]].wasPressedThisFrame)
            {
                if (!Forge(recipes[i]) && recipes[i] != null && CurrentPlayer != null && !CurrentPlayer.HasEnoughPoint(recipes[i].cost))
                {
                    Debug.Log($"「{recipes[i].label}」の錬成にはエナジーが {recipes[i].cost} 必要です");
                }
                return;
            }
        }
    }

    // 駒・持ち駒・狙撃の選択を全部やめる
    void ResetSelectionState()
    {
        selectedPiece = null;
        isSelectingHand = false;
        isDroppingHand = false;
        isSelectingSnipeTarget = false;
        snipingPiece = null;
        snipeTargets.Clear();
        currentMovablePositions.Clear();
        gameBoard.AllDeactivePanel();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        if (HandUIManager.Instance != null) HandUIManager.Instance.HidePanel();
    }

    // ------------------------------------------------------------
    //  行動（移動・打つ・狙撃）のあとの手番の交代。爆発・狙撃の演出があれば、終わるまで待ってから交代する
    // ------------------------------------------------------------
    void EndActionTurn()
    {
        float wait = pendingActionFxTime;
        pendingActionFxTime = 0f;
        if (wait <= 0f)
        {
            NextState();
            return;
        }
        StartCoroutine(EndTurnAfter(wait));
    }

    System.Collections.IEnumerator EndTurnAfter(float seconds)
    {
        isResolvingAction = true;
        yield return new WaitForSeconds(seconds);
        isResolvingAction = false;
        if (IsInTurn) NextState();
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

    // オンライン対戦：サーバーが認めた移動（自分の手も相手の手も、この通知で実行する）
    public void OnServerMovePiece(int fromX, int fromY, int toX, int toY, PlayerType activePlayer, bool promote)
    {
        ExecuteRemote(MatchAction.Move(new Vector2Int(fromX, fromY), new Vector2Int(toX, toY), promote), activePlayer);
    }

    // オンライン対戦：サーバーが認めた持ち駒を打つ手
    public void OnServerDropPiece(PieceType type, int toX, int toY, PlayerType activePlayer)
    {
        ExecuteRemote(MatchAction.Drop(type, new Vector2Int(toX, toY)), activePlayer);
    }

    public void OnServerGameOver(PlayerType serverWinner)
    {
        winner = serverWinner;
        currentState = GameState.GameOver;
    }

    void AddPieceToHand(PlayerType player, PieceType type)
    {
        if (!PieceRules.GoesToHand(type)) return; // 王将・火薬は持ち駒にならない

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
        if (timeStop != null)
        {
            // 時間停止中はエナジーが増えない（取りのエナジーは、時間が動き出して取りを処理するときに入る）
            Debug.Log($"（時間停止中）{player} のエナジー +{amount} は入らない");
            return;
        }
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
    // 取った駒は by の持ち駒になる（王将・火薬は持ち駒にならない）。取れたら true（盾などで防がれたら false）。
    // attacker は取ろうとした駒（演出・ログ用。わからなければ null）。
    // 王将が全部取られたかの判定は呼び出し側で行う（ResolveKingLoss）
    public bool CapturePiece(GamePiece piece, PlayerType by, GamePiece attacker = null)
    {
        if (!IsOnBoard(piece)) return false;

        // 時間停止中：取りの予定にして、取れたことにする（盾の判定・エナジー・持ち駒化は時間が動き出してから）
        if (timeStop != null)
        {
            DeferCapture(piece, by, attacker);
            return true;
        }

        // アンデッド（Prototype の effects.js の capture）：取られても持ち駒にならず、「成仏」で消失する（エナジーもなし）。
        // 盾は自分の魔法でしかかけられず、アンデッドは自分の魔法の対象にならないので、盾状態のアンデッドはいない
        if (piece.isUndead && !(piece.hasShield && piece.player != by))
        {
            return VanishPiece(piece, ReasonUndeadRest);
        }

        PieceDefeat defeat = new PieceDefeat(piece, DefeatKind.Capture, by, "取り");
        if (IsDefeatIntercepted(defeat)) return false;

        // 盾（Prototype の effects.js の tryCapture / isGuarded）：相手の取りを1回だけ防いで割れる。駒はそのマスに残る
        if (piece.hasShield && piece.player != by)
        {
            BreakShield(piece, attacker, by);
            return false;
        }

        AddPoint(defeat.Owner, PieceValues.CapturedSideGain(defeat.Type));
        AddPoint(by, PieceValues.CapturerGain(defeat.Type));
        RemoveDefeatedPiece(defeat);
        AddPieceToHand(by, defeat.Type); // 成った駒も元の駒として持ち駒になる
        return true;
    }

    // 駒を盾状態にする（魔法「盾」）。盾状態にしたら true（もう盾状態なら false）
    public bool GiveShield(GamePiece piece)
    {
        if (!IsOnBoard(piece) || piece.hasShield) return false;
        piece.hasShield = true;
        Debug.Log($"({piece.X}, {piece.Y})：{piece.player} の{HandUIManager.GetJapaneseName(piece.type)}が盾状態になった！");
        ShieldGiven?.Invoke(piece);
        return true;
    }

    // 駒をファランクス状態にする（必殺魔法「ファランクス」。Prototype の effects.js の givePhalanx）。盾状態はそのまま。
    // ファランクス状態にしたら true（もうファランクス状態なら false）
    public bool GivePhalanx(GamePiece piece)
    {
        if (!IsOnBoard(piece) || piece.hasPhalanx) return false;
        piece.hasPhalanx = true;
        Debug.Log($"({piece.X}, {piece.Y})：{piece.player} の{HandUIManager.GetJapaneseName(piece.type)}がファランクス状態になった！（手番の終わりに自動で前進して前方を突く）");
        PhalanxGiven?.Invoke(piece);
        return true;
    }

    // 盾が取りを防いで割れる（Prototype の effects.js の breakShield）
    void BreakShield(GamePiece piece, GamePiece attacker, PlayerType by)
    {
        piece.hasShield = false;
        float delay = ActiveSpecial != null ? Mathf.Max(0f, ActiveSpecial.HitTime) : captureFxDelay;
        pendingActionFxTime = Mathf.Max(pendingActionFxTime, delay + ShieldBreakFxDuration);
        string by2 = attacker != null ? $"{HandUIManager.GetJapaneseName(attacker.type)}の" : "";
        Debug.Log($"({piece.X}, {piece.Y})：{piece.player} の{HandUIManager.GetJapaneseName(piece.type)}の盾が{by2}攻撃を防いだ！（盾は割れた）");
        ShieldBroken?.Invoke(new ShieldBreak(piece, attacker, by, delay));
    }

    // 消失：エナジー・持ち駒化なしで盤から消える（爆発など）。盾では防げない。消えたら true（割り込みで止められたら false）
    public bool VanishPiece(GamePiece piece, string reason)
    {
        if (!IsOnBoard(piece)) return false;

        // 時間停止中：消失の予定にする（駒は灰色でその場に残り、以降はないものとして扱う）
        if (timeStop != null)
        {
            DeferDoom(piece, reason);
            return true;
        }

        PieceDefeat defeat = new PieceDefeat(piece, DefeatKind.Vanish, null, reason);
        if (IsDefeatIntercepted(defeat)) return false;

        RemoveDefeatedPiece(defeat);
        return true;
    }

    bool IsOnBoard(GamePiece piece)
    {
        if (piece == null || gameBoard == null) return false;
        if (gameBoard.GetPieceAt(piece.X, piece.Y) == piece) return true;
        // 時間停止の解除中は、予定の駒として残していた駒も盤上の駒として倒せる
        return resolvingTimeStop != null && resolvingTimeStop.IsHeld(piece);
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
        if (gameBoard.GetPieceAt(defeat.X, defeat.Y) == defeat.Piece)
        {
            gameBoard.RemovePieceAt(defeat.X, defeat.Y);
        }
        else
        {
            // 時間停止の予定の駒（盤のデータから外してある。そのマスに今いる別の駒は消さない）
            gameBoard.DestroyDetached(defeat.Piece);
        }
        if (resolvingTimeStop != null) resolvingTimeStop.Held.Remove(defeat.Piece);
        PieceDefeated?.Invoke(defeat);
    }

    // ------------------------------------------------------------
    //  マスの状態（破壊・炎上・落とし穴）の共通入口
    //  Prototype の effects.js の breakCell / setBurning / setTrap / enterCell。
    //  データは GameBoard.Cells（BoardCells）が持ち、見た目は BoardCellView が Cells.Changed を受けて更新する
    // ------------------------------------------------------------

    public BoardCells Cells => gameBoard != null ? gameBoard.Cells : null;
    public GameBoard Board => gameBoard;

    // マスを破壊する（対局終了まで戻らない）。そこにいた駒は消失する。破壊したら true。
    // 王将が消えることもあるので、呼び出し側で ResolveKingLoss を呼ぶこと（スキルなら ExecuteSkill が呼ぶ）
    public bool BreakCell(int x, int y)
    {
        BoardCells cells = Cells;
        if (cells == null) return false;
        if (timeStop != null)
        {
            // 時間停止中は予定にする（時間が動き出してから破壊する）
            BoardCell c = cells.At(x, y);
            if (c == null || c.IsBroken) return false;
            DeferCell(TimeStopCellEffect.Break, x, y, timeStop.Owner, 0);
            return true;
        }
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
        if (timeStop != null)
        {
            // 時間停止中は予定にする（時間が動き出したターンから数える）
            BoardCell c = cells.At(x, y);
            if (c == null || c.IsBroken || turns <= 0) return false;
            DeferCell(TimeStopCellEffect.Burn, x, y, timeStop.Owner, turns);
            return true;
        }
        return cells.SetBurning(x, y, turns, match.Turn.TurnNumber);
    }

    // 落とし穴を仕掛ける（owner は仕掛けたプレイヤー。穴は両プレイヤーに見える）。
    // 駒がいるマス・破壊マス・炎上のマスには置けない。仕掛けたら true
    public bool SetTrap(int x, int y, PlayerType owner)
    {
        BoardCells cells = Cells;
        if (cells == null || gameBoard.HasPieceAt(x, y)) return false;
        if (timeStop != null)
        {
            // 時間停止中は予定にする（時間が動き出してから仕掛ける）
            BoardCell c = cells.At(x, y);
            if (c == null || c.State != CellState.Normal || c.HasTrap) return false;
            DeferCell(TimeStopCellEffect.Trap, x, y, owner, 0);
            return true;
        }
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

        // 時間停止中は消失の予定にする（駒はそのマスに残るが、もう動かせない）
        if (timeStop != null && cell.IsHazard)
        {
            DeferEnterVanish(piece, x, y, cell.HasTrap ? BoardCells.ReasonTrap : BoardCells.ReasonBurning);
            return true;
        }

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

        // 移動の見た目の途中なら、そのマスに着いてから消える（BoardCellView の写しが 0.2 秒で進む）
        enterCellFxDelay = DG.Tweening.DOTween.IsTweening(piece.transform) ? 0.2f : 0f;
        VanishPiece(piece, reason);
        enterCellFxDelay = 0f;
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
        // 時間停止中、予定の駒（取られる予定など）が残っているマスには打てない
        if (timeStop != null && timeStop.IsHeldSquare(targetX, targetY)) return false;
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

    // owner が持ち駒 type を (x, y) に打つ（Dispatch・オンラインの通知の ExecutePlan から。判定は済んでいること）
    private void DropPiece(PlayerType owner, PieceType type, int x, int y)
    {
        Player ownerPlayer = PlayerOf(owner);
        ownerPlayer.RemovePieceFromHand(type);
        var hand = ownerPlayer.Hand;

        GamePiece dropped = gameBoard.SpawnPiece(type, owner, x, y);
        if (dropped != null && basicMoveFx != null) basicMoveFx.Dropped(dropped);
        if (dropped != null) EnterCell(dropped, x, y); // 炎上のマスに打ったら消失する

        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(owner, hand);
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


    // 行動が終わった：2回行動なら手番を続ける。そうでなければ、手番交代の前の自動行動（ファランクスなど）→ 手番交代
    void NextState()
    {
        // 時間停止中：1 回の行動では手番を終えない（回数を数え、使い切ったら予定をまとめて処理してから手番の流れに戻る）
        if (TimeStopAfterAction(true)) return;

        if (hasExtraTurn)
        {
            hasExtraTurn = false;
            isSelectingHand = false;
            isDroppingHand = false;
            Debug.Log("追加行動権により、同じプレイヤーのターンが続きます。");
            UpdateActivePlayerSkillUI(); // 獲得したポイントをスキルボタンに反映
            CheckTranscendentUnlock();   // 手番を続ける行動のあとも解放を判定する
            return; // ターンを切り替えずに同じプレイヤーのターンを続ける
        }

        if (!isRunningTurnEnd && IsInTurn)
        {
            PlayerType side = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
            if (HasTurnEndWork(side))
            {
                RunTurnEndActions(side);
                return; // 自動行動の演出が終わってから PassTurn
            }
        }
        PassTurn();
    }

    bool HasTurnEndWork(PlayerType side)
    {
        foreach (TurnEndAction action in TurnEndActionsInOrder())
        {
            if (action.HasWork(this, side)) return true;
        }
        return false;
    }

    // 手番側の自動行動を順に行う：小さなカットイン → 効果（盤面はすぐ変わり、演出は時間軸どおりに再生）→ 演出を待つ → 勝敗の判定。
    // 全部終わったら手番を相手に渡す
    async void RunTurnEndActions(PlayerType side)
    {
        isRunningTurnEnd = true;
        isResolvingAction = true;
        ResetSelectionState();
        try
        {
            foreach (TurnEndAction action in TurnEndActionsInOrder())
            {
                if (!IsInTurn) return;
                if (!action.HasWork(this, side)) continue;

                if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【{action.Title}】 {action.GetStatusText(side)}");
                if (!string.IsNullOrEmpty(action.CutInName))
                {
                    await CutInEffect.Instance.PlayMiniAsync(action.CutInName, action.GetCutInSub(this, side), action.Color, destroyCancellationToken, 0f, action.CutInStyle);
                }
                if (!IsInTurn) return;

                SpecialContext timeline = new SpecialContext(this, gameBoard, side, specialFx);
                pendingActionFxTime = 0f;
                ActiveSpecial = timeline;
                try
                {
                    action.Run(new TurnEndContext(this, gameBoard, side, timeline, magicFx));
                }
                catch (System.Exception e)
                {
                    Debug.LogException(e);
                }
                finally
                {
                    ActiveSpecial = null;
                }

                float wait = Mathf.Max(pendingActionFxTime, timeline.Time + 0.2f);
                pendingActionFxTime = 0f;
                await Awaitable.WaitForSecondsAsync(wait, destroyCancellationToken);
                UpdateActivePlayerSkillUI();
                if (ResolveKingLoss()) return; // 自動行動で王将を取った・巻き込んだ
            }
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }
        finally
        {
            isRunningTurnEnd = false;
        }

        isResolvingAction = false;
        if (IsInTurn) PassTurn();
    }

    // 手番を相手に渡す（ターン数・エナジー・詰み・千日手の判定は OnTurnStarted）
    void PassTurn()
    {
        // ターン切り替え時に状態をクリア
        isSelectingHand = false;
        isDroppingHand = false;
        isSelectingSnipeTarget = false;
        snipingPiece = null;
        snipeTargets.Clear();
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
        if (basicMoveFx != null)
        {
            if (match.Turn.MoveNumber <= 1) basicMoveFx.GameStart();
            else basicMoveFx.TurnStarted(side);
        }
        if (side == match.Turn.FirstPlayer && match.Turn.IsDropUnlockTurn)
        {
            turnNotice = $"第{match.Turn.TurnNumber}ターン：敵陣への持ち駒の打ち込みが解禁された！";
            Debug.Log(turnNotice);
            if (basicMoveFx != null) basicMoveFx.DropUnlocked();
        }
        AddPoint(side, MatchConfig.PointsPerTurn);
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetTurnInfo(match.Turn.Label);
        }

        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, side);
        if (isActivePlayerInCheck && basicMoveFx != null) basicMoveFx.Check();

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

        // 超常魔法の解放判定（両プレイヤー分。解放したらカットイン）
        CheckTranscendentUnlock();
    }

    // 操作中でないときのステータス表示（王手中なら知らせる。手番の始まりのお知らせもここに出す）
    string IdleStatusText
    {
        get
        {
            if (timeStop != null) return TimeStopStatusText;
            string check = isActivePlayerInCheck ? "【王手！】 放置すると次の手で王将を取られます" : "";
            if (string.IsNullOrEmpty(turnNotice)) return check;
            if (string.IsNullOrEmpty(check)) return turnNotice;
            return check + "\n" + turnNotice;
        }
    }

    // 1ターンの終わり（後手の手番が終わったとき。手番交代の前）。
    // 期限切れの炎上を鎮火する。演出を見せてから手番を渡したいもの（世界の終わりの崩壊）は、
    // この直前に roundEndActions（WorldEndCollapse）として行っている
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

    // 王手を回避できる手（駒の移動・狙撃・持ち駒を打つ）が1つもないか。スキル・駒錬成は判定に含めない
    // 王手放置は禁止していないので、「指せる手」のうち王将が取られなくなる手があるかを調べる
    bool IsCheckmated(PlayerType side)
    {
        GamePiece[] cells = gameBoard.GetBoardData();
        for (int i = 0; i < cells.Length; i++)
        {
            GamePiece piece = cells[i];
            if (piece == null || piece.player != side || piece.isUndead) continue; // アンデッドは持ち主が動かせない

            for (int x = 0; x < 9; x++)
            {
                for (int y = 0; y < 9; y++)
                {
                    if (CanPieceMoveTo(piece, x, y) && CheckRules.IsMoveSafe(gameBoard, piece, x, y)) return false;
                }
            }
        }

        // 狙撃（通常の行動なので含める。王手をかけている駒を撃つ・火薬を撃って爆発させる）
        for (int i = 0; i < cells.Length; i++)
        {
            GamePiece sniper = cells[i];
            if (sniper == null || sniper.player != side || !PieceRules.IsSniper(sniper.type)) continue;
            foreach (Vector2Int t in GetSnipeTargets(sniper))
            {
                if (CheckRules.IsSnipeSafe(gameBoard, sniper, t.x, t.y)) return false;
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
            // 必殺技のチャージ（必要数で頭打ち。チャージが違えば指せる手が違うので別の局面）
            SpecialMove special = GetSpecialMove(piece);
            if (special != null) sb.Append(Mathf.Min(piece.moveCount, special.requiredMoves));
            if (piece.hasShield) sb.Append('s'); // 盾状態（Prototype の hash.js と同じく局面に含める）
            if (piece.hasPhalanx) sb.Append('p'); // ファランクス状態（〃）
            if (piece.isUndead) sb.Append('u');   // アンデッド（〃）
        }
        foreach (Player p in new[] { player1, player2 })
        {
            sb.Append('|');
            if (p == null) continue;
            foreach (PieceType type in PieceRules.HandOrder)
            {
                int num;
                p.Hand.TryGetValue(type, out num);
                sb.Append(num).Append(',');
            }
        }
        sb.Append(sideToMove == PlayerType.Player1 ? '1' : '2');
        gameBoard.Cells.AppendKey(sb); // マスの状態（破壊・炎上・落とし穴）も局面に含める
        match.AppendKey(sb);           // 超常魔法の解放・使用・世界の終わり（Prototype の hash.js と同じ）
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

    // ------------------------------------------------------------
    //  スキル（魔法スキル）の発動（Prototype の play.js の castMagic・skills.js の magicStatus）
    //  スキルボタン → 対象を選ぶスキルなら対象選択 → 魔法陣 → カットイン → 効果。
    //  SkillDefinition.EndsTurn が true のスキル（盾・剣・落とし穴など）は手番を消費する（MatchConfig.MagicEndsTurn）。
    //  2回行動・駒入替・爆発・王将複製は今までどおり手番を消費しない
    // ------------------------------------------------------------

    Player PlayerOf(PlayerType type)
    {
        return type == PlayerType.Player1 ? player1 : player2;
    }

    // type が編成した魔法（ルール説明の一覧の印などに使う。なければ null）
    public SkillDeck GetSkillDeck(PlayerType type)
    {
        Player p = PlayerOf(type);
        return p != null ? p.SkillDeck : null;
    }

    // ------------------------------------------------------------
    //  超常魔法の解放（Prototype の turn.js の checkTranscendentUnlock・Game.js の showTranscendentUnlock）
    // ------------------------------------------------------------

    // owner が編成した超常魔法（なければ null）
    public SkillDefinition GetTranscendentSkill(PlayerType owner)
    {
        Player p = PlayerOf(owner);
        if (p == null || p.SkillDeck == null || p.SkillDeck.skills == null) return null;
        foreach (SkillDefinition skill in p.SkillDeck.skills)
        {
            if (skill != null && skill.rank == SkillRank.Transcendent) return skill;
        }
        return null;
    }

    public bool IsTranscendentUnlocked(PlayerType owner) => match != null && match.IsTranscendentUnlocked(owner);
    public bool IsTranscendentUsed(PlayerType owner) => match != null && match.IsTranscendentUsed(owner);

    // 解放条件の達成状況（盤上の今の状態。魔法ボタンの下の表示用）
    public TranscendentProgress GetTranscendentProgress(PlayerType owner)
    {
        return TranscendentRules.Progress(gameBoard, owner);
    }

    // 両プレイヤーの解放条件を調べ、満たしていれば解放する（以後は条件が崩れても解放のまま）。
    // 超常魔法を編成していれば「超常魔法 解放！」のカットインを出す。
    // 手番の始まりと、手番を続ける行動（2回行動・手番を使わない魔法）のあとに呼ぶ
    void CheckTranscendentUnlock()
    {
        if (match == null || gameBoard == null || !IsInTurn) return;
        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            if (match.IsTranscendentUnlocked(owner) || !TranscendentRules.Progress(gameBoard, owner).Met) continue;
            match.UnlockTranscendent(owner);
            SkillDefinition skill = GetTranscendentSkill(owner);
            Debug.Log($"{TurnEndAction.SideName(owner)}：超常魔法の解放条件を満たした" + (skill != null ? $"（「{skill.skillName}」が使える。1対局に1回だけ）" : ""));
            if (skill != null) unlockQueue.Enqueue(owner);
        }
        UpdateActivePlayerSkillUI();
        if (unlockQueue.Count > 0 && !isShowingUnlock) ShowTranscendentUnlocks();
    }

    // 解放の演出：カットイン →（TranscendentUnlocked でスキルボタンに「解放！」）→ 少し待つ。その間は操作できない
    async void ShowTranscendentUnlocks()
    {
        isShowingUnlock = true;
        try
        {
            while (unlockQueue.Count > 0 && IsInTurn)
            {
                PlayerType owner = unlockQueue.Dequeue();
                SkillDefinition skill = GetTranscendentSkill(owner);
                if (skill == null) continue;
                if (HandUIManager.Instance != null)
                {
                    HandUIManager.Instance.SetStatusText($"【超常魔法 解放！】 {TurnEndAction.SideName(owner)}の「{skill.skillName}」が使えるようになった（1対局に1回だけ）");
                }
                Color color = SkillRanks.ColorOf(SkillRank.Transcendent);
                await CutInEffect.Instance.PlayAsync("超常魔法 解放！", $"{TurnEndAction.SideName(owner)}・「{skill.skillName}」が使えるようになった", color, destroyCancellationToken);
                UpdateActivePlayerSkillUI();
                TranscendentUnlocked?.Invoke(owner);
                await Awaitable.WaitForSecondsAsync(0.8f, destroyCancellationToken);
            }
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }
        finally
        {
            isShowingUnlock = false;
        }
        unlockQueue.Clear();
        if (!IsInTurn) return;
        UpdateActivePlayerSkillUI();
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
    }

    // ------------------------------------------------------------
    //  天変地異・世界の終わり（Prototype の effects.js の shuffleCells・play.js の startWorldEnd・worldEndCollapse）
    //  乱数は対局の乱数（match.Rng）を使う
    // ------------------------------------------------------------

    // 破壊されていないすべてのマスを、駒・マスの状態（炎上と残りターン・落とし穴）ごとランダムに入れ替える。
    // 破壊マスは動かない。マスごとの移動なので、成り・火薬の爆発・炎上・落とし穴は発動しない（盾・必殺技の移動回数なども、そのまま駒と一緒に動く）。
    // 盤面のデータだけ変える（見た目は呼び出し側が MagicFx.Cataclysm で飛ばす）。入れ替えの一覧を返す
    public List<CellMove> ShuffleCells()
    {
        List<CellMove> moves = new List<CellMove>();
        if (gameBoard == null || match == null) return moves;

        List<Vector2Int> squares = new List<Vector2Int>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (!Cells.IsBroken(x, y)) squares.Add(new Vector2Int(x, y));
            }
        }
        // squares[i] には、シャッフルした from[i] にあった駒・マスの状態が入る
        List<Vector2Int> from = match.Rng.Shuffle(squares);
        for (int i = 0; i < squares.Count; i++)
        {
            BoardCell cell = Cells.At(from[i].x, from[i].y);
            moves.Add(new CellMove
            {
                From = from[i],
                To = squares[i],
                Piece = gameBoard.GetPieceAt(from[i].x, from[i].y),
                Burning = cell != null && cell.IsBurning,
                Trap = cell != null && cell.HasTrap,
            });
        }
        gameBoard.RearrangePieces(from, squares);
        Cells.Rearrange(from, squares);
        return moves;
    }

    // ------------------------------------------------------------
    //  アンデッド・パラダイス（Prototype の effects.js の reviveUndead）
    // ------------------------------------------------------------

    // type の駒を owner のアンデッドとして (x, y) に復活させる（成っていない状態。空きの通常マスに置くこと）。作った駒を返す。
    // データはすぐ盤に入る。見た目（地面から這い上がる）は呼び出し側が MagicFx.UndeadRise で再生する
    public GamePiece ReviveUndead(PieceType type, PlayerType owner, int x, int y)
    {
        if (gameBoard == null || gameBoard.HasPieceAt(x, y)) return null;
        GamePiece piece = gameBoard.SpawnPiece(type, owner, x, y);
        if (piece == null) return null;
        piece.MakeUndead(); // 初期配置の駒ではない（倒されても記録しない）
        UndeadRisen?.Invoke(piece);
        return piece;
    }

    // 世界の終わりが進行中か（どちらかが発動した）
    public bool IsWorldEndActive => match != null && match.IsWorldEndActive;

    // 世界の終わりを始める（今のターンから。以後、ターンの終わりごとに WorldEndCollapse が崩す）
    public void StartWorldEnd(PlayerType owner)
    {
        if (match == null) return;
        match.StartWorldEnd(owner);
        Debug.Log($"世界の終わりが始まった…… 1ターンごとに {MatchConfig.WorldEndCells} マスが崩れ落ちる（{TurnEndAction.SideName(owner)}が発動）");
        WorldEndStarted?.Invoke(owner);
    }

    // 世界の終わりで崩れる候補：破壊されていないマスのうち、王将がいないマス
    public List<Vector2Int> WorldEndCandidates()
    {
        List<Vector2Int> pool = new List<Vector2Int>();
        if (gameBoard == null) return pool;
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (Cells.IsBroken(x, y)) continue;
                GamePiece q = gameBoard.GetPieceAt(x, y);
                if (q != null && q.type == PieceType.King) continue;
                pool.Add(new Vector2Int(x, y));
            }
        }
        return pool;
    }

    // 候補から n マスをランダムに選ぶ（対局の乱数。候補が n マス未満なら候補全部）
    public List<Vector2Int> PickWorldEndCells(int n)
    {
        List<Vector2Int> pool = WorldEndCandidates();
        if (match == null || pool.Count == 0 || n <= 0) return new List<Vector2Int>();
        List<Vector2Int> shuffled = match.Rng.Shuffle(pool);
        return shuffled.GetRange(0, Mathf.Min(n, shuffled.Count));
    }

    // スキルを今の盤面で使えるか（エナジー・オンライン・効果の対象。手番中の操作の状態は UseSkill が見る）。スキルボタンの押せる／押せないにも使う
    public bool CanUseSkill(PlayerType player, SkillDefinition skill)
    {
        return SkillBlockReason(player, skill) == null;
    }

    // スキルを使えない理由（使えるなら null）。Prototype の magicStatus の reason
    string SkillBlockReason(PlayerType player, SkillDefinition skill)
    {
        if (skill == null || skill.effect == null) return "効果が設定されていません";
        Player owner = PlayerOf(player);
        if (owner == null) return "プレイヤーがいません";
        if (skill.rank == SkillRank.Transcendent && match != null)
        {
            // 超常魔法：1 対局に 1 回だけ・解放条件を満たすまで使えない
            if (match.IsTranscendentUsed(player)) return "使用済み：1対局に1回だけ";
            if (!match.IsTranscendentUnlocked(player)) return "未解放：相手の駒が自陣にいて、盤上の駒数が相手より少ないときに解放";
        }
        if (!owner.HasEnoughPoint(skill.cost)) return $"エナジー不足：あと {skill.cost - owner.Energy}";
        // 手番を使う魔法・剣などの演出は、まだオンライン対戦の通信に乗せていない
        if (skill.EndsTurn && IsOnlineMatch) return "オンライン対戦ではまだ使えません";
        if (timeStop != null && skill.effect is DoubleTurnEffect) return "時間停止中は使えません";
        if (!skill.effect.CanUse(new SkillContext(this, gameBoard, player))) return "今は効果の対象がありません";
        return null;
    }

    public void UseSkill(PlayerType player, int slotIndex)
    {
        // 成りの選択中・スキルや狙撃の対象選択中・演出中は使えない
        if (isWaitingForPromoteChoice || IsSelectingSkillTarget || isSelectingSnipeTarget || IsBusy) return;
        if (!IsInTurn) return;

        // 自分のターンであるかチェック
        PlayerType activePlayer = IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        if (player != activePlayer)
        {
            Debug.LogWarning("相手のターンにはスキルを使用できません。");
            return;
        }
        Player targetPlayer = PlayerOf(player);
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

        // コスト・盤面の条件
        string reason = SkillBlockReason(player, skill);
        if (reason != null)
        {
            Debug.LogWarning($"スキル「{skill.skillName}」は使えません：{reason}");
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【{skill.skillName}】 {reason}");
            return;
        }

        // 対象を選ぶスキルは、選び終わってから発動する（ポイントもその時に消費）
        if (skill.effect.TargetCount > 0)
        {
            BeginSkillTargetSelection(skill, new SkillContext(this, gameBoard, player));
            return;
        }
        Dispatch(MatchAction.Magic(skill.Id)); // 対象を選ばない魔法はすぐ発動
    }

    // 発動（Dispatch の ExecutePlan から）：エナジー消費 → 魔法陣 → カットイン → 効果 →（手番を使うスキルは）演出を待って手番交代
    async void ExecuteSkill(SkillDefinition skill, SkillContext context)
    {
        PlayerType player = context.Player;
        Player targetPlayer = PlayerOf(player);
        bool endsTurn = skill.EndsTurn;
        string rankLabel = SkillRanks.Label(skill.rank);
        Vector2Int? focus = context.Targets.Count > 0 ? context.Targets[0] : (Vector2Int?)null;

        // 選択状態を解除し、演出のあいだは操作できないようにする
        ResetSelectionState();
        isResolvingAction = true;

        // コスト消費（発動の時点で払う。対象選択を取り消したときは払わない）
        targetPlayer.ConsumePoint(skill.cost);
        if (skill.rank == SkillRank.Transcendent) match.MarkTranscendentUsed(player); // 超常魔法は 1 対局に 1 回
        UpdateActivePlayerSkillUI();
        Debug.Log($"プレイヤー {(player == PlayerType.Player1 ? "1" : "2")} が{rankLabel}「{skill.skillName}」を発動！（エナジー -{skill.cost}）");
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【{rankLabel}】 {skill.skillName}！");
        }

        try
        {
            if (magicFx != null) magicFx.PlayCast(skill, focus);
            await Awaitable.WaitForSecondsAsync(0.25f, destroyCancellationToken);
            string side = player == PlayerType.Player1 ? "先手" : "後手";
            await CutInEffect.Instance.PlayAsync(skill.skillName, $"{side}・{rankLabel}", skill.color, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }

        if (!IsInTurn)
        {
            isResolvingAction = false;
            return;
        }

        // 効果（盤面はすぐ変わり、演出は Timeline の時刻どおりに再生される）
        context.Timeline = new SpecialContext(this, gameBoard, player, specialFx);
        context.Fx = magicFx;
        pendingActionFxTime = 0f;
        ActiveSpecial = context.Timeline;
        try
        {
            skill.Use(context);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            ActiveSpecial = null;
        }

        if (!endsTurn && IsOnlineMatch)
        {
            // オンライン対戦時はサーバーへ通知（手番を使わない今までのスキルのみ）
            NetworkManager.Instance.SendUseSkillRequest();
        }

        if (endsTurn)
        {
            // 演出を見せ終わってから、勝敗の判定と手番の交代
            float wait = Mathf.Max(pendingActionFxTime, context.Timeline.Time + 0.3f);
            pendingActionFxTime = 0f;
            try
            {
                await Awaitable.WaitForSecondsAsync(wait, destroyCancellationToken);
            }
            catch (System.OperationCanceledException)
            {
                return;
            }
            isResolvingAction = false;
            UpdateActivePlayerSkillUI();
            if (ResolveKingLoss()) return;
            if (IsInTurn) NextState();
            return;
        }

        // 手番を使わないスキル：演出は待たずに、続けて操作できる
        pendingActionFxTime = 0f;
        isResolvingAction = false;
        UpdateActivePlayerSkillUI();
        if (ResolveKingLoss()) return; // スキルで王将が全部いなくなった（爆発では王将は倒せないので、今のスキルでは起きない）
        if (TimeStopAfterAction(false)) return; // 時間停止中は 1 回に数える（使い切ったら予定を処理して、手番を続ける）
        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, player);
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText($"【{rankLabel}】 {skill.skillName} 発動！" +
                (string.IsNullOrEmpty(IdleStatusText) ? "" : "\n" + IdleStatusText));
        }
        CheckTranscendentUnlock(); // 手番を続ける行動のあとも解放を判定する（Prototype の afterAction）
    }

    void BeginSkillTargetSelection(SkillDefinition skill, SkillContext context)
    {
        SkillEffect effect = skill.effect;
        BeginTargetSelection(new TargetSelection
        {
            Title = skill.skillName,
            Count = effect.TargetCount,
            Chosen = context.Targets,
            IsValid = pos => effect.IsValidTarget(context, pos),
            Prompt = index => effect.GetTargetPrompt(index),
            Preview = cursor => effect.GetPreview(context, cursor),
            // 選び終わった：「魔法」行動を Dispatch（ポイントは発動するときに消費する）
            OnComplete = () => Dispatch(MatchAction.Magic(skill.Id, context.Targets, context.Orient)),
            AreaColor = skill.color,
            BlockedText = effect.BlockedText,
            OrientCount = Mathf.Max(1, effect.OrientCount),
            Orient = context.Orient,
            SetOrient = orient => context.Orient = orient,
            OrientLabel = effect.GetOrientLabel,
        });
    }

    // マスを選ぶモードに入る（スキル・必殺技の共通）
    void BeginTargetSelection(TargetSelection selection)
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

        targetSelection = selection;
        lastPreviewCursor = new Vector2Int(-1, -1);

        // 必殺技：画面を暗くし、使う駒と、選べるマスにいる駒を光らせる
        if (selection.Caster != null)
        {
            SetDarkOverlayActive(true);
            selection.Caster.SetHighlight(true);
            highlightedPieces.Add(selection.Caster);
            foreach (Vector2Int pos in selection.Preview(new Vector2Int(-1, -1)).Panels)
            {
                GamePiece q = gameBoard.GetPieceAt(pos.x, pos.y);
                if (q != null && q != selection.Caster)
                {
                    q.SetHighlight(true);
                    highlightedPieces.Add(q);
                }
            }
        }
        UpdateTargetPrompt(null);
        UpdateActivePlayerSkillUI();
    }

    // 案内文（範囲を選べないときはその理由、向きを切り替えられるときは今の向きも出す）
    void UpdateTargetPrompt(SkillPreview preview)
    {
        if (HandUIManager.Instance == null || targetSelection == null) return;
        TargetSelection selection = targetSelection;
        string prompt = selection.Prompt(selection.Chosen.Count);
        string orient = "";
        if (selection.OrientCount > 1)
        {
            string label = selection.OrientLabel != null ? selection.OrientLabel(selection.Orient) : "";
            orient = $"\n向き: {(string.IsNullOrEmpty(label) ? (selection.Orient + 1).ToString() : label)}（R で切り替え）";
        }
        string blocked = preview != null && preview.IsBlocked ? $"\n× {selection.BlockedText}" : "";
        HandUIManager.Instance.SetStatusText($"【{selection.Title}】 {prompt} (決定: F / 取消: Space){orient}{blocked}");
    }

    // 範囲の向きを次へ（R キー）。向きが1つしかなければ何もしない
    public void RotateTargetOrient()
    {
        TargetSelection selection = targetSelection;
        if (selection == null || selection.OrientCount <= 1) return;
        selection.Orient = (selection.Orient + 1) % selection.OrientCount;
        if (selection.SetOrient != null) selection.SetOrient(selection.Orient);
        lastPreviewCursor = new Vector2Int(-1, -1); // 光らせ直す
    }

    void HandleTargetSelection()
    {
        TargetSelection selection = targetSelection;
        Vector2Int cursor = new Vector2Int(gameCursor.X, gameCursor.Y);

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
        {
            RotateTargetOrient();
        }

        // カーソルが動いたら（向きを変えたら）、光らせるマス・塗る範囲を更新する
        if (cursor != lastPreviewCursor)
        {
            lastPreviewCursor = cursor;
            SkillPreview preview = selection.Preview(cursor);
            gameBoard.AllDeactivePanel();
            foreach (Vector2Int pos in preview.Panels)
            {
                gameBoard.ActivePanel(GameHelper.CalcPanelNum(pos.x, pos.y));
            }
            if (skillAreaView != null) skillAreaView.Show(preview.Area, preview.Blockers, selection.AreaColor);
            UpdateTargetPrompt(preview);
        }

        if (gameController.IsOkTrigger())
        {
            SkillPreview preview = selection.Preview(cursor);
            if (preview.IsBlocked)
            {
                Debug.Log(selection.BlockedText);
                if (skillAreaView != null) skillAreaView.Shake();
                return;
            }
            if (!selection.IsValid(cursor))
            {
                Debug.Log("そのマスは選べません");
                return;
            }

            selection.Chosen.Add(cursor);
            lastPreviewCursor = new Vector2Int(-1, -1); // 選択状態が変わったので光らせ直す
            if (selection.Chosen.Count < selection.Count)
            {
                UpdateTargetPrompt(null);
                return;
            }

            EndTargetSelection();
            selection.OnComplete();
        }
        else if (gameController.IsCancelTrigger())
        {
            CancelTargetSelection();
        }
    }

    // 対象選択をやめる（スキルのポイント・必殺技のチャージは減らない）。「必殺技をやめる」ボタン・Space
    public void CancelTargetSelection()
    {
        if (targetSelection == null) return;
        EndTargetSelection();
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetStatusText(IdleStatusText);
        }
    }

    void EndTargetSelection()
    {
        targetSelection = null;
        selectedPiece = null;
        gameBoard.AllDeactivePanel();
        if (skillAreaView != null) skillAreaView.Hide();
        ClearBoardHighlight();
        SetDarkOverlayActive(false);
        UpdateActivePlayerSkillUI();
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
            if (target != null && selected != null && target != selected && (target.player != selected.player || PieceRules.IsExplosive(target.type)))
            {
                target.SetHighlight(true);
                highlightedPieces.Add(target);
            }
        }
    }

    // 指定したプレイヤーの王将が盤面にまだ残っているかをチェックする
    private bool IsKingAlive(PlayerType player)
    {
        // 時間停止中に取られる予定の王将は、まだ倒されていない
        if (timeStop != null && timeStop.HasHeldKing(player)) return true;

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
                    // エナジーが足りていて、今の盤面で効果の対象がある（盾をかけられる駒がある など）ときだけ押せる
                    skillSlotUIs[i].button.interactable = CanUseSkill(activePlayer.PlayerType, skill);
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
