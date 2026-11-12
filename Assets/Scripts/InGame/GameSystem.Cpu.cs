using UnityEngine;

// CPU と対戦（Prototype の src/controller/CpuController.js と Match の Controller）。GameSystem の続き（partial）。
//
//  ・対戦モードの画面（ModeSelectView）で「CPU と対戦」を選ぶと MatchSetup に入り、対局の開始時（Start）に CpuPlayer を足す
//  ・CPU の手番は、人の入力（盤のクリック・キー・ボタン）を受け付けない（HandleTurnUpdate・CanActNow・UseSkill・Dispatch）。
//    CPU は CpuPlayer が DispatchFromCpu で行動を実行する（判定は人と同じ CanDispatch）
//  ・カメラは人の側に固定する（手番ごとに盤を回さない）
//  ・オンライン対戦では使わない
public partial class GameSystem
{
    private PlayerType? cpuSide;   // CPU が指す側（CPU と対戦でなければ null）
    private bool dispatchingCpu;   // DispatchFromCpu の中（CPU の手番でも Dispatch を通す）

    public CpuPlayer Cpu { get; private set; }

    public bool HasCpu => cpuSide.HasValue;

    // side を CPU が指すか
    public bool IsCpuSide(PlayerType side) => cpuSide.HasValue && cpuSide.Value == side;

    // 今が CPU の手番か
    public bool IsCpuTurn => IsInTurn && cpuSide.HasValue && CurrentSide == cpuSide.Value;

    // CPU が考え始めてよいか（CPU の手番で、演出・自動行動・選択の途中でない）
    public bool CanCpuAct => IsCpuTurn && !IsReplaying && !IsBusy && !isRunningTurnEnd && !isWaitingForPromoteChoice
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
    PlayerType CameraSideFor(PlayerType turn)
    {
        return cpuSide.HasValue ? BoardZones.Opponent(cpuSide.Value) : turn;
    }

    // 対局の開始時：CPU と対戦なら CpuPlayer を足す
    void SetupCpu()
    {
        if (!MatchSetup.IsVsCpu) return;
        if (IsOnlineMatch)
        {
            Debug.LogWarning("オンライン対戦では CPU と対戦できないので、2人で対戦として始めます");
            return;
        }
        cpuSide = MatchSetup.CpuSide;
        Cpu = gameObject.AddComponent<CpuPlayer>();
        Cpu.Init(this, cpuSide.Value, MatchSetup.CpuLevel);
        Debug.Log($"CPU と対戦：あなたは{(MatchSetup.HumanSide == PlayerType.Player1 ? "先手" : "後手")}、CPU は {CpuLevels.Label(MatchSetup.CpuLevel)}");
    }
}
