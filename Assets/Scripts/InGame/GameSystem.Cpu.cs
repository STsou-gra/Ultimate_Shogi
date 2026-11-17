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

    // 合戦将棋型：CPU の席に RealtimeCpuPacer を付ける（対局の始め・棋譜の再生が終わったとき）
    void AttachRealtimeCpu()
    {
        if (!IsRealtime || !cpuSide.HasValue || Cpu == null) return;
        if (realtimeCpu == null) realtimeCpu = new RealtimeCpuPacer(Cpu.Level, Cpu);
        else realtimeCpu.Reset();
        SetSeatController(cpuSide.Value, realtimeCpu);
        Debug.Log($"[CPU] {Policy.Ruleset.Name}：{(cpuSide.Value == PlayerType.Player1 ? "先手" : "後手")}の席を CPU（{CpuLevels.Label(Cpu.Level)}）が " +
                  $"{MatchPolicy.TicksToSec(Policy.CpuIntervalTicks(Cpu.Level)):0.0} 秒ごとに動かす");
    }
}
