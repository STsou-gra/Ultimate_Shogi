using System.Collections.Generic;
using UnityEngine;

// 必殺技で使い回す処理（Prototype の src/defs/specials/kit.js）
// Unity の盤は X が段（Player1 の前方が +X）、Y が筋
public static class SpecialKit
{
    // 縦横 4 方向・斜め 4 方向（どちらも向きに偏りがないので、手番で反転しなくてよい）
    public static readonly Vector2Int[] Orthogonal =
    {
        new Vector2Int(1, 0), new Vector2Int(-1, 0), new Vector2Int(0, 1), new Vector2Int(0, -1),
    };
    public static readonly Vector2Int[] Diagonal =
    {
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, 1), new Vector2Int(-1, -1),
    };

    // ------------------------------------------------------------
    //  突進（歩「突撃」・香「貫通槍」）
    // ------------------------------------------------------------

    // 前方への突進経路（pierce = true なら相手の駒を貫通して進む）。
    // 翼状態の駒は破壊・炎上・落とし穴のマスを通り抜け、火薬の上は飛び越える（経路に入れない＝爆発させない。盤の端の手前の火薬なら、その手前で止まる）
    public static List<Vector2Int> ChargePath(SpecialContext ctx, GamePiece p, bool pierce)
    {
        List<Vector2Int> path = new List<Vector2Int>();
        bool wing = PieceRules.HasWing(p);
        int f = SpecialContext.Forward(p.player);
        int y = p.Y;
        for (int x = p.X + f; SpecialContext.InBounds(x, y); x += f)
        {
            if (ctx.IsBlockedFor(p, x, y)) break;                // 破壊マス・壁の手前で止まる（翼の駒は破壊マスを通り抜ける）
            GamePiece q = ctx.PieceAt(x, y);
            if (q == null)
            {
                path.Add(new Vector2Int(x, y));
                if (ctx.IsHazardFor(p, x, y)) break;              // 炎上・落とし穴に入ると消失（翼の駒は通り抜ける）
                continue;
            }
            if (SpecialContext.IsExplosive(q))
            {
                if (wing) continue;                               // 翼の駒は火薬を飛び越える
                path.Add(new Vector2Int(x, y));                   // 火薬に突っ込む → 爆発
                break;
            }
            if (q.player != p.player)
            {
                path.Add(new Vector2Int(x, y));
                if (pierce) continue;
                break;
            }
            break;                                                // 味方の駒の手前で止まる
        }
        return path;
    }

    // 経路に沿って 1 マスずつ高速移動（残像付き）。途中の相手の駒は Step で取られる
    public static void Charge(SpecialContext ctx, GamePiece p, bool pierce)
    {
        List<Vector2Int> path = ChargePath(ctx, p, pierce);
        Color trail = PlayerColor(p.player);
        foreach (Vector2Int sq in path)
        {
            ctx.Afterimage(p, trail);
            bool? alive = ctx.Step(p, sq.x, sq.y, PieceMoveStyle.Dash, 0.07f);
            if (alive != true) return;
        }
        ctx.Landed(p, 3);
    }

    // ------------------------------------------------------------
    //  光線（角「光芒四射」・飛「龍王旋風」）
    // ------------------------------------------------------------
    public struct Beam
    {
        public Vector2Int Direction;
        public Vector2Int? End;   // 光線が届く最後のマス（盤の端・駒・破壊マス・壁）
        public GamePiece Hit;     // 当たって取る駒（相手の駒か火薬。なければ null）
    }

    // 指定方向へ光線を撃ったときの着弾
    public static List<Beam> BeamPlan(SpecialContext ctx, GamePiece p, Vector2Int[] dirs)
    {
        List<Beam> plan = new List<Beam>();
        foreach (Vector2Int d in dirs)
        {
            Beam beam = new Beam { Direction = d };
            int x = p.X + d.x;
            int y = p.Y + d.y;
            while (SpecialContext.InBounds(x, y))
            {
                beam.End = new Vector2Int(x, y);
                if (ctx.IsWall(x, y)) break;                     // 壁に当たって止まる
                GamePiece q = ctx.PieceAt(x, y);
                if (ctx.IsBroken(x, y) && q == null) break;      // 破壊マスに吸い込まれて止まる（破壊マスの上の翼の駒には当たる）
                if (q != null)
                {
                    if (q.player != p.player || SpecialContext.IsExplosive(q)) beam.Hit = q;
                    break;
                }
                x += d.x;
                y += d.y;
            }
            plan.Add(beam);
        }
        return plan;
    }

    public static bool AnyBeamHits(SpecialContext ctx, GamePiece p, Vector2Int[] dirs)
    {
        foreach (Beam b in BeamPlan(ctx, p, dirs))
        {
            if (b.Hit != null) return true;
        }
        return false;
    }

    // 光線を放ち、各方向で最初に当たった相手の駒を取る（火薬なら爆発）
    public static void FireBeams(SpecialContext ctx, GamePiece p, Vector2Int[] dirs, Color color)
    {
        List<Beam> plan = BeamPlan(ctx, p, dirs);
        List<Vector2Int> ends = new List<Vector2Int>();
        foreach (Beam b in plan)
        {
            if (b.End.HasValue) ends.Add(b.End.Value);
        }
        if (ctx.Fx != null) ctx.Fx.Beams(new Vector2Int(p.X, p.Y), ends, color, ctx.Time);
        ctx.Wait(0.3f);

        foreach (Beam b in plan)
        {
            if (!ctx.IsAlive(b.Hit)) continue; // 先の爆発で消えた
            if (SpecialContext.IsExplosive(b.Hit)) ctx.Explode(b.Hit.X, b.Hit.Y);
            else ctx.Capture(b.Hit, b.Direction);
        }
    }

    // プレイヤーの色（PieceMoveAnimator・GameTickEffects と同じ）
    public static Color PlayerColor(PlayerType player)
    {
        return player == PlayerType.Player1 ? SpecialMove.Hex(0x4fc3ff) : SpecialMove.Hex(0xff5277);
    }
}
