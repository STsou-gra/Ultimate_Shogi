using System.Collections.Generic;

// 王手・打ち歩詰めの判定を、盤面のスナップショット（BoardSnapshot）の上で行う（Rules/GameRules.cs の CheckRules を移したもの）。
// 判定の中身は CheckRules と同じにしている（王将が 2 枚以上なら王手・詰みはない、走り駒の利きは破壊・炎上・落とし穴で止まる、
// 火薬に入る手は爆発まで読む、狙撃手がいれば打った歩を撃ち抜ける など）。CheckRules を変えたら、こちらも同じように変えること
public static class SnapshotRules
{
    const int NONE = -1;

    // player が王手されているか（CheckRules.IsInCheck）
    public static bool IsInCheck(BoardSnapshot s, PlayerType player)
    {
        return IsInCheck(s.Grid, s, NONE, NONE, player);
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

    // 歩で王手された側が、駒を動かして王手を回避できるか
    static bool CanEscapePawnCheck(SnapshotPiece[] cells, BoardSnapshot s, int pawn, int pawnAttack, PlayerType defender)
    {
        for (int from = 0; from < cells.Length; from++)
        {
            SnapshotPiece piece = cells[from];
            if (piece == null || piece.Owner != defender || piece.undead) continue; // アンデッドは持ち主が動かせない

            // 狙撃手がいれば、打った歩を撃ち抜いて王手を外せる
            if (PieceRules.IsSniper(piece.type)) return true;

            for (int to = 0; to < cells.Length; to++)
            {
                if (to == from) continue;
                SnapshotPiece target = cells[to];
                bool intoBomb = target != null && PieceRules.IsExplosive(target.type);
                if (target != null && target.Owner == defender && !intoBomb) continue;
                if (!CanAttack(cells, s, pawn, from, to)) continue;

                // 王将が炎上・落とし穴のマスへ逃げても消失するだけなので、回避にならない
                int toX, toY;
                GameHelper.CalcPanelPosition(out toX, out toY, to);
                if (piece.type == PieceType.King && s.IsHazard(toX, toY)) continue;

                SnapshotPiece[] sim = SimulateMove(cells, s, piece, from, to);

                // 打った歩を取ったら（火薬の爆発に巻き込んだら）、仮の歩は盤上から消える
                int pawnX, pawnY;
                GameHelper.CalcPanelPosition(out pawnX, out pawnY, pawn);
                bool capturedPawn = (to == pawn) || (intoBomb && PieceRules.IsInExplosion(toX, toY, pawnX, pawnY));
                if (!IsInCheck(sim, s, capturedPawn ? NONE : pawn, capturedPawn ? NONE : pawnAttack, defender))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // 配列をコピーして、from の駒を to へ動かした後の配列を作る（炎上・落とし穴のマスに入った駒は消失する。火薬なら爆発させる）
    static SnapshotPiece[] SimulateMove(SnapshotPiece[] cells, BoardSnapshot s, SnapshotPiece piece, int from, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        SnapshotPiece[] sim = (SnapshotPiece[])cells.Clone();
        SnapshotPiece target = sim[to];
        sim[from] = null;
        bool landed = true;
        if (target != null && target != piece && PieceRules.IsExplosive(target.type))
        {
            Explode(sim, toX, toY);
            landed = !PieceRules.IsDestroyedByExplosion(piece.type) && sim[to] == null;
        }
        if (landed) sim[to] = s.IsHazard(toX, toY) ? null : piece;
        return sim;
    }

    // 配列の上で爆発させる（GameSystem.Explode と同じ範囲・同じ王将の扱い）
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
                if (MatchConfig.ChainExplosion && PieceRules.IsExplosive(sim[i].type) && (x != cx || y != cy)) chain.Add((x, y));
                sim[i] = null;
            }
        }
        foreach ((int x, int y) c in chain) Explode(sim, c.x, c.y);
    }

    // blocker: 仮の駒（打つ駒）がいるマス／blockerAttack: その仮の駒が利きを持つマス
    static bool IsInCheck(SnapshotPiece[] cells, BoardSnapshot s, int blocker, int blockerAttack, PlayerType player)
    {
        if (CountKings(cells, player) != 1) return false;

        int king = FindKing(cells, player);
        if (king == NONE) return false;
        if (king == blockerAttack) return true;

        PlayerType attacker = BoardZones.Opponent(player);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].Owner == attacker && CanAttack(cells, s, blocker, i, king))
            {
                return true;
            }
        }
        return false;
    }

    // cells[from] の駒が to のマスに利いているか（駒の動き・破壊マス・途中の駒やマス）
    static bool CanAttack(SnapshotPiece[] cells, BoardSnapshot s, int blocker, int from, int to)
    {
        SnapshotPiece piece = cells[from];
        if (piece == null || from == to) return false;

        int fromX, fromY, toX, toY;
        GameHelper.CalcPanelPosition(out fromX, out fromY, from);
        GameHelper.CalcPanelPosition(out toX, out toY, to);

        // GamePiece.CanMove と同じ：破壊マスには行けない＋駒の動きの形
        if (s.IsBroken(toX, toY)) return false;
        if (!PieceMoves.CanMoveByPattern(piece.type, piece.Owner, piece.promoted, toX - fromX, toY - fromY)) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight && IsPathBlocked(cells, s, blocker, fromX, fromY, toX, toY))
        {
            return false;
        }
        return true;
    }

    static bool IsPathBlocked(SnapshotPiece[] cells, BoardSnapshot s, int blocker, int fromX, int fromY, int toX, int toY)
    {
        if (BoardSimulator.IsPathBlocked(cells, s, fromX, fromY, toX, toY)) return true;
        if (blocker == NONE) return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        if (System.Math.Abs(dx) <= 1 && System.Math.Abs(dy) <= 1) return false;

        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        int x = fromX + stepX;
        int y = fromY + stepY;
        for (int i = 0; i < 9 && (x != toX || y != toY); i++)
        {
            if (GameHelper.CalcPanelNum(x, y) == blocker) return true;
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
