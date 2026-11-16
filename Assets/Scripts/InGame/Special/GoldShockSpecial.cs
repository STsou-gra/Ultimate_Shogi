using System.Collections.Generic;
using UnityEngine;

// 必殺技：金「金剛衝」（Prototype の src/defs/specials/ki.js）
// 周囲 8 マスの相手の駒を 1 マス吹き飛ばす。盤外に押し出した駒は取る（持ち駒になる）。
// 押し出し先に駒・破壊マス・壁があればぶつかって止まる。炎上・落とし穴なら消失、火薬なら爆発
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Gold Shock (金剛衝)")]
public class GoldShockSpecial : SpecialMove
{
    public GoldShockSpecial()
    {
        SetDefaults("金剛衝", 2, 0xffd54a, "周囲8マスの相手の駒を1マス吹き飛ばす。盤外に押し出した駒は取る。");
    }

    public override bool CanUse(SpecialContext context)
    {
        foreach (SpecialContext.Neighbor n in context.Neighbors(context.Caster))
        {
            if (n.Piece.player != context.Owner) return true;
        }
        // 討伐任務：周囲にボスがいれば使える
        foreach (Vector2Int c in Around(context.Caster))
        {
            if (context.BossAt(c.x, c.y)) return true;
        }
        return false;
    }

    // 周囲 8 マス（盤の外も含む）
    static List<Vector2Int> Around(GamePiece p)
    {
        List<Vector2Int> res = new List<Vector2Int>(8);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0) res.Add(new Vector2Int(p.X + dx, p.Y + dy));
            }
        }
        return res;
    }

    public override void Apply(SpecialContext context)
    {
        GamePiece p = context.Caster;
        if (context.Fx != null) context.Fx.Shock(new Vector2Int(p.X, p.Y), context.Time);
        context.Wait(0.15f);
        context.HitBoss(Around(p)); // 討伐任務：周囲にボスがいれば攻撃（ボスは押せない）
        // 周りの駒は同時に吹き飛ぶ（盤面は 1 つずつ処理し、見た目の時刻だけそろえる）
        List<SpecialContext.Neighbor> targets = context.Neighbors(p).FindAll(n => n.Piece.player != p.player);
        float start = context.Time;
        float end = start;
        foreach (SpecialContext.Neighbor n in targets)
        {
            if (!context.IsAlive(n.Piece)) continue;
            context.Time = start;
            context.Push(n.Piece, n.DX, n.DY);
            end = Mathf.Max(end, context.Time);
        }
        context.Time = end;
    }
}
