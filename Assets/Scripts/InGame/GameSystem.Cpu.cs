using UnityEngine;

// CPU と対戦（Prototype の src/controller/CpuController.js と Match の Controller）。GameSystem の続き（partial）。
//
//  ・対戦モードの画面（ModeSelectView）で「CPU と対戦」を選ぶと MatchSetup に入り、対局の開始時（Start）に CpuPlayer を足す
//  ・CPU の手番は、人の入力（盤のクリック・キー・ボタン）を受け付けない（HandleTurnUpdate・CanActNow・UseSkill・Dispatch）。
//    CPU は CpuPlayer が DispatchFromCpu で行動を実行する（判定は人と同じ CanDispatch）
//  ・カメラは人の側に固定する（手番ごとに盤を回さない）
//  ・オンライン対戦では使わない
//  ・合戦将棋型（手番がない）：CPU の席に RealtimeCpuPacer（GameSystem.Realtime.cs）を付ける（SetSeatController）。
//    前の行動を決めてから一定の間隔（Lv0 2.0 秒／Lv1 1.5 秒／Lv2 1.2 秒。BattleRuleset.CpuIntervalSec）たったら、CpuPlayer が
//    その席を手番にした写しの局面で考え（IRealtimeCpuBrain）、考え終わった tick に行動を入れる（そのときの局面で判定し、できなければ捨てる）。
//    人はキーボード全体で自分の席を操作する（SeatKeys.KassenSolo）。盤は回さない（先手の側から見る）
//  ・合戦将棋型の基本対戦（MatchPolicy.PlayersControlKingOnly）：人が動かせるのは王将だけ。人の席には、王将以外の駒の
//    移動・必殺技・狙撃と持ち駒を打つのを受け持つ味方の CPU（RealtimeCpuPacer ＋ CpuPlayer）を付ける（AttachAllyCpus）。
//    2人で対戦なら両方の席、CPU と対戦なら人の席だけ（CPU の席は今までどおり CPU が全部の駒を動かす）。
//    魔法・錬成・停止世界の解除は人が行う。人の入力から王将以外の駒の行動は出せない（Dispatch・IsHumanAllowedAction）。
//    席が Controller（CPU・棋譜の再生）に任されている間は、味方の CPU は動かない（棋譜には味方の CPU の行動も席の行動として残るので、再生で再現される）
public partial class GameSystem
{
    private PlayerType? cpuSide;   // CPU が指す側（CPU と対戦でなければ null）
    private bool dispatchingCpu;   // DispatchFromCpu の中（CPU の手番でも Dispatch を通す）

    public CpuPlayer Cpu { get; private set; }

    // 合戦将棋型の CPU の席の Controller（CPU と対戦の合戦将棋型でなければ null）
    private RealtimeCpuPacer realtimeCpu;

    public bool HasCpu => cpuSide.HasValue;

    // side を CPU が指すか
    public bool IsCpuSide(PlayerType side) => cpuSide.HasValue && cpuSide.Value == side;

    // 今が CPU の手番か（合戦将棋型は、CPU の席の行動を処理している間）
    public bool IsCpuTurn => IsInTurn && cpuSide.HasValue && CurrentSide == cpuSide.Value;

    // CPU が考え始めてよいか（本将棋型：CPU の手番で、演出・自動行動・選択の途中でない。合戦将棋型は RealtimeCpuPacer が決める）
    public bool CanCpuAct => !IsRealtime && IsCpuTurn && !IsReplaying && !IsBusy && !isRunningTurnEnd && !isWaitingForPromoteChoice
        && !IsSelectingSkillTarget && !isSelectingSnipeTarget && match != null && gameBoard != null;

    // 今の手番の手数（CPU が考えている間に局面が変わっていないかを確かめる）
    public int CurrentMoveNumber => match != null ? match.Turn.MoveNumber : 0;

    // CPU の行動を実行する（判定は人と同じ。実行できなければ false）
    public bool DispatchFromCpu(MatchAction action)
    {
        if (!IsCpuTurn) return false;
        dispatchingCpu = true;
        try
        {
            return Dispatch(action);
        }
        finally
        {
            dispatchingCpu = false;
        }
    }

    // turn の手番のときにカメラを向ける側（CPU と対戦なら、いつも人の側）
    // （合戦将棋型は盤を回さない：いつも先手の側から見る）
    PlayerType CameraSideFor(PlayerType turn)
    {
        if (IsRealtime) return PlayerType.Player1;
        return cpuSide.HasValue ? BoardZones.Opponent(cpuSide.Value) : turn;
    }

    // 対局の開始時：CPU と対戦なら CpuPlayer を足す
    void SetupCpu()
    {
        if (!MatchSetup.IsVsCpu) return;
        if (IsRaid) return; // 討伐任務（棋譜の再生など）：CPU は使わない（後手の席はボス）
        if (IsOnlineMatch)
        {
            Debug.LogWarning("オンライン対戦では CPU と対戦できないので、2人で対戦として始めます");
            return;
        }
        cpuSide = MatchSetup.CpuSide;
        Cpu = gameObject.AddComponent<CpuPlayer>();
        Cpu.Init(this, cpuSide.Value, MatchSetup.CpuLevel);
        Debug.Log($"CPU と対戦（{Policy.Ruleset.Name}）：あなたは{(MatchSetup.HumanSide == PlayerType.Player1 ? "先手" : "後手")}、CPU は {CpuLevels.Label(MatchSetup.CpuLevel)}");
    }

    // 合戦将棋型：CPU の席に RealtimeCpuPacer を付ける（対局の始め・棋譜の再生が終わったとき）。人の席には味方の CPU も付ける
    void AttachRealtimeCpu()
    {
        AttachAllyCpus();
        if (!IsRealtime || !cpuSide.HasValue || Cpu == null) return;
        if (realtimeCpu == null) realtimeCpu = new RealtimeCpuPacer(Cpu.Level, Cpu);
        else realtimeCpu.Reset();
        SetSeatController(cpuSide.Value, realtimeCpu);
        Debug.Log($"[CPU] {Policy.Ruleset.Name}：{(cpuSide.Value == PlayerType.Player1 ? "先手" : "後手")}の席を CPU（{CpuLevels.Label(Cpu.Level)}）が " +
                  $"{MatchPolicy.TicksToSec(Policy.CpuIntervalTicks(Cpu.Level)):0.0} 秒ごとに動かす");
    }

    // ------------------------------------------------------------
    //  味方の CPU（王将だけ操作）
    // ------------------------------------------------------------

    private readonly RealtimeCpuPacer[] allyCpus = new RealtimeCpuPacer[2]; // [先手, 後手]（人の席だけ。なければ null）
    private readonly CpuPlayer[] allyBrains = new CpuPlayer[2];

    // 人が動かせるのは王将だけで、王将以外の駒は味方の CPU が動かす対局か（合戦将棋型の基本対戦）
    public bool UsesAllyCpu => IsRealtime && Policy.PlayersControlKingOnly;

    // 席 owner の味方の CPU（なければ null。HUD の「考え中」の表示用）
    public CpuPlayer AllyCpuOf(PlayerType owner) => allyCpus[(int)owner] != null ? allyBrains[(int)owner] : null;

    // 人の席に味方の CPU を付ける（対局の始め・棋譜の再生が終わったとき。付け直すときは考えている途中をやめて間隔を数え直す）
    void AttachAllyCpus()
    {
        if (!UsesAllyCpu) return;
        // CPU と対戦：相手の CPU と同じ強さ（同じ間隔）で王将以外の駒を動かす ／ 2人で対戦：戦闘型の既定の強さ
        int level = CpuLevels.Clamp(HasCpu ? MatchSetup.CpuLevel : Policy.Ruleset.AllyCpuLevel);
        foreach (PlayerType side in BothSides)
        {
            int i = (int)side;
            if (IsCpuSide(side))
            {
                allyCpus[i] = null; // CPU の席は CPU が全部の駒を動かす
                continue;
            }
            if (allyBrains[i] == null)
            {
                CpuPlayer brain = gameObject.AddComponent<CpuPlayer>();
                brain.Init(this, side, level);
                brain.IsAlly = true;
                brain.ActionFilter = IsAllyAction;
                allyBrains[i] = brain;
            }
            if (allyCpus[i] == null) allyCpus[i] = new RealtimeCpuPacer(level, allyBrains[i]);
            else allyCpus[i].Reset();
        }
        Debug.Log($"[味方CPU] {Policy.Ruleset.Name}：人が動かすのは王将だけ。王将以外の駒は味方の CPU（{CpuLevels.Label(level)}）が " +
                  $"{MatchPolicy.TicksToSec(Policy.CpuIntervalTicks(level)):0.0} 秒ごとに動かす");
    }

    // ctrl が今動いている味方の CPU か（席が Controller に任されている間は動かない）
    bool IsActiveAllyCpu(IRealtimeSeatController ctrl)
    {
        if (ctrl == null) return false;
        for (int i = 0; i < allyCpus.Length; i++)
        {
            if (allyCpus[i] == ctrl && seatControllers[i] == null) return true;
        }
        return false;
    }

    // 行動する盤上の駒（action.from）の種類（なければ null）
    PieceType? ActorPieceType(MatchAction a)
    {
        if (a == null || gameBoard == null || !a.from.IsOnBoard) return null;
        GamePiece p = gameBoard.GetPieceAt(a.from.x, a.from.y);
        return p != null ? p.type : (PieceType?)null;
    }

    // 味方の CPU が行う行動か（王将以外の駒の移動・必殺技・狙撃と、持ち駒を打つ）
    bool IsAllyAction(MatchAction a) => MatchPolicy.IsAllyActionUnderKingOnly(a, ActorPieceType(a));

    // 席 owner の人の入力から出してよい行動か（王将だけ操作のとき：王将の行動・魔法・錬成・停止世界の解除だけ）
    bool IsHumanAllowedAction(MatchAction a, PlayerType owner)
    {
        if (!UsesAllyCpu || seatControllers[(int)owner] != null) return true;
        return MatchPolicy.IsHumanActionUnderKingOnly(a, ActorPieceType(a));
    }

    // 人の席で、盤上の駒 piece を人が選べないか（王将だけ操作のときの王将以外の駒）
    bool IsAllyControlledPiece(GamePiece piece) => UsesAllyCpu && piece != null && piece.type != PieceType.King;
}
