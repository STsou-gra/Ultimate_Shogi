using System.Collections.Generic;

// 王手・打ち歩詰めの判定を、盤面のスナップショット（BoardSnapshot）の上で行う（Rules/GameRules.cs の CheckRules を移したもの）。
// 判定の中身は CheckRules と同じにしている（王将が 2 枚以上なら王手・詰みはない、走り駒の利きは破壊・壁・炎上・落とし穴で止まる、翼状態の駒はマスの効果を受けない、
// 火薬に入る手は爆発まで読む、狙撃手がいれば打った歩を撃ち抜ける、巨人は先頭の 1 列で取る・HP が残れば取れない など）。
// CheckRules を変えたら、こちらも同じように変えること
public static class SnapshotRules
{
    const int NONE = -1;

    // player が王手されているか（CheckRules.IsInCheck）
    public static bool IsInCheck(BoardSnapshot s, PlayerType player)
    {
        return IsInCheck(s.Grid, s, null, NONE, player);
    }

    // player が (x, y) に歩を打つと打ち歩詰めになるか（CheckRules.IsUchifuzume）
    public static bool IsUchifuzume(BoardSnapshot s, PlayerType player, int x, int y)
    {
        SnapshotPiece[] cells = s.Grid;
        PlayerType opponent = BoardZones.Opponent(player);
        if (CountKings(cells, opponent) != 1) return false;

        // 炎上のマスに打った歩はすぐ消失するので、王手にならない
        if (s.IsHazard(x, y)) return false;

        // 打った歩のすぐ前に相手の王将がいなければ王手ではない
        int front = GameHelper.CalcPanelNum(x + PieceMoves.Forward(player), y);
        if (front == NONE) return false;
        SnapshotPiece king = cells[front];
        if (king == null || king.Owner != opponent || king.type != PieceType.King) return false;

        int pawn = GameHelper.CalcPanelNum(x, y);
        return !CanEscapePawnCheck(cells, s, pawn, front, opponent);
    }

    // その移動の後に、自分の王将が取られる状態にならないか（CheckRules.IsMoveSafe。詰みの判定）
    public static bool IsMoveSafe(BoardSnapshot s, SnapshotPiece piece, int toX, int toY)
    {
        SnapshotPiece[] cells = s.Grid;
        if (CountKings(cells, piece.Owner) != 1) return true;
        int from = GameHelper.CalcPanelNum(piece.x, piece.y);
        int to = GameHelper.CalcPanelNum(toX, toY);
        if (from == NONE || to == NONE) return true;

        if (piece.IsGiant)
        {
            // 巨人：先頭の 1 列に相手の最後の王将がいれば、取って勝ち
            foreach (MatchSquare c in GiantRules.FrontRowAt(piece.Owner, new MatchSquare(toX, toY)))
            {
                SnapshotPiece q = At(cells, c.x, c.y);
                if (q != null && q.owner != piece.owner && q.type == PieceType.King && CountKings(cells, q.Owner) == 1) return true;
            }
            return !IsInCheck(SimulateGiantMove(cells, s, piece, toX, toY, out _), s, null, NONE, piece.Owner);
        }

        // 相手の最後の王将を取る手は、その時点で勝ちなので指せる
        SnapshotPiece target = cells[to];
        if (target != null && target.owner != piece.owner && target.type == PieceType.King && CountKings(cells, target.Owner) == 1) return true;

        // 最後の王将が炎上・落とし穴のマスに入ると消失して負けになる（翼状態の王将は消失しない）
        if (piece.type == PieceType.King && s.IsHazardFor(piece, toX, toY) && !Bounces(piece, target)) return false;

        SnapshotPiece[] sim = SimulateMove(cells, s, piece, from, to);
        return !IsInCheck(sim, s, null, NONE, piece.Owner);
    }

    // 狙撃した後に、自分の王将が取られる状態にならないか（CheckRules.IsSnipeSafe）
    public static bool IsSnipeSafe(BoardSnapshot s, SnapshotPiece sniper, int targetX, int targetY)
    {
        SnapshotPiece[] cells = s.Grid;
        if (CountKings(cells, sniper.Owner) != 1) return true;
        int target = GameHelper.CalcPanelNum(targetX, targetY);
        if (target == NONE || cells[target] == null) return true;
        SnapshotPiece[] sim = (SnapshotPiece[])cells.Clone();
        if (PieceRules.IsExplosive(sim[target].type)) Explode(sim, targetX, targetY);
        else if (GiantSurvives(sim[target], 1)) return !IsInCheck(cells, s, null, NONE, sniper.Owner); // 巨人の HP が残る
        else RemoveFrom(sim, sim[target]);
        return !IsInCheck(sim, s, null, NONE, sniper.Owner);
    }

    // 持ち駒 type を打った後に、自分の王将が取られる状態にならないか（CheckRules.IsDropSafe）
    public static bool IsDropSafe(BoardSnapshot s, PlayerType player, PieceType type, int x, int y)
    {
        SnapshotPiece[] cells = s.Grid;
        if (CountKings(cells, player) != 1) return true;
        // 炎上のマスに打った駒はすぐ消失するので、通り道をふさげない（鳥は消失しないので、ふさげる）
        if (!PieceRules.IsGiant(type) && s.IsHazardForType(type, x, y)) return !IsInCheck(cells, s, null, NONE, player);
        return !IsInCheck(cells, s, DropBlockers(type, x, y), NONE, player);
    }

    // 打った駒がふさぐマス（巨人は 3×3）
    static int[] DropBlockers(PieceType type, int x, int y)
    {
        if (!PieceRules.IsGiant(type)) return new[] { GameHelper.CalcPanelNum(x, y) };
        List<MatchSquare> c = GiantRules.Cells(x, y);
        int[] res = new int[c.Count];
        for (int i = 0; i < c.Count; i++) res[i] = GameHelper.CalcPanelNum(c[i].x, c[i].y);
        return res;
    }

    // 歩で王手された側が、駒を動かして王手を回避できるか
    static bool CanEscapePawnCheck(SnapshotPiece[] cells, BoardSnapshot s, int pawn, int pawnAttack, PlayerType defender)
    {
        int[] pawnBlocker = { pawn };
        for (int from = 0; from < cells.Length; from++)
        {
            SnapshotPiece piece = cells[from];
            if (piece == null || piece.Owner != defender || piece.undead) continue; // アンデッドは持ち主が動かせない
            if (!BoardSnapshot.IsAnchor(piece, from)) continue;                    // 巨人は中央のマスで 1 回だけ

            // 狙撃手がいれば、打った歩を撃ち抜いて王手を外せる
            if (PieceRules.IsSniper(piece.type)) return true;

            if (piece.IsGiant)
            {
                // 巨人：前へ動く手（打った歩は先頭の 1 列で取れる）
                foreach (MatchSquare m in GiantMovesOn(cells, s, piece, pawnBlocker))
                {
                    SnapshotPiece[] gsim = SimulateGiantMove(cells, s, piece, m.x, m.y, out bool moved);
                    bool tookPawn = moved && GiantRules.FrontRowAt(piece.Owner, m).Exists(c => GameHelper.CalcPanelNum(c.x, c.y) == pawn);
                    if (!IsInCheck(gsim, s, tookPawn ? null : pawnBlocker, tookPawn ? NONE : pawnAttack, defender)) return true;
                }
                continue;
            }

            for (int to = 0; to < cells.Length; to++)
            {
                if (to == from) continue;
                SnapshotPiece target = cells[to];
                bool intoBomb = BoardSnapshot.Detonates(piece, target); // 鳥は火薬を爆発させない（味方の火薬には入れない）
                if (target != null && target.Owner == defender && !intoBomb) continue;
                if (!CanAttack(cells, s, pawnBlocker, from, to)) continue;

                // 王将が炎上・落とし穴のマスへ逃げても消失するだけなので、回避にならない
                int toX, toY;
                GameHelper.CalcPanelPosition(out toX, out toY, to);
                if (piece.type == PieceType.King && s.IsHazardFor(piece, toX, toY) && !Bounces(piece, target)) continue;

                SnapshotPiece[] sim = SimulateMove(cells, s, piece, from, to);

                // 打った歩を取ったら（火薬の爆発に巻き込んだら）、仮の歩は盤上から消える
                int pawnX, pawnY;
                GameHelper.CalcPanelPosition(out pawnX, out pawnY, pawn);
                bool capturedPawn = (to == pawn) || (intoBomb && PieceRules.IsInExplosion(toX, toY, pawnX, pawnY));
                if (!IsInCheck(sim, s, capturedPawn ? null : pawnBlocker, capturedPawn ? NONE : pawnAttack, defender))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // piece が target のマスへ入ろうとして跳ね返るか（HP が残る相手の巨人）
    static bool Bounces(SnapshotPiece piece, SnapshotPiece target)
    {
        return target != null && target.owner != piece.owner && GiantSurvives(target, 1);
    }

    // 巨人 g が hits 回の攻撃を受けても残るか（巨人でなければ false）
    static bool GiantSurvives(SnapshotPiece g, int hits)
    {
        if (g == null || !g.IsGiant) return false;
        int hp = g.hp > 0 ? g.hp : GiantRules.MaxHp;
        return hp - hits > 0;
    }

    static SnapshotPiece At(SnapshotPiece[] cells, int x, int y)
    {
        int i = GameHelper.CalcPanelNum(x, y);
        return i == NONE ? null : cells[i];
    }

    // 配列から駒 q を取り除く（巨人は占めるマス全部）
    static void RemoveFrom(SnapshotPiece[] sim, SnapshotPiece q)
    {
        if (q == null) return;
        for (int i = 0; i < sim.Length; i++)
        {
            if (sim[i] == q) sim[i] = null;
        }
    }

    // 配列をコピーして、from の駒を to へ動かした後の配列を作る（炎上・落とし穴のマスに入った駒は消失する。火薬なら爆発させる。
    // HP が残る相手の巨人のマスなら跳ね返って動かない。最後の HP の巨人なら取って入る）
    static SnapshotPiece[] SimulateMove(SnapshotPiece[] cells, BoardSnapshot s, SnapshotPiece piece, int from, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        SnapshotPiece[] sim = (SnapshotPiece[])cells.Clone();
        SnapshotPiece target = sim[to];
        if (Bounces(piece, target)) return sim;
        if (target != null && target.IsGiant && target != piece) RemoveFrom(sim, target);
        sim[from] = null;
        bool landed = true;
        if (target != null && target != piece && BoardSnapshot.Detonates(piece, target))
        {
            Explode(sim, toX, toY);
            landed = !PieceRules.IsDestroyedByExplosion(piece.type) && sim[to] == null;
        }
        if (landed) sim[to] = s.IsHazardFor(piece, toX, toY) ? null : piece;
        return sim;
    }

    // 巨人 g が配列 cells の上で動ける先（GiantRules.Moves。blockers は仮の駒のマス：相手の駒として扱う）
    static List<MatchSquare> GiantMovesOn(SnapshotPiece[] cells, BoardSnapshot s, SnapshotPiece g, int[] blockers)
    {
        if (s.timeStopped) return new List<MatchSquare>();
        return GiantRules.Moves(g.Owner, g.x, g.y,
            (x, y) =>
            {
                int i = GameHelper.CalcPanelNum(x, y);
                SnapshotPiece q = cells[i];
                if (q == null) return IsBlocker(blockers, i) ? GiantRules.Occupant.Enemy : GiantRules.Occupant.Empty;
                if (q == g) return GiantRules.Occupant.Empty;
                if (PieceRules.IsExplosive(q.type)) return GiantRules.Occupant.Bomb;
                return q.owner == g.owner ? GiantRules.Occupant.Own : GiantRules.Occupant.Enemy;
            },
            (x, y) => s.IsBlockedFor(g, x, y),
            (x, y) => s.IsHazard(x, y));
    }

    // 巨人 g を移動先 (toX, toY)（先頭の 1 列の中央）へ動かした後の配列（BoardSimulator.ApplyGiantMove と同じ流れ）：
    // 先頭の 1 列の相手の駒を取り（盾・HP が残る巨人がいれば跳ね返って動かない。仮の駒（打った歩）は配列にないので、そのまま取ったことになる）、火薬は爆発、
    // 新しく入ったマスが落とし穴・炎上なら 1 回の攻撃。moved：動けたか
    static SnapshotPiece[] SimulateGiantMove(SnapshotPiece[] cells, BoardSnapshot s, SnapshotPiece g, int toX, int toY, out bool moved)
    {
        SnapshotPiece[] sim = (SnapshotPiece[])cells.Clone();
        moved = false;
        MatchSquare center = GiantRules.CenterAfter(g.Owner, new MatchSquare(toX, toY));
        List<MatchSquare> row = GiantRules.FrontRow(g.Owner, center.x, center.y);
        bool blocked = false;
        List<MatchSquare> bombs = new List<MatchSquare>();
        foreach (MatchSquare c in row)
        {
            int i = GameHelper.CalcPanelNum(c.x, c.y);
            SnapshotPiece q = sim[i];
            if (q == null) continue;
            if (PieceRules.IsExplosive(q.type)) { bombs.Add(c); continue; }
            if (q.owner == g.owner) { blocked = true; continue; }
            if (q.shield || GiantSurvives(q, 1)) { blocked = true; continue; }
            RemoveFrom(sim, q);
        }
        if (blocked) return sim;
        int hits = 0;
        RemoveFrom(sim, g);
        foreach (MatchSquare c in bombs) sim[GameHelper.CalcPanelNum(c.x, c.y)] = null;
        foreach (MatchSquare c in GiantRules.Cells(center.x, center.y)) sim[GameHelper.CalcPanelNum(c.x, c.y)] = g;
        foreach (MatchSquare c in bombs)
        {
            hits++;
            ExplodeAround(sim, c.x, c.y, g);
        }
        bool hazard = false;
        foreach (MatchSquare c in row)
        {
            if (s.IsHazard(c.x, c.y)) hazard = true;
        }
        if (hazard) hits++;
        if (!GiantSurvives(g, hits) && hits > 0) RemoveFrom(sim, g);
        moved = true;
        return sim;
    }

    // 爆発（巨人 self の 1 回の攻撃は呼び出し側で数える。self 以外は Explode と同じ）
    static void ExplodeAround(SnapshotPiece[] sim, int cx, int cy, SnapshotPiece self)
    {
        int r = MatchConfig.ExplosionRadius;
        List<SnapshotPiece> victims = new List<SnapshotPiece>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                SnapshotPiece q = At(sim, x, y);
                if (q != null && q != self && !victims.Contains(q)) victims.Add(q);
            }
        }
        foreach (SnapshotPiece q in victims)
        {
            if (!PieceRules.IsDestroyedByExplosion(q.type) || GiantSurvives(q, 1)) continue;
            RemoveFrom(sim, q);
        }
    }

    // 配列の上で爆発させる（GameSystem.Explode と同じ範囲・同じ王将の扱い。巨人は HP が残れば残る）
    static void Explode(SnapshotPiece[] sim, int cx, int cy)
    {
        int r = MatchConfig.ExplosionRadius;
        List<(int x, int y)> chain = new List<(int x, int y)>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                int i = GameHelper.CalcPanelNum(x, y);
                if (i == NONE || sim[i] == null || !PieceRules.IsDestroyedByExplosion(sim[i].type)) continue;
                if (sim[i].IsGiant)
                {
                    if (!GiantSurvives(sim[i], 1)) RemoveFrom(sim, sim[i]);
                    continue;
                }
                if (MatchConfig.ChainExplosion && PieceRules.IsExplosive(sim[i].type) && (x != cx || y != cy)) chain.Add((x, y));
                sim[i] = null;
            }
        }
        foreach ((int x, int y) c in chain) Explode(sim, c.x, c.y);
    }

    // blockers: 仮の駒（打つ駒）がいるマス／blockerAttack: その仮の駒が利きを持つマス
    static bool IsInCheck(SnapshotPiece[] cells, BoardSnapshot s, int[] blockers, int blockerAttack, PlayerType player)
    {
        if (CountKings(cells, player) != 1) return false;

        int king = FindKing(cells, player);
        if (king == NONE) return false;
        if (king == blockerAttack) return true;

        PlayerType attacker = BoardZones.Opponent(player);
        for (int i = 0; i < cells.Length; i++)
        {
            SnapshotPiece p = cells[i];
            if (p == null || p.Owner != attacker) continue;
            if (p.IsGiant)
            {
                if (BoardSnapshot.IsAnchor(p, i) && GiantAttacks(cells, s, blockers, p, king)) return true;
                continue;
            }
            if (CanAttack(cells, s, blockers, i, king)) return true;
        }
        return false;
    }

    // 巨人 g の先頭の 1 列が、動いたときに to のマスへ届くか（取れる）
    static bool GiantAttacks(SnapshotPiece[] cells, BoardSnapshot s, int[] blockers, SnapshotPiece g, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        foreach (MatchSquare m in GiantMovesOn(cells, s, g, blockers))
        {
            foreach (MatchSquare c in GiantRules.FrontRowAt(g.Owner, m))
            {
                if (c.x == toX && c.y == toY) return true;
            }
        }
        return false;
    }

    // cells[from] の駒が to のマスに利いているか（駒の動き・破壊マス・途中の駒やマス）
    static bool CanAttack(SnapshotPiece[] cells, BoardSnapshot s, int[] blockers, int from, int to)
    {
        SnapshotPiece piece = cells[from];
        if (piece == null || from == to) return false;

        int fromX, fromY, toX, toY;
        GameHelper.CalcPanelPosition(out fromX, out fromY, from);
        GameHelper.CalcPanelPosition(out toX, out toY, to);

        // GamePiece.CanMove と同じ：破壊マス・壁には行けない（翼状態の駒は破壊マスには行ける）＋駒の動きの形
        if (s.IsBlockedFor(piece, toX, toY)) return false;
        if (!PieceMoves.CanMoveByPattern(piece.type, piece.Owner, piece.promoted, toX - fromX, toY - fromY)) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight && IsPathBlocked(cells, s, blockers, fromX, fromY, toX, toY))
        {
            return false;
        }
        return true;
    }

    static bool IsBlocker(int[] blockers, int index)
    {
        if (blockers == null) return false;
        foreach (int b in blockers)
        {
            if (b == index) return true;
        }
        return false;
    }

    static bool IsPathBlocked(SnapshotPiece[] cells, BoardSnapshot s, int[] blockers, int fromX, int fromY, int toX, int toY)
    {
        if (BoardSimulator.IsPathBlocked(cells, s, cells[GameHelper.CalcPanelNum(fromX, fromY)], fromX, fromY, toX, toY)) return true;
        if (blockers == null) return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        if (System.Math.Abs(dx) <= 1 && System.Math.Abs(dy) <= 1) return false;

        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        int x = fromX + stepX;
        int y = fromY + stepY;
        for (int i = 0; i < 9 && (x != toX || y != toY); i++)
        {
            if (IsBlocker(blockers, GameHelper.CalcPanelNum(x, y))) return true;
            x += stepX;
            y += stepY;
        }
        return false;
    }

    static int CountKings(SnapshotPiece[] cells, PlayerType player)
    {
        int count = 0;
        foreach (SnapshotPiece piece in cells)
        {
            if (piece != null && piece.Owner == player && piece.type == PieceType.King) count++;
        }
        return count;
    }

    static int FindKing(SnapshotPiece[] cells, PlayerType player)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].Owner == player && cells[i].type == PieceType.King) return i;
        }
        return NONE;
    }
}
