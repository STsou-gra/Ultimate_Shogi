using System.Collections.Generic;
using UnityEngine;

// 必殺技：桂「天馬跳躍」（Prototype の src/defs/specials/ke.js）
// 周囲 2 マス以内の好きなマスへ大跳躍。着地点の相手の駒を取る（火薬なら爆発、炎上・落とし穴なら消失）
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Knight Leap (天馬跳躍)")]
public class KnightLeapSpecial : SpecialMove
{
    public KnightLeapSpecial()
    {
        SetDefaults("天馬跳躍", 2, 0xb388ff, "周囲2マス以内の好きなマスへ大跳躍。着地点の相手の駒を取る。");
    }

    public override List<Vector2Int> GetTargets(SpecialContext context)
    {
        GamePiece p = context.Caster;
        List<Vector2Int> result = new List<Vector2Int>();
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = p.X + dx;
                int y = p.Y + dy;
                if (context.CanLandOn(p, x, y)) result.Add(new Vector2Int(x, y));
            }
        }
        return result;
    }

    public override string GetTargetPrompt()
    {
        return "跳ぶ先のマスを選択（周囲2マス以内）";
    }

    public override void Apply(SpecialContext context)
    {
        if (!context.Target.HasValue) return;
        Vector2Int t = context.Target.Value;
        bool? alive = context.Step(context.Caster, t.x, t.y, PieceMoveStyle.BigJump, 0.7f);
        if (alive == true) context.Landed(context.Caster, 10);
    }
}
