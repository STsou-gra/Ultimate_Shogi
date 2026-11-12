using System.Collections.Generic;
using UnityEngine;

// 魔法スキルで使い回す判定（Prototype の src/defs/magic/kit.js の MagicKit）。
// Unity の盤は X が段（Player1 は +X へ進む）、Y が筋
public static class MagicKit
{
    // 自分の魔法の対象・起点に選べる自分の駒か（Prototype の ownPieceSquares）。
    // アンデッドは選べない（相手の魔法・駒の効果は通常どおり受ける）
    public static bool IsOwnPiece(SkillContext context, GamePiece piece)
    {
        return piece != null && piece.player == context.Player && !piece.isUndead;
    }

    // 駒がいない通常状態（破壊・炎上・落とし穴がない）のマスか（Prototype の emptyNormalSquares・kit.js の空きマス）
    public static bool IsEmptyNormal(GameBoard board, Vector2Int pos)
    {
        if (!BoardCells.InBounds(pos.x, pos.y) || board.HasPieceAt(pos.x, pos.y)) return false;
        // 時間停止中、予定の駒が残っているマス・マスの変化の予定があるマスは空きマスにしない
        TimeStopState ts = board.TimeStop;
        if (ts != null && (ts.IsHeldSquare(pos.x, pos.y) || ts.HasPendingCell(pos.x, pos.y))) return false;
        BoardCell cell = board.Cells.At(pos.x, pos.y);
        return cell != null && cell.State == CellState.Normal && !cell.HasTrap;
    }

    // 自陣の、駒がいない通常状態（落とし穴なし）のマス（Prototype の ownZoneEmptySquares。アンデッド・パラダイスの復活先）
    public static List<Vector2Int> OwnZoneEmptySquares(GameBoard board, PlayerType owner)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            if (!BoardZones.IsOwnZone(owner, x)) continue;
            for (int y = 0; y < BoardCells.Size; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (IsEmptyNormal(board, pos)) result.Add(pos);
            }
        }
        return result;
    }

    // 倒された自分の初期配置の駒のうち、復活できるもの（Prototype の revivableFallen。王将は対象外）。倒された順
    public static List<PieceType> RevivableFallen(MatchState match, PlayerType owner)
    {
        List<PieceType> result = new List<PieceType>();
        if (match == null) return result;
        foreach (FallenPieceRecord f in match.FallenInitial(owner))
        {
            if (f.Type != PieceType.King) result.Add(f.Type);
        }
        return result;
    }

    // 駒 p の前方 1×3 マス（左前・前・右前）。盤外・破壊マス（駒のいない）は除く（Prototype の frontSquares）
    public static List<Vector2Int> FrontSquares(GameBoard board, GamePiece p)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        if (p == null) return result;
        int x = p.X + SpecialContext.Forward(p.player);
        for (int y = p.Y - 1; y <= p.Y + 1; y++)
        {
            // 破壊マスは除く。ただし破壊マスの上の駒（翼状態の駒）は残す（剣・槍は届く）
            if (!BoardCells.InBounds(x, y) || (board.Cells.IsBroken(x, y) && !board.HasPieceAt(x, y))) continue;
            result.Add(new Vector2Int(x, y));
        }
        return result;
    }

    // 駒 p の前方 1×3 マスにいる相手の駒（Prototype の frontEnemies）
    public static List<GamePiece> FrontEnemies(GameBoard board, GamePiece p)
    {
        List<GamePiece> result = new List<GamePiece>();
        foreach (Vector2Int s in FrontSquares(board, p))
        {
            GamePiece q = board.GetPieceAt(s.x, s.y);
            if (q != null && q.player != p.player) result.Add(q);
        }
        return result;
    }

    // 帯状の範囲（Prototype の band。範囲を選ぶ魔法用）：
    // horizontal なら 9 筋 × 3 段（x を中央の段とする連続 3 段）、そうでなければ 3 筋 × 9 段（y を中央の筋とする連続 3 筋）。
    // 盤の端では、盤からはみ出さないように内側へずらす
    public static List<Vector2Int> Band(int x, int y, bool horizontal)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        int mx = Mathf.Clamp(x, 1, 7);
        int my = Mathf.Clamp(y, 1, 7);
        for (int a = 0; a < 9; a++)
        {
            for (int b = -1; b <= 1; b++)
            {
                result.Add(horizontal ? new Vector2Int(mx + b, a) : new Vector2Int(a, my + b));
            }
        }
        return result;
    }

    // (cx, cy) を中心とした (2r+1)×(2r+1) のうち盤内のマス（Prototype の squareArea）
    public static List<Vector2Int> SquareArea(int cx, int cy, int r = 1)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (BoardCells.InBounds(x, y)) result.Add(new Vector2Int(x, y));
            }
        }
        return result;
    }

    // マスの一覧のうち、どちらかの王将がいるマス（Prototype の kingSquares。王将を巻き込めない範囲魔法の blockers 用）
    public static List<Vector2Int> KingSquares(GameBoard board, List<Vector2Int> cells)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        foreach (Vector2Int c in cells)
        {
            GamePiece q = board.GetPieceAt(c.x, c.y);
            if (q != null && q.type == PieceType.King) result.Add(c);
        }
        return result;
    }
}
