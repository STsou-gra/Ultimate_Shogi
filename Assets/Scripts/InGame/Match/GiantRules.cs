using System.Collections.Generic;

// 錬成駒「巨人」の決まり（Prototype の src/defs/pieces.js の KYOJIN の giant: { size: 3, hp: 3, maxSteps: 5 } と
// src/core/board.js の giantCells・footprint、src/core/moves.js の giantFrontRow・giantMoves・giantDropSquares）。
// GameObject に依存しないので、本編（GameSystem・GameBoard・CheckRules）と盤面のスナップショット（BoardSimulator・SnapshotRules）の両方が使う。
//
// 座標は GamePiece と同じ（x = 段、y = 筋。先手の前は +x）。巨人の X・Y は 3×3 の中央のマスで、盤のデータには占めるマス全部に同じ駒を入れる。
//  ・移動 … 前へまっすぐ 1〜MaxSteps マス。移動先は「先頭の 1 列（横 3 マス）の中央のマス」で表す（クリックするマス。駒の中央はその 1 つ手前）。
//           1 マス進むごとに、新しく入る先頭の 1 列が全部入れるマス（盤内・破壊／壁でない・空き／相手の駒／火薬）でなければ、その手前で止まる。
//           先頭の 1 列に駒・炎上・落とし穴があれば、そこまで（そこで止まる）。時間停止中は動けない
//  ・打つ … 中央のマスを選ぶ。占めるマスが全部盤内の、駒のいない通常状態のマス（破壊・炎上・落とし穴・壁・時間停止の予定の駒がない）。
//           前へ 1 マスも動けない位置（相手側の端から 1 段以内に中央）には打てない。序盤は占めるマスが 1 つでも敵陣なら打てない
//  ・HP … MaxHp。攻撃（取り・消失）を受けるたびに 1 減り、MaxHp 回目で倒れる（それまでの攻撃では、入ろうとした駒は元のマスへ跳ね返る）
public static class GiantRules
{
    public const int Size = 3;      // 3×3 マス
    public const int Radius = 1;    // 中央から端までのマス数
    public const int MaxHp = 3;
    public const int MaxSteps = 5;

    // 先頭の 1 列のマスに何があるか（移動の判定用）
    public enum Occupant { Empty, Own, Enemy, Bomb }

    static int Forward(PlayerType owner) => owner == PlayerType.Player1 ? 1 : -1;

    // 中央 (cx, cy) の巨人が占めるマス（盤外も含む。x → y の順）
    public static List<MatchSquare> Cells(int cx, int cy)
    {
        List<MatchSquare> res = new List<MatchSquare>(Size * Size);
        for (int x = cx - Radius; x <= cx + Radius; x++)
        {
            for (int y = cy - Radius; y <= cy + Radius; y++) res.Add(new MatchSquare(x, y));
        }
        return res;
    }

    // (x, y) が中央 (cx, cy) の巨人の占めるマスか
    public static bool Covers(int cx, int cy, int x, int y)
    {
        return System.Math.Abs(x - cx) <= Radius && System.Math.Abs(y - cy) <= Radius;
    }

    // 中央が (cx, cy) のときの先頭の 1 列（前方の端の横 3 マス。筋の小さい順）
    public static List<MatchSquare> FrontRow(PlayerType owner, int cx, int cy)
    {
        List<MatchSquare> res = new List<MatchSquare>(Size);
        int x = cx + Forward(owner) * Radius;
        for (int y = cy - Radius; y <= cy + Radius; y++) res.Add(new MatchSquare(x, y));
        return res;
    }

    // 移動先 to（先頭の 1 列の中央のマス）へ動いたあとの中央のマス
    public static MatchSquare CenterAfter(PlayerType owner, MatchSquare to)
    {
        return new MatchSquare(to.x - Forward(owner) * Radius, to.y);
    }

    // 移動先 to へ動いたときの先頭の 1 列
    public static List<MatchSquare> FrontRowAt(PlayerType owner, MatchSquare to)
    {
        MatchSquare c = CenterAfter(owner, to);
        return FrontRow(owner, c.x, c.y);
    }

    // 中央 (cx, cy) の巨人の移動先（先頭の 1 列の中央のマス。近い順）。
    // occupant：マスの駒（巨人自身のマスは呼ばれない）、blocked：入れないマス（破壊・壁）、hazard：炎上・落とし穴
    public static List<MatchSquare> Moves(PlayerType owner, int cx, int cy,
        System.Func<int, int, Occupant> occupant, System.Func<int, int, bool> blocked, System.Func<int, int, bool> hazard)
    {
        List<MatchSquare> res = new List<MatchSquare>();
        int f = Forward(owner);
        for (int k = 1; k <= MaxSteps; k++)
        {
            List<MatchSquare> row = FrontRow(owner, cx + f * k, cy);
            bool ok = true, stop = false;
            foreach (MatchSquare c in row)
            {
                if (!BoardCells.InBounds(c.x, c.y) || blocked(c.x, c.y)) { ok = false; break; }
                Occupant o = occupant(c.x, c.y);
                if (o == Occupant.Own) { ok = false; break; }
                if (o != Occupant.Empty || hazard(c.x, c.y)) stop = true;
            }
            if (!ok) break;
            res.Add(new MatchSquare(row[0].x, cy));
            if (stop) break;
        }
        return res;
    }

    // owner が中央 (cx, cy) に巨人を打てるか。free：駒のいない通常状態のマス（破壊・炎上・落とし穴・壁・予定の駒がない）か、
    // zoneLocked：序盤で敵陣に打てない期間か
    public static bool CanDropAt(PlayerType owner, int cx, int cy, System.Func<int, int, bool> free, bool zoneLocked)
    {
        if (cx - Radius < 0 || cx + Radius >= BoardCells.Size || cy - Radius < 0 || cy + Radius >= BoardCells.Size) return false;
        int distToEnd = owner == PlayerType.Player1 ? BoardCells.Size - 1 - cx : cx;
        if (distToEnd <= Radius) return false; // 前へ 1 マスも動けない
        foreach (MatchSquare c in Cells(cx, cy))
        {
            if (!free(c.x, c.y)) return false;
            if (zoneLocked && BoardZones.IsEnemyZone(owner, c.x)) return false;
        }
        return true;
    }
}
