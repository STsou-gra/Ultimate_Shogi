using UnityEngine;

// 世界の終わり（超常魔法）のターンの終わりの崩壊（Prototype の src/core/turn.js の roundEnd のうち worldEndCollapse）。
// 1 ターンの終わり（後手の手番の行動のあと）に、発動したプレイヤー 1 人につき MatchConfig.WorldEndCells マスが崩れる
// （発動したターンの分は発動したときに崩れているので、そのターンの終わりには崩れない）。
// 「赤く点滅 → 崩落」を見せてから手番を渡すので、GameSystem の roundEndActions として OnRoundEnd の直前に行う
public class WorldEndCollapse : TurnEndAction
{
    public static readonly Color DoomColor = SpecialMove.Hex(0xd50000);

    public override string Title => WorldEndEffect.Title;
    public override Color Color => DoomColor;
    public override string GetStatusText(PlayerType owner) => "世界が崩れる……";

    public override bool HasWork(GameSystem system, PlayerType owner)
    {
        MatchState match = system != null ? system.Match : null;
        if (match == null || owner != match.Turn.SecondPlayer) return false; // 1 ターンの終わり（後手の手番のあと）だけ
        return match.WorldEndCellsThisRound > 0 && system.WorldEndCandidates().Count > 0;
    }

    public override void Run(TurnEndContext context)
    {
        int n = context.GameSystem.Match.WorldEndCellsThisRound;
        WorldEndEffect.Collapse(context.GameSystem, n, context.Timeline, context.Fx);
    }
}
