using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 合戦将棋型（手番のないリアルタイム制）。GameSystem の続き（partial）。
// Prototype の src/match/RealtimeMatch.js（固定刻みのゲームループ）・src/core/clock.js の tick・turn.js の afterRealtimeAction。
//
//  ・手番はない。1 台のキーボードを左右に分けた 2 つの席（InputSeat。先手 WASD 側・後手 矢印側）で、両者が同時に操作する。
//    自分の駒・自分の持ち駒・自分の魔法のキーを選べば、その人の行動になる（行動の owner）。
//    スキルボタン・錬成ボタン（マウス）は、最後にキーを押した席（操作中の席 FocusSide）の分を出す
//  ・時間は 1 tick = MatchConfig.TickMs（50ms）の固定刻み。Update が実時間を溜めて、1 tick ずつ StepRealtime を呼ぶ
//      1. 席の Controller（CPU など。IRealtimeSeatController）から、この tick に入れる行動を集める
//      2. キュー（Dispatch で積んだ行動と 1 の行動）を積んだ順に、判定（PlanAction）→ 実行（ExecutePlan）→ AfterRealtimeAction
//      3. RealtimeTick（時間を 1 tick 進める：毎秒のエナジー・打ち込みの解禁・鎮火・世界の終わり・自動行動・超常魔法の解放）
//    入力は Dispatch でキューに入れ、次の tick の頭で実行する（Command パターン）。行動には行動したプレイヤー（owner）と
//    実行した時刻（t = Clock.Tick）が付く（ActionHistory）。同じシードと同じ「tick つきの行動の列」なら同じ対局になる
//  ・演出は待たない（MatchPolicy.AwaitsEffects = false）：必殺技・魔法・時間停止の解除はその場で盤面を変え、演出は重ねて再生する。
//    全画面のカットインは出さない（MatchPolicy.ShowsCutIns = false）。盤面を変える予定（SpecialContext.ScheduleState）もその場で行う
//  ・「一時停止」（Esc・HUD のボタン）。ルール説明を開いている間・ウィンドウが非アクティブの間も止まる（時間も入力も止まる）
//  ・行動のハッシュ（ActionHistory・棋譜の hash）は、その行動の処理（AfterRealtimeAction）が終わった直後に取る（Prototype の RealtimeMatch.play。
//    次の tick を進める前なので、フレームの長さに左右されない）
//  ・席の Controller（IRealtimeSeatController）：CPU（RealtimeCpuPacer ＋ CpuPlayer。GameSystem.Cpu.cs）・棋譜の再生（RealtimeReplayController。
//    両方の席に付ける。GameSystem.Kifu.cs）。Controller の席は人が操作できない
public partial class GameSystem
{
    // 戦闘型の違いの入口（対局の準備の前は本将棋型として扱う）
    private static readonly MatchPolicy fallbackPolicy = new MatchPolicy(BattleRulesets.Honshogi, null);
    public MatchPolicy Policy => match != null ? match.Policy : fallbackPolicy;

    // 合戦将棋型か（手番がなく、時間で進む）
    public bool IsRealtime => match != null && match.Policy.IsRealtime;

    // 盤面を変える予定（SpecialContext.ScheduleState）をその場で行うか（演出を待たない戦闘型）
    public bool AppliesStateImmediately => !Policy.AwaitsEffects;

    // ------------------------------------------------------------
    //  席
    // ------------------------------------------------------------

    private InputSeat[] realtimeSeats;             // [先手, 後手]（合戦将棋型だけ）
    private PlayerType focusSide = PlayerType.Player1; // 操作中の席（最後にキーを押した席。UI はこの席の分を出す）
    private bool selectionsDirty = true;             // 席の選択の表示（光らせるマス・駒）を描き直すか
    private bool boardChangedThisStep = false;        // この tick に盤面が変わったか（席の選択を確かめ直す）

    public PlayerType FocusSide => IsRealtime ? focusSide : CurrentSide;

    // 先手・後手の席（本将棋型は共通の席）
    public InputSeat SeatOf(PlayerType owner)
    {
        if (realtimeSeats == null) return sharedSeat;
        return realtimeSeats[(int)owner];
    }

    // 席のキー（ルール説明・HUD の表示用）
    public SeatKeys KeysOf(PlayerType owner) => SeatOf(owner).Keys;

    // 席で最後に出した案内文（HUD 用）
    public string SeatStatus(PlayerType owner) => SeatOf(owner).Status;

    // 席が今、操作できるか（対局中・一時停止していない・CPU の席でない・相手の時間停止中でない）。カーソルも動かない
    public bool CanSeatOperate(InputSeat s)
    {
        if (s == null || !IsInTurn) return false;
        if (!IsRealtime) return true;
        if (IsRealtimePaused || !s.Owner.HasValue) return false;
        if (seatControllers[(int)s.Owner.Value] != null) return false;
        return timeStop == null || timeStop.Owner == s.Owner.Value;
    }

    // カーソルの上下左右を逆にするか（本将棋型の後手の手番は盤が回っている。合戦将棋型は盤を回さない）
    public bool IsCursorFlipped(InputSeat s) => !IsRealtime && !IsPlayer1Turn();

    // カーソルが動いた（合戦将棋型：その席を操作中にする）
    public void NotifyCursorMoved(InputSeat s)
    {
        if (!IsRealtime || s == null || !s.Owner.HasValue) return;
        FocusSeat(s.Owner.Value);
        selectionsDirty = true;
    }

    // 席を切り替える（中の処理用。UI は変えない）：選択の状態と「今のプレイヤー」（CurrentPlayer・IsMyPiece など）をその席にする
    void UseSeat(PlayerType owner)
    {
        seat = SeatOf(owner);
        if (IsInTurn) currentState = owner == PlayerType.Player1 ? GameState.Player1Turn : GameState.Player2Turn;
    }

    // 操作中の席を変える（UI をその席の分にする）
    void FocusSeat(PlayerType owner)
    {
        bool changed = focusSide != owner;
        focusSide = owner;
        UseSeat(owner);
        if (changed) RefreshFocusUI();
    }

    // 操作中の席の UI（持ち駒の表示・持ち駒の選択パネル・成りの確認・スキルボタン・案内文）
    void RefreshFocusUI()
    {
        if (!IsInTurn) return;
        InputSeat s = Seat;
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(focusSide, CurrentPlayer.Hand);
            if (s.IsSelectingHand) HandUIManager.Instance.ShowPanel();
            else HandUIManager.Instance.HidePanel();
            HandUIManager.Instance.SetStatusText(s.Status);
        }
        if (promoteConfirmPanel != null) promoteConfirmPanel.SetActive(s.IsWaitingForPromoteChoice);
        UpdatePromoteChoiceVisuals();
        UpdateActivePlayerSkillUI();
        selectionsDirty = true;
    }

    void MarkSelectionsDirty()
    {
        selectionsDirty = true;
    }

    // 席 owner に案内文を出す（操作中の席なら画面の案内文にも出す）
    void ShowSeatNotice(PlayerType owner, string text)
    {
        if (!IsRealtime)
        {
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(text);
            return;
        }
        SeatOf(owner).Status = text;
        if (owner == focusSide && Seat == SeatOf(owner) && HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(text);
    }

    // 席 owner に案内文を出す（CPU の席の「考えています」など。合戦将棋型は HUD の席のパネルに出る）
    public void ShowSeatStatus(PlayerType owner, string text) => ShowSeatNotice(owner, text);

    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------

    // 合戦将棋型の準備（Start から）：先手・後手の席とカーソル、案内文のキーの読み替え、HUD
    void SetupRealtime()
    {
        // CPU と対戦：人の席はキーボード全体を使う（SeatKeys.KassenSolo）。CPU の席のキーは使わない
        // 討伐任務：プレイヤーは先手の 1 人。キーボード全体を使う（SeatKeys.RaidSolo。錬成のキーが多い）。後手の席はボス（キーは使わない）
        realtimeSeats = new[]
        {
            new InputSeat(PlayerType.Player1, IsRaid ? SeatKeys.RaidSolo("プレイヤー")
                : HasCpu ? (IsCpuSide(PlayerType.Player1) ? SeatKeys.KassenSente : SeatKeys.KassenSolo("先手")) : SeatKeys.KassenSente),
            new InputSeat(PlayerType.Player2, HasCpu ? (IsCpuSide(PlayerType.Player2) ? SeatKeys.KassenGote : SeatKeys.KassenSolo("後手")) : SeatKeys.KassenGote),
        };
        if (HasCpu) focusSide = BoardZones.Opponent(cpuSide.Value); // 操作中の席は人の席
        if (IsRaid) focusSide = PlayerType.Player1;

        // 先手の席はシーンのカーソル、後手の席は複製したカーソル（色を変える）
        sceneCursor.Bind(this, realtimeSeats[0]);
        GameObject copy = Instantiate(sceneCursor.gameObject, sceneCursor.transform.parent);
        copy.name = sceneCursor.gameObject.name + " (後手)";
        GameCursor goteCursor = copy.GetComponent<GameCursor>();
        goteCursor.Bind(this, realtimeSeats[1]);
        TintCursor(sceneCursor, new Color(0.55f, 0.75f, 1f, 1f));  // 先手：青み（陣営オーラと同じ系統の色）
        TintCursor(goteCursor, new Color(1f, 0.55f, 0.6f, 1f));    // 後手：赤み
        sceneCursor.SetPosition(2, 4);
        goteCursor.SetPosition(6, 4);
        if (IsRaid) goteCursor.gameObject.SetActive(false); // 討伐任務：後手の席（ボス）のカーソルは出さない
        seat = realtimeSeats[(int)focusSide];

        // 案内文（「決定: F / 取消: Space」など）を、操作中の席のキーに読み替えて出す
        HandUIManager.StatusFilter = FilterSeatStatus;
        if (HandUIManager.Instance != null) HandUIManager.Instance.SeatMode = true;

        if (FindAnyObjectByType<RealtimeHudView>() == null) gameObject.AddComponent<RealtimeHudView>();
        if (IsRaid)
        {
            Debug.Log($"討伐任務（{Policy.Ruleset.Name}）：{RealtimeKeyHint(realtimeSeats[0].Keys)} で操作する");
        }
        else if (HasCpu)
        {
            InputSeat human = realtimeSeats[(int)focusSide];
            Debug.Log($"{Policy.Ruleset.Name}：あなた（{human.Keys.Name}）は {RealtimeKeyHint(human.Keys)} で操作する。CPU は {CpuLevels.Label(MatchSetup.CpuLevel)}");
        }
        else Debug.Log($"{Policy.Ruleset.Name}：先手 {SeatKeys.Labels(SeatKeys.KassenSente.Up, SeatKeys.KassenSente.Left, SeatKeys.KassenSente.Down, SeatKeys.KassenSente.Right)}・" +
                  $"後手 {SeatKeys.Labels(SeatKeys.KassenGote.Up, SeatKeys.KassenGote.Left, SeatKeys.KassenGote.Down, SeatKeys.KassenGote.Right)} で同時に操作する");
    }

    static void TintCursor(GameCursor cursor, Color color)
    {
        if (cursor == null) return;
        foreach (SpriteRenderer sr in cursor.GetComponentsInChildren<SpriteRenderer>(true))
        {
            sr.color = new Color(sr.color.r * color.r, sr.color.g * color.g, sr.color.b * color.b, sr.color.a);
        }
    }

    // 合戦将棋型の始まり（DecideTurn から）：手番の始まりの処理（ターン数・エナジー +1・詰み・千日手）はない
    void StartRealtime()
    {
        match.Turn.BeginRealtime();
        if (basicMoveFx != null) basicMoveFx.GameStart();
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetTurnInfo(Policy.ProgressLabel);
        }
        foreach (InputSeat s in realtimeSeats)
        {
            UseSeat(s.Owner.Value);
            s.Status = IsRaid
                ? (Policy.IsPlayerSeat(s.Owner.Value)
                    ? $"【討伐任務】 {(BossDefinition != null ? BossDefinition.Name : "ボス")}を倒せ！ 駒をボスのマスへ動かすと攻撃（駒は元のマスへ戻る）。赤く光るマスからは逃げよう（{RealtimeKeyHint(s.Keys)}）"
                    : "【ボス】")
                : IsCpuSide(s.Owner.Value)
                ? $"【CPU】 {CpuLevels.Label(MatchSetup.CpuLevel)}が {Policy.Ruleset.CpuIntervalSec[CpuLevels.Clamp(MatchSetup.CpuLevel)]:0.0} 秒ごとに動く"
                : UsesAllyCpu
                ? $"【{Policy.Ruleset.Name}】 あなたが動かすのは王将だけ。ほかの駒は味方の CPU が動かす（必殺技は使わない）。魔法・錬成は王将（あなた）だけが使える（{RealtimeKeyHint(s.Keys)}）"
                : $"【{Policy.Ruleset.Name}】 手番なし。いつでも自分の駒を動かせる（{RealtimeKeyHint(s.Keys)}）";
        }
        UseSeat(focusSide);
        RefreshFocusUI();
        UpdateActivePlayerSkillUI();
    }

    // 席のキーの短い案内（HUD・開始時の案内文）
    public static string RealtimeKeyHint(SeatKeys k)
    {
        return $"移動 {SeatKeys.Labels(k.Up, k.Left, k.Down, k.Right)}・決定 {SeatKeys.Label(k.Ok)}・取消 {SeatKeys.Label(k.Cancel)}・" +
               $"持ち駒 {SeatKeys.Label(k.Hand)}・必殺技 {SeatKeys.Label(k.Special)}・狙撃 {SeatKeys.Label(k.Snipe)}・" +
               $"魔法 {SeatKeys.Label(k.Skills, 0)}〜{SeatKeys.Label(k.Skills, k.Skills.Length - 1)}・錬成 {SeatKeys.Labels(new[] { k.Forge[0] }, new[] { k.Forge[k.Forge.Length - 1] })}・" +
               $"向き {SeatKeys.Label(k.Orient)}・解除 {SeatKeys.Label(k.Release)}";
    }

    // 案内文の「決定: F」などを、今の席のキーに読み替え、「［先手］」を付ける。
    // 操作中でない席の案内文は、その席に残すだけで画面には出さない（null を返す）
    string FilterSeatStatus(string raw)
    {
        if (!IsRealtime || realtimeSeats == null) return raw;
        InputSeat s = Seat;
        s.Status = raw ?? "";
        if (s != SeatOf(focusSide)) return null;
        if (string.IsNullOrEmpty(raw)) return "";
        return $"［{s.Keys.Name}］ " + LocalizeKeys(raw, s.Keys);
    }

    // 案内文のキーの名前を席のキーに読み替える
    public static string LocalizeKeys(string text, SeatKeys k)
    {
        if (string.IsNullOrEmpty(text) || k == null || k.UsesGameController) return text;
        string ok = SeatKeys.Label(k.Ok), cancel = SeatKeys.Label(k.Cancel);
        return text
            .Replace("決定: F", $"決定: {ok}")
            .Replace("取消: Space", $"取消: {cancel}")
            .Replace("[Space]", $"[{cancel}]")
            .Replace("もう一度 F", $"もう一度 {ok}")
            .Replace("狙撃: G", $"狙撃: {SeatKeys.Label(k.Snipe)}")
            .Replace("発動: E", $"発動: {SeatKeys.Label(k.Special)}")
            .Replace("解除: Q", $"解除: {SeatKeys.Label(k.Release)}")
            .Replace("R で切り替え", $"{SeatKeys.Label(k.Orient)} で切り替え")
            .Replace("成らない: J / ", "")
            .Replace("選択: 矢印キー・WASD", $"選択: {SeatKeys.Labels(k.Up, k.Left, k.Down, k.Right)}")
            .Replace("選択: A・D または W・S", $"選択: {SeatKeys.Labels(k.Left, k.Right)} または {SeatKeys.Labels(k.Up, k.Down)}");
    }

    // ------------------------------------------------------------
    //  一時停止
    // ------------------------------------------------------------

    private bool userPaused = false;
    public bool IsUserPaused => userPaused;

    // 時間と入力を止めているか（一時停止・ルール説明を開いている・ウィンドウが非アクティブ）
    public bool IsRealtimePaused => IsRealtime && (userPaused || RulesOverlayView.IsBlockingInput || !Application.isFocused);

    // 止めている理由（HUD 用。止めていなければ ""）
    public string PauseReason
    {
        get
        {
            if (!IsRealtime) return "";
            if (userPaused) return "一時停止中";
            if (RulesOverlayView.IsBlockingInput) return "ルール説明を開いている間は止まります";
            if (!Application.isFocused) return "ウィンドウが非アクティブの間は止まります";
            return "";
        }
    }

    // シーンを離れる（GameSystem.OnDestroy から）
    void OnDestroyRealtime()
    {
        // 案内文の読み替えはこの対局だけ（次のシーンに持ち越さない）
        if (HandUIManager.StatusFilter == (System.Func<string, string>)FilterSeatStatus) HandUIManager.StatusFilter = null;
    }

    public void SetPaused(bool paused)
    {
        if (!IsRealtime || !IsInTurn) return;
        userPaused = paused;
        Debug.Log(paused ? "一時停止" : "再開");
    }

    public void TogglePause() => SetPaused(!userPaused);

    // ------------------------------------------------------------
    //  CPU などの席の Controller
    // ------------------------------------------------------------

    private readonly IRealtimeSeatController[] seatControllers = new IRealtimeSeatController[2];

    // 席 owner を Controller（CPU・棋譜の再生など）に任せる（null で人間に戻す）。任せた席はキーで操作できない
    public void SetSeatController(PlayerType owner, IRealtimeSeatController controller)
    {
        seatControllers[(int)owner] = controller;
    }

    public IRealtimeSeatController SeatController(PlayerType owner) => seatControllers[(int)owner];

    // controller がどちらかの席に付いているか
    bool IsSeatControllerAttached(IRealtimeSeatController controller)
    {
        return controller != null && (seatControllers[0] == controller || seatControllers[1] == controller);
    }

    // 対局の始め（Start の最後）：合戦将棋型の席の Controller を付ける（棋譜の再生なら両方の席に再生、CPU と対戦なら CPU の席に CPU）
    void SetupRealtimeControllers()
    {
        if (!IsRealtime) return;
        if (replay != null)
        {
            BeginRealtimeReplay();
            return;
        }
        AttachRealtimeCpu();
        AttachBossSeat(); // 討伐任務：後手の席はボス（キーで操作できない。GameSystem.Boss.cs）
    }

    // 局面を読み込む前（GameSystem.State.cs の ImportState から）：両方の席の選択とキューをやめる
    void ResetRealtimeForImport()
    {
        realtimeQueue.Clear();
        realtimeAccMs = 0f;
        if (realtimeSeats == null) return;
        foreach (InputSeat s in realtimeSeats) ResetSeatSelection(s.Owner.Value);
        UseSeat(focusSide);
    }

    // ------------------------------------------------------------
    //  毎フレーム：入力 → 時間を進める → 選択の表示
    // ------------------------------------------------------------

    private struct QueuedAction
    {
        public MatchAction Action;
        public PlayerType Owner;
        public IRealtimeSeatController From;
    }
    private readonly List<QueuedAction> realtimeQueue = new List<QueuedAction>();
    private float realtimeAccMs = 0f;

    // 時間が 1 tick 進んだ（HUD の経過時間の表示など）
    public event System.Action RealtimeTicked;

    void HandleRealtimeUpdate()
    {
        Keyboard keyboard = Keyboard.current;

        // 一時停止（Esc。ルール説明を閉じた Esc は数えない）
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !RulesOverlayView.IsBlockingInput)
        {
            TogglePause();
        }

        // 両方の席の入力（キーを押した席を操作中にする。対象選択中の席は、範囲の表示を更新するため毎フレーム見る）
        if (keyboard != null && !IsRealtimePaused)
        {
            foreach (InputSeat s in realtimeSeats)
            {
                if (!IsInTurn) break;
                if (!CanSeatOperate(s)) continue;
                bool pressed = s.Keys.AnyPressed();
                if (!pressed && s.TargetSelection == null) continue;
                if (pressed) FocusSeat(s.Owner.Value);
                else UseSeat(s.Owner.Value);
                HandleTurnUpdate();
                selectionsDirty = true;
            }
            if (IsInTurn) UseSeat(focusSide);
        }

        if (!IsInTurn) return;
        // 棋譜の早送り（Prototype の RealtimeMatch の speed）：1 フレームの上限（MaxFrameMs）をかけてから倍にする
        AdvanceRealtime(Time.unscaledDeltaTime * 1000f, replay != null ? replay.Speed : 1f);
        if (IsInTurn && selectionsDirty) RenderSeatSelections();
    }

    // 実時間 ms ミリ秒ぶん時間を進める（止めている間は進めない。溜まった時間を 1 tick ずつ StepRealtime に分ける）
    void AdvanceRealtime(float ms, float speed = 1f)
    {
        if (IsRealtimePaused) return;
        if (KifuMenuView.IsBlockingInput) return; // 棋譜メニューを開いている間も止める（再生も止まる）
        realtimeAccMs += Mathf.Min(ms, MatchConfig.MaxFrameMs) * Mathf.Max(1f, speed);
        int n = 0;
        while (realtimeAccMs >= MatchConfig.TickMs && IsInTurn)
        {
            realtimeAccMs -= MatchConfig.TickMs;
            StepRealtime();
            if (++n > 400)
            {
                realtimeAccMs = 0f; // 1 フレームに進めすぎない
                break;
            }
        }
    }

    // 行動を次の tick の頭で実行するように積む（Dispatch から。判定は済んでいる）
    void EnqueueRealtime(MatchAction action, PlayerType owner, IRealtimeSeatController from = null)
    {
        MatchAction a = action.Clone();
        a.Owner = owner;
        realtimeQueue.Add(new QueuedAction { Action = a, Owner = owner, From = from });
    }

    // 1 tick 進める
    void StepRealtime()
    {
        boardChangedThisStep = false;

        // 1. 席の Controller（CPU・棋譜の再生など）の行動。両方の席に同じ Controller を付けたときは 1 回だけ聞く
        //    （行動に owner があればその席、なければ聞いた席の行動にする）
        for (int i = 0; i < seatControllers.Length; i++)
        {
            IRealtimeSeatController ctrl = seatControllers[i];
            if (ctrl == null || (i > 0 && seatControllers[0] == ctrl)) continue;
            IEnumerable<MatchAction> actions = ctrl.RealtimeActions(this, (PlayerType)i);
            if (actions == null) continue;
            foreach (MatchAction a in actions)
            {
                if (a == null) continue;
                PlayerType owner = a.Owner ?? (PlayerType)i;
                if (seatControllers[(int)owner] != ctrl) continue; // その席を任されていない
                EnqueueRealtime(a, owner, ctrl);
            }
        }

        // 1'. 味方の CPU（王将だけ操作：人の席の王将以外の駒。GameSystem.Cpu.cs）。席が Controller に任されている間は動かない。
        //     停止世界の間は（どちらの席の味方も）止まる：考えている途中をやめ、時間が動き出してから間隔を数え直す
        for (int i = 0; i < allyCpus.Length; i++)
        {
            RealtimeCpuPacer ally = allyCpus[i];
            if (ally == null || seatControllers[i] != null) continue;
            if (timeStop != null)
            {
                ally.Reset();
                continue;
            }
            IEnumerable<MatchAction> actions = ally.RealtimeActions(this, (PlayerType)i);
            if (actions == null) continue;
            foreach (MatchAction a in actions)
            {
                if (a != null) EnqueueRealtime(a, (PlayerType)i, ally);
            }
        }

        // 2. キューの行動を積んだ順に実行する
        if (realtimeQueue.Count > 0)
        {
            List<QueuedAction> queue = new List<QueuedAction>(realtimeQueue);
            realtimeQueue.Clear();
            foreach (QueuedAction q in queue)
            {
                if (!IsInTurn) break;
                if (q.From != null && !IsSeatControllerAttached(q.From) && !IsActiveAllyCpu(q.From)) continue; // 外された Controller（終わった再生など）の行動
                RunRealtimeAction(q);
            }
        }
        if (!IsInTurn)
        {
            EndRealtimeReplayAtGameOver();
            return;
        }

        // 3. 時間を進める
        RealtimeTick();
        if (!IsInTurn)
        {
            EndRealtimeReplayAtGameOver();
            return;
        }

        if (boardChangedThisStep)
        {
            RevalidateSeats();
            RefreshFocusUI();
        }
        RealtimeTicked?.Invoke();
    }

    // キューの行動を 1 つ実行する（行動したプレイヤーの席に切り替えて、判定 → 実行 → 行動のあとの処理）
    void RunRealtimeAction(QueuedAction q)
    {
        PlayerType prevFocus = focusSide;
        UseSeat(q.Owner);
        bool fromReplay = q.From is RealtimeReplayController;
        MatchAction executed = null;
        if (fromReplay) replayDispatching = true; // 再生中でも、再生が出した行動は通す
        try
        {
            ActionPlan plan = PlanAction(q.Action, out string reason);
            if (plan != null && IsActiveAllyCpu(q.From) && !IsAllyAction(q.Action))
            {
                // 考えている間に盤面が変わり、その駒が王将になった（味方の CPU は王将・魔法・錬成を使わない）
                plan = null;
                reason = "味方の CPU が動かせるのは王将以外の駒だけです";
            }
            if (plan == null)
            {
                Debug.Log($"[Action] 実行できなくなった行動：{q.Action}（{reason}）");
                if (q.From != null) q.From.OnRejected(this, q.Action, reason);
                return;
            }
            TimeStopState stopped = timeStop;
            ExecutePlan(plan);
            AfterRealtimeAction(plan, stopped);
            boardChangedThisStep = true;

            // ハッシュはこの行動の処理が終わった直後（次の行動・次の tick の前）に取る（Prototype の RealtimeMatch.play の entry.hash）
            executed = hashPendingAction;
            FlushScheduledFx();
            FinishActionHash();
            FinalizeKifuHash();
            UpdateSimulationCheckNow(); // エディタ：シミュレーションの予測と比べる（次の tick で時間が進む前に）
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            replayDispatching = false;
            // 行動を Controller に知らせる（棋譜の再生はハッシュを照らし合わせる。Prototype の onAction）
            if (executed != null)
            {
                for (int i = 0; i < seatControllers.Length; i++)
                {
                    IRealtimeSeatController ctrl = seatControllers[i];
                    if (ctrl == null || (i > 0 && seatControllers[0] == ctrl)) continue;
                    try
                    {
                        ctrl.OnAction(this, executed);
                    }
                    catch (System.Exception e)
                    {
                        Debug.LogException(e);
                    }
                }
            }
            if (IsInTurn)
            {
                UseSeat(prevFocus);
                RefreshFocusUI();
            }
        }
    }

    // 行動のあと（Prototype の afterRealtimeAction）：手数 → 連打の間隔 → 停止世界の回数（錬成は数えない）→ 勝敗 → 超常魔法の解放の判定。
    // 手番はないので交代しない。ファランクス・アンデッドの自動行動・世界の終わりは時間で進む（RealtimeTick）
    void AfterRealtimeAction(ActionPlan plan, TimeStopState stopped)
    {
        match.Turn.AdvanceMove();
        int cd = Policy.PieceCooldownTicks;
        if (cd > 0 && plan.Piece != null && IsOnBoard(plan.Piece)) plan.Piece.cooldownUntilTick = match.Clock.Tick + cd;

        TimeStopState ts = timeStop;
        if (ts != null)
        {
            if (ts.JustStarted || ts != stopped)
            {
                // 停止世界を発動した行動（数えない）。相手の席の選択はやめさせる（相手は動けない）
                ts.JustStarted = false;
                ResetSeatSelection(BoardZones.Opponent(ts.Owner), "【時間停止中】 相手が時間を止めた。時間が動き出すまで操作できない", true);
                TimeStopUpdated?.Invoke(ts, false);
                ShowSeatNotice(ts.Owner, TimeStopStatusText);
            }
            else if (!ts.Ending && plan.Action.type != MatchActionType.Forge && Policy.TimeStopLimitsMoves)
            {
                // 討伐任務は行動回数の上限がない（TimeStopSec 秒だけ止まる）
                ts.Remaining--;
                if (ts.Remaining <= 0) ts.Ending = true;
                TimeStopUpdated?.Invoke(ts, true);
                if (!ts.Ending) ShowSeatNotice(ts.Owner, TimeStopStatusText);
            }
            if (ts.Ending && timeStop == ts) ResolveTimeStopAndReset();
        }
        if (ResolveKingLoss()) return;
        // 討伐任務：盤上の駒の移動・必殺技（ボスへの攻撃を含む）ごとに +2 エナジー（打つ・魔法・錬成・狙撃は対象外。時間停止中も入る）
        int moveEnergy = Policy.MoveEnergy;
        if (moveEnergy > 0 && (plan.Action.type == MatchActionType.Move || plan.Action.type == MatchActionType.Special))
        {
            match.Energy.Gain(plan.Owner, moveEnergy, false);
            UpdateActivePlayerSkillUI();
            if (ResolveKingLoss()) return;
        }
        CheckTranscendentUnlock();
        UpdateActivePlayerSkillUI();
    }

    // 時間停止を解除して予定をまとめて処理し、両方の席の選択をやめる（盤面が大きく変わるので）
    void ResolveTimeStopAndReset()
    {
        ResolveTimeStopNow();
        foreach (InputSeat s in realtimeSeats) ResetSeatSelection(s.Owner.Value, "【時は動き出す】 止まっていた間の効果が起こった", true);
        boardChangedThisStep = true;
    }

    // 時間を 1 tick 進める（Prototype の src/core/clock.js の tick）
    //  1. Tick++。停止世界の間は止まっている時間だけ数え、TimeStopSec 秒で解除して予定を処理する（対局の時間は進めない。2〜6 も行わない）
    //  2. Time++
    //  3. 毎秒：エナジー +EnergyPerSec（両者）・経過時間の表示
    //  4. 敵陣への打ち込みの解禁（DropLockSec 秒）・期限切れの炎上の鎮火
    //  5. 世界の終わり（WorldEndIntervalSec 秒ごと）・ファランクスとアンデッドの自動行動（AutoActSec 秒ごと。先手 → 後手）
    //  6. 勝敗 → 超常魔法の解放の判定（UnlockCheckSec 秒ごと）
    void RealtimeTick()
    {
        MatchClock c = match.Clock;
        c.Tick++;

        TimeStopState ts = timeStop;
        if (ts != null)
        {
            ts.Elapsed++;
            if (ts.Elapsed >= Policy.TimeStopTicks)
            {
                ts.Ending = true;
                Debug.Log($"{TurnEndAction.SideName(ts.Owner)}：時間停止から {Policy.Ruleset.Durations.TimeStopSec:0} 秒たった（残り {ts.Remaining} 回）");
                ResolveTimeStopAndReset();
                ResolveKingLoss();
            }
            return;
        }

        c.Time++;
        if (Policy.IsEverySecond)
        {
            // 時間でエナジーが増えるのはプレイヤーの席だけ（討伐任務の後手の席はボス）
            int e = Policy.EnergyPerSec;
            if (Policy.GainsEnergy(PlayerType.Player1)) AddPoint(PlayerType.Player1, e, true);
            if (Policy.GainsEnergy(PlayerType.Player2)) AddPoint(PlayerType.Player2, e, true);
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetTurnInfo(Policy.ProgressLabel);
        }

        if (Policy.IsDropUnlockMoment)
        {
            string notice = $"{Policy.DropLockLabel.Replace("まで", "")}たった：敵陣への持ち駒の打ち込みが解禁された！";
            Debug.Log(notice);
            if (basicMoveFx != null) basicMoveFx.DropUnlocked();
            ShowSeatNotice(PlayerType.Player1, notice);
            ShowSeatNotice(PlayerType.Player2, notice);
        }

        if (Cells != null && Cells.ExtinguishExpired(Policy.Now).Count > 0) boardChangedThisStep = true;

        if (Policy.IsEveryWorldEnd)
        {
            int n = match.WorldEndCellsThisRound;
            if (n > 0 && WorldEndCandidates().Count > 0)
            {
                RunRealtimeAuto(PlayerType.Player1, ctx => WorldEndEffect.Collapse(this, n, ctx.Timeline, ctx.Fx));
                if (ResolveKingLoss()) return;
            }
        }

        if (Policy.IsEveryAutoAct)
        {
            foreach (PlayerType side in BothSides)
            {
                foreach (TurnEndAction action in turnEndActions)
                {
                    if (!action.HasWork(this, side)) continue;
                    RunRealtimeAuto(side, action.Run);
                    if (ResolveKingLoss()) return; // 自動行動で王将を取った・巻き込んだ
                }
            }
        }

        // 討伐任務：手下 → ボスの AI（Prototype のモードの onTick。GameSystem.Boss.cs）→ 勝敗
        if (IsRaid)
        {
            TickBoss();
            if (ResolveKingLoss()) return;
        }

        if (Policy.IsEveryUnlockCheck) CheckTranscendentUnlock();
    }

    // 自動行動 1 つ（ファランクス・アンデッド・世界の終わりの崩壊）をその場で行う。小さなカットインは出さず、演出は待たない
    void RunRealtimeAuto(PlayerType side, System.Action<TurnEndContext> run)
    {
        SpecialContext timeline = new SpecialContext(this, gameBoard, side, specialFx);
        pendingActionFxTime = 0f;
        ActiveSpecial = timeline;
        try
        {
            run(new TurnEndContext(this, gameBoard, side, timeline, magicFx));
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            ActiveSpecial = null;
        }
        RunCounters(timeline); // 自動行動は 1 体ごとに反撃まで済ませている。残っていれば念のため
        pendingActionFxTime = 0f;
        boardChangedThisStep = true;
        UpdateActivePlayerSkillUI();
    }

    // ------------------------------------------------------------
    //  演出を待たない実行（必殺技・魔法）
    // ------------------------------------------------------------

    // 必殺技（Prototype の play.js の special。合戦将棋型はカットインを出さない）：構え → 効果 → 移動回数を 0 に → 成り
    void ExecuteSpecialNow(GamePiece caster, SpecialMove move, Vector2Int? target)
    {
        if (!IsOnBoard(caster)) return;
        string pieceName = HandUIManager.GetJapaneseName(caster.type);
        Debug.Log($"{caster.player} の{pieceName}が必殺技「{move.moveName}」を発動！");
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【必殺技】 {pieceName}の「{move.moveName}」！");
        if (specialFx != null)
        {
            specialFx.PlayCast(caster, move);
            specialFx.PopName(caster, move);
        }

        SpecialContext context = new SpecialContext(this, gameBoard, caster, specialFx) { Target = target };
        int fromX = caster.X;
        pendingActionFxTime = 0f;
        ActiveSpecial = context;
        BeginBossEffect(BossHit.OfSpecial(caster.type)); // 討伐任務：この技がボスに当たるのは 1 回だけ（技のダメージ）
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
            EndBossEffect();
        }

        if (IsOnBoard(caster))
        {
            caster.moveCount = 0;
            RefreshSpecialReady(caster);
            if (CanPromote(caster, fromX, caster.X))
            {
                if (timeStop != null)
                {
                    DeferPromote(caster);
                }
                else
                {
                    caster.isPromoted = true;
                    context.Schedule(caster.UpdateVisual);
                }
            }
        }
        RunCounters(context); // 技の取りがカウンターに無効にされたら反撃される
        pendingActionFxTime = 0f;
        UpdateActivePlayerSkillUI();
    }

    // 魔法（Prototype の play.js の castMagic。合戦将棋型はカットインを出さない）：エナジー消費 → 魔法陣 → 効果
    void ExecuteSkillNow(SkillDefinition skill, SkillContext context)
    {
        PlayerType player = context.Player;
        Player targetPlayer = PlayerOf(player);
        string rankLabel = SkillRanks.Label(skill.rank);
        Vector2Int? focus = context.Targets.Count > 0 ? context.Targets[0] : (Vector2Int?)null;

        ResetSelectionState();
        targetPlayer.ConsumePoint(skill.cost);
        if (skill.rank == SkillRank.Transcendent) match.MarkTranscendentUsed(player);
        Debug.Log($"プレイヤー {(player == PlayerType.Player1 ? "1" : "2")} が{rankLabel}「{skill.skillName}」を発動！（エナジー -{skill.cost}）");
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【{rankLabel}】 {skill.skillName}！");
        if (magicFx != null) magicFx.PlayCast(skill, focus);

        context.Timeline = new SpecialContext(this, gameBoard, player, specialFx);
        context.Fx = magicFx;
        pendingActionFxTime = 0f;
        ActiveSpecial = context.Timeline;
        BeginBossEffect(BossHit.OfMagic(skill.Id)); // 討伐任務：この魔法がボスに当たるのは 1 回だけ（魔法ごとのダメージ）
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
            EndBossEffect();
        }
        RunCounters(context.Timeline); // 剣などの取りがカウンターに無効にされたら反撃される
        pendingActionFxTime = 0f;
        UpdateActivePlayerSkillUI();
    }

    // ------------------------------------------------------------
    //  席の選択の確かめ直しと表示
    // ------------------------------------------------------------

    // 盤面が変わったあと：席の選択が古くなっていたら（選んだ駒が取られた・相手の時間停止が始まった など）やめ、
    // 移動先・打てるマスを計算し直す
    void RevalidateSeats()
    {
        if (realtimeSeats == null) return;
        foreach (InputSeat s in realtimeSeats)
        {
            PlayerType owner = s.Owner.Value;
            bool stale = false;
            if (s.SelectedPiece != null && (!IsOnBoard(s.SelectedPiece) || s.SelectedPiece.player != owner)) stale = true;
            if (s.IsSelectingSnipeTarget && (s.SnipingPiece == null || !IsOnBoard(s.SnipingPiece))) stale = true;
            if (s.TargetSelection is TargetSelection sel && sel.Caster != null && !IsOnBoard(sel.Caster)) stale = true;
            if (s.IsWaitingForPromoteChoice) stale = stale || gameBoard.GetPieceAt(s.PendingFromX, s.PendingFromY) == null;
            if (timeStop != null && timeStop.Owner != owner && s.IsBusySelecting) stale = true;
            if (s.IsDroppingHand && (!PlayerOf(owner).Hand.TryGetValue(s.SelectedHandType, out int left) || left <= 0)) stale = true;

            if (stale)
            {
                ResetSeatSelection(owner, "【選択の取り消し】 盤面が変わったので、選んでいたものを取り消した");
                continue;
            }
            RefreshSeatTargets(s);
        }
        UseSeat(focusSide);
        selectionsDirty = true;
    }

    // 席の移動先・打てるマス・狙撃の対象を、今の盤面で計算し直す（カーソルは動かさない）
    void RefreshSeatTargets(InputSeat s)
    {
        PlayerType owner = s.Owner.Value;
        UseSeat(owner);
        if (s.IsSelectingSnipeTarget)
        {
            s.SnipeTargets.Clear();
            s.SnipeTargets.AddRange(GetSnipeTargets(s.SnipingPiece));
            s.MovablePositions.Clear();
            s.MovablePositions.AddRange(s.SnipeTargets);
        }
        else if (s.IsDroppingHand)
        {
            s.MovablePositions.Clear();
            for (int tx = 0; tx < 9; tx++)
            {
                for (int ty = 0; ty < 9; ty++)
                {
                    if (CanDropPieceTo(owner, s.SelectedHandType, tx, ty)) s.MovablePositions.Add(new Vector2Int(tx, ty));
                }
            }
        }
        else if (s.SelectedPiece != null && s.TargetSelection == null && !s.IsWaitingForPromoteChoice)
        {
            ClearBoardHighlight();
            ShowMovablePanels(s.SelectedPiece);
        }
        else if (s.TargetSelection != null)
        {
            s.LastPreviewCursor = new Vector2Int(-1, -1); // 範囲の表示を作り直す
        }
    }

    // 席 owner の選択をすべてやめる（対象選択・成りの確認・駒・持ち駒・狙撃）。選んでいる途中だったら、その席の案内文を notice にする
    // （always なら選んでいなくても案内文を notice にする）
    void ResetSeatSelection(PlayerType owner, string notice = null, bool always = false)
    {
        if (realtimeSeats == null) return;
        PlayerType prev = Seat.Owner ?? focusSide;
        UseSeat(owner);
        InputSeat s = Seat;
        if (s.TargetSelection != null) EndTargetSelection();
        if (s.IsWaitingForPromoteChoice)
        {
            s.IsWaitingForPromoteChoice = false;
            if (promoteConfirmPanel != null && owner == focusSide) promoteConfirmPanel.SetActive(false);
        }
        bool wasSelecting = s.IsBusySelecting;
        ClearBoardHighlight();
        selectedPiece = null;
        isSelectingHand = false;
        isDroppingHand = false;
        isSelectingSnipeTarget = false;
        snipingPiece = null;
        snipeTargets.Clear();
        currentMovablePositions.Clear();
        if (owner == focusSide && HandUIManager.Instance != null) HandUIManager.Instance.HidePanel();
        if (wasSelecting || (always && notice != null)) s.Status = notice ?? "";
        UseSeat(prev);
        selectionsDirty = true;
    }

    // 両方の席の選択の表示を重ねて描く（光らせるマス・駒）。席の処理の中で片方が消した分も描き直す
    void RenderSeatSelections()
    {
        selectionsDirty = false;
        if (realtimeSeats == null || gameBoard == null) return;
        gameBoard.AllDeactivePanel();
        foreach (InputSeat s in realtimeSeats)
        {
            if (s.TargetSelection != null)
            {
                foreach (Vector2Int pos in s.PreviewPanels) gameBoard.ActivePanel(GameHelper.CalcPanelNum(pos.x, pos.y));
            }
            else
            {
                if (s.SelectedPiece != null && IsOnBoard(s.SelectedPiece))
                {
                    gameBoard.ActivePanel(GameHelper.CalcPanelNum(s.SelectedPiece.X, s.SelectedPiece.Y));
                }
                if (s.SelectedPiece != null || s.IsDroppingHand || s.IsSelectingSnipeTarget)
                {
                    foreach (Vector2Int pos in s.MovablePositions) gameBoard.ActivePanel(GameHelper.CalcPanelNum(pos.x, pos.y));
                }
            }
            foreach (GamePiece p in s.HighlightedPieces)
            {
                if (p != null) p.SetHighlight(true);
            }
        }
    }
}

// 合戦将棋型の席を任せる相手（CPU・棋譜の再生など。Prototype の Controller の realtimeActions・onAction・onRejected）。
// GameSystem.SetSeatController で席に付けると、毎 tick の頭に RealtimeActions が呼ばれ、返した行動をその席の行動として実行する。
// 両方の席に同じ Controller を付けたときは 1 回だけ呼ばれる（行動に owner があればその席、なければ聞いた席の行動）
public interface IRealtimeSeatController
{
    // この tick に入れる行動（なければ空・null）。owner は付けなくてよい（席の持ち主になる）
    IEnumerable<MatchAction> RealtimeActions(GameSystem system, PlayerType seat);

    // 返した行動が実行できなかった（その tick に判定し直して、実行できなくなっていた。reason は理由）
    void OnRejected(GameSystem system, MatchAction action, string reason);

    // 行動が 1 つ実行された（どの席の行動でも。entry は ActionHistory の記録：owner・t・その行動のあとの盤面のハッシュ hash つき）
    void OnAction(GameSystem system, MatchAction entry);
}

// 棋譜の再生（Prototype の src/controller/RealtimeReplayController.js）：記録した行動（owner と t つき）を、記録した tick に、記録した順に入れる。
// 同じシード（MatchSetup.Seed・棋譜の seed）で始めた対局の両方の席に付けると、同じ対局になる。
// 各行動のあと、盤面のハッシュを記録と照らし合わせ、ずれたら・実行できない行動があったら・記録した tick を過ぎていたら、そこで終える
// （終わったら onEnd。GameSystem はそこで席を人・CPU に戻すので、その局面から続けて指せる）
public class RealtimeReplayController : IRealtimeSeatController
{
    // 再生の結果（Prototype の onEnd の { finished, played, total, error }）
    public struct Result
    {
        public bool Finished;  // 最後まで再生した・ずれた・再現できなかった（false：途中でやめた）
        public int Played;
        public int Total;
        public string Error;   // ずれた・再現できなかった理由（一致したら null）
    }

    private readonly List<MatchAction> actions;
    private readonly System.Action<Result> onEnd;
    private readonly System.Action<int> onProgress;
    private int next;    // 次に入れる行動
    private int played;  // 実行された行動

    public int Played => played;
    public int Total => actions.Count;
    public bool Ended { get; private set; }

    // recorded：記録した行動（ActionHistory・棋譜の actions。owner と t があるもの。hash があれば照らし合わせる）
    public RealtimeReplayController(IEnumerable<MatchAction> recorded, System.Action<Result> onEnd = null, System.Action<int> onProgress = null)
    {
        actions = new List<MatchAction>();
        foreach (MatchAction a in recorded)
        {
            if (a != null && a.t >= 0 && a.Owner.HasValue) actions.Add(a.Clone());
        }
        // tick の順に並べる（同じ tick の行動は記録した順のまま）
        actions.Sort(new StableByTick(actions));
        this.onEnd = onEnd;
        this.onProgress = onProgress;
    }

    // 再生し終わったか（全部の行動を入れた）
    public bool IsFinished => next >= actions.Count;

    public IEnumerable<MatchAction> RealtimeActions(GameSystem system, PlayerType seat)
    {
        List<MatchAction> res = new List<MatchAction>();
        MatchClock clock = system.Match != null ? system.Match.Clock : null;
        if (Ended || clock == null) return res;
        while (next < actions.Count && actions[next].t <= clock.Tick)
        {
            if (actions[next].t < clock.Tick)
            {
                End(true, $"{next + 1} 手目の時刻（tick {actions[next].t}）を過ぎています");
                return new List<MatchAction>();
            }
            MatchAction a = actions[next++].Clone();
            a.hash = "";
            res.Add(a);
        }
        return res;
    }

    public void OnAction(GameSystem system, MatchAction entry)
    {
        if (Ended || played >= actions.Count) return;
        MatchAction recorded = actions[played];
        played++;
        onProgress?.Invoke(played);
        if (!string.IsNullOrEmpty(recorded.hash) && entry.hash != recorded.hash)
        {
            Debug.LogWarning($"[Replay] {played} 手目のあとの盤面のハッシュが違う：棋譜 {recorded.hash} ／ 再生 {entry.hash}（{entry}）");
            End(true, $"{played} 手目のあとの盤面が棋譜と一致しません");
        }
        else if (played >= actions.Count) End(true, null);
    }

    public void OnRejected(GameSystem system, MatchAction action, string reason)
    {
        Debug.LogWarning($"[Replay] 記録した行動が実行できなかった（棋譜と対局がずれた）：t={action.t} {action}（{reason}）");
        End(true, $"{played + 1} 手目の行動（{action}）を再現できない：{reason}");
    }

    // 対局をやめた（「再生を止める」など）
    public void Cancel()
    {
        End(false, null);
    }

    void End(bool finished, string error)
    {
        if (Ended) return;
        Ended = true;
        onEnd?.Invoke(new Result { Finished = finished, Played = played, Total = actions.Count, Error = error });
    }

    class StableByTick : IComparer<MatchAction>
    {
        private readonly Dictionary<MatchAction, int> index = new Dictionary<MatchAction, int>();
        public StableByTick(List<MatchAction> list)
        {
            for (int i = 0; i < list.Count; i++) index[list[i]] = i;
        }
        public int Compare(MatchAction x, MatchAction y)
        {
            int c = x.t.CompareTo(y.t);
            return c != 0 ? c : index[x].CompareTo(index[y]);
        }
    }
}

// CPU の考える部分（RealtimeCpuPacer が使う。CpuPlayer が実装する）。考えている間も時間は進むので、考え始めと結果の受け取りを分ける
public interface IRealtimeCpuBrain
{
    // seat の席の次の一手を考え始める（今の局面を写して、別スレッド・フレームごとに考える）。指せる手がなければ false
    bool BeginThinking(GameSystem system, PlayerType seat);

    // 考え終わったか。終わったら result に行動（指さないなら null）
    bool PollThinking(GameSystem system, out MatchAction result);

    // 考えるのをやめる（結果は捨てる）
    void CancelThinking();
}

// CPU を一定の間隔で動かす Controller（Prototype の CpuController.realtimeActions。間隔は Lv0 2.0 秒／Lv1 1.5 秒／Lv2 1.2 秒）。
//  ・前の行動を決めてから CpuIntervalTicks(level) tick たったら、その席を手番にした写しの局面で考え始める（IRealtimeCpuBrain。考えている間も時間は進む）
//  ・考え終わったら、その tick に行動を返す（そのときの局面で判定し、できなければ捨てる）。次に考え始めるのは、考え終わった時刻から数える
//  ・同期で選ぶ関数（choose）を渡してもよい（テスト・簡単な CPU 用。その tick にすぐ返す）
public class RealtimeCpuPacer : IRealtimeSeatController
{
    private readonly int level;
    private readonly System.Func<GameSystem, PlayerType, MatchAction> choose;
    private readonly IRealtimeCpuBrain brain;
    private int nextTick = -1;
    private int lastTick = -1;
    private bool thinking;

    public int Level => level;
    public bool IsThinking => thinking;

    public RealtimeCpuPacer(int level, System.Func<GameSystem, PlayerType, MatchAction> choose)
    {
        this.level = level;
        this.choose = choose;
    }

    public RealtimeCpuPacer(int level, IRealtimeCpuBrain brain)
    {
        this.level = level;
        this.brain = brain;
    }

    public IEnumerable<MatchAction> RealtimeActions(GameSystem system, PlayerType seat)
    {
        List<MatchAction> res = new List<MatchAction>();
        MatchClock clock = system.Match != null ? system.Match.Clock : null;
        if (clock == null || (choose == null && brain == null)) return res;
        int interval = system.Policy.CpuIntervalTicks(level);
        if (clock.Tick < lastTick) Reset(); // 局面を読み込んで時間が戻った
        lastTick = clock.Tick;
        if (nextTick < 0) nextTick = clock.Tick + interval;

        if (thinking)
        {
            if (!brain.PollThinking(system, out MatchAction result)) return res;
            thinking = false;
            nextTick = clock.Tick + interval; // 考え終わった時刻から数える（Prototype と同じ）
            if (result != null)
            {
                result.Owner = seat;
                res.Add(result);
            }
            return res;
        }
        if (clock.Tick < nextTick) return res;

        if (choose != null)
        {
            nextTick = clock.Tick + interval;
            MatchAction a = choose(system, seat);
            if (a != null)
            {
                a.Owner = seat;
                res.Add(a);
            }
            return res;
        }
        thinking = brain.BeginThinking(system, seat);
        if (!thinking) nextTick = clock.Tick + 1; // 指せる手がない：次の tick にまた見る
        return res;
    }

    // 考えている途中をやめて、間隔を数え直す
    public void Reset()
    {
        if (thinking && brain != null) brain.CancelThinking();
        thinking = false;
        nextTick = -1;
    }

    public void OnRejected(GameSystem system, MatchAction action, string reason)
    {
    }

    public void OnAction(GameSystem system, MatchAction entry)
    {
    }
}
