using UnityEngine;
using System.Collections.Generic;

public interface IGameRule
{
    bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board);
    bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board);
}

/// <summary>
/// 駒自体の動き、進路上に障害物がないか、移動先に自分の駒がないかなどの基本移動ルール。
/// 破壊マスは移動先にも通過にもできない（移動先は各駒の CanMove、通り道は IsPathBlocked で判定）。
/// 炎上・落とし穴のマスは移動先に選べる（入った駒は消失する）が、走り駒はそこで止まる
/// </summary>
public class BaseMoveRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        // 基本の動き（各駒の CanMove。破壊マスには行けない）
        if (!piece.CanMove(toX, toY)) return false;

        // 障害物チェック（桂馬以外）：途中の駒・破壊マス・炎上・落とし穴
        if (piece.type != PieceType.Knight)
        {
            if (GameHelper.IsPathBlocked(board.GetBoardData(), board.Cells, piece.X, piece.Y, toX, toY))
            {
                return false;
            }
        }

        // 移動先に自分の駒があるか
        GamePiece targetPiece = board.GetPieceAt(toX, toY);
        if (targetPiece != null)
        {
            if (targetPiece.player == piece.player)
            {
                return false; // 自分の駒があるので移動できない
            }
        }
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        return true; // 移動ルールなのでDrop判定は常にパス
    }
}

/// <summary>
/// すでに駒が置かれているマスには持ち駒を打てないルール
/// </summary>
public class EmptyCellDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        return !board.HasPieceAt(toX, toY);
    }
}

/// <summary>
/// 破壊マス・落とし穴のマスには持ち駒を打てないルール（Prototype の moves.js の getDropSquares）。
/// 炎上のマスには打てる（打った駒はすぐ消失する）
/// </summary>
public class CellStateDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        BoardCell cell = board.Cells.At(toX, toY);
        return cell != null && !cell.IsBroken && !cell.HasTrap;
    }
}

/// <summary>
/// 歩・香車・桂馬が盤外に出て進めなくなる位置に打つことを禁止するルール
/// </summary>
public class NoLegalMoveDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (type == PieceType.Pawn || type == PieceType.Lance)
        {
            // 先手 (Player1, 前進方向は +X) -> X = 8 は一番奥なので打てない
            if (player == PlayerType.Player1 && toX == 8)
            {
                return false;
            }
            // 後手 (Player2, 前進方向は -X) -> X = 0 は一番奥なので打てない
            if (player == PlayerType.Player2 && toX == 0)
            {
                return false;
            }
        }
        else if (type == PieceType.Knight)
        {
            // 先手 -> X >= 7 (7, 8) は桂馬の行き所がないので打てない
            if (player == PlayerType.Player1 && toX >= 7)
            {
                return false;
            }
            // 後手 -> X <= 1 (0, 1) は桂馬の行き所がないので打てない
            if (player == PlayerType.Player2 && toX <= 1)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 同じ縦列（筋）に自分の歩がすでにある場合、新たに歩を打つことを禁止するルール (二歩)
/// </summary>
public class NifuDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (type == PieceType.Pawn)
        {
            for (int x = 0; x < 9; x++)
            {
                GamePiece piece = board.GetPieceAt(x, toY);
                if (piece != null && piece.player == player && piece.type == PieceType.Pawn)
                {
                    return false;
                }
            }
        }
        return true;
    }
}

/// <summary>
/// 序盤（第 MatchConfig.EnemyZoneDropLockTurns ターンまで）は、敵陣（相手側の3段）に持ち駒を打てないルール
/// </summary>
public class EnemyZoneDropLockRule : IGameRule
{
    private readonly TurnState turn;

    public EnemyZoneDropLockRule(TurnState turn)
    {
        this.turn = turn;
    }

    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        // 段は X（BoardZones の説明を参照）
        return !(turn.IsEnemyZoneDropLocked && BoardZones.IsEnemyZone(player, toX));
    }
}

/// <summary>
/// 打ち歩詰めの禁止（相手の王将が1枚だけのときに適用）
/// 王手放置や、王将を取られるマスへの移動は禁止しない（放置したら次の手で王将を取られて負け）
/// </summary>
public class UchifuzumeRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        return !(type == PieceType.Pawn && CheckRules.IsUchifuzume(board, player, toX, toY));
    }
}

/// <summary>
/// 王手・打ち歩詰めの判定。実際の駒は動かさず、盤面の配列をコピーして「動かした後」を調べる。
/// 王将が2枚以上ある間は、そのプレイヤーに王手・詰みは発生しない（1枚のときのみ適用）。
/// マスの状態も見る：走り駒の利きは破壊マス・炎上・落とし穴で止まり、炎上・落とし穴に入った駒・打った駒は消失する
/// </summary>
public static class CheckRules
{
    const int NONE = -1;

    public static PlayerType Opponent(PlayerType player)
    {
        return (player == PlayerType.Player1) ? PlayerType.Player2 : PlayerType.Player1;
    }

    // 指定プレイヤーが王手されているか
    public static bool IsInCheck(GameBoard board, PlayerType player)
    {
        return IsInCheck(board.GetBoardData(), board.Cells, NONE, NONE, player);
    }

    // その移動の後に、自分の王将が取られる状態にならないか（詰みの判定で、王手を防げる手かを調べる）
    public static bool IsMoveSafe(GameBoard board, GamePiece piece, int toX, int toY)
    {
        GamePiece[] cells = board.GetBoardData();
        BoardCells cellStates = board.Cells;
        if (CountKings(cells, piece.player) != 1) return true;

        int from = GameHelper.CalcPanelNum(piece.X, piece.Y);
        int to = GameHelper.CalcPanelNum(toX, toY);
        if (from == NONE || to == NONE) return true;

        // 相手の最後の王将を取る手は、その時点で勝ちなので指せる
        GamePiece target = cells[to];
        if (target != null && target.player != piece.player && target.type == PieceType.King && CountKings(cells, target.player) == 1)
        {
            return true;
        }

        // 最後の王将が炎上・落とし穴のマスに入ると消失して負けになる
        if (piece.type == PieceType.King && cellStates.IsHazard(toX, toY)) return false;

        GamePiece[] sim = SimulateMove(cells, cellStates, piece, from, to);
        return !IsInCheck(sim, cellStates, NONE, NONE, piece.player);
    }

    // 持ち駒を打った後に、自分の王将が取られる状態にならないか（詰みの判定で、合駒で王手を防げるかを調べる）
    public static bool IsDropSafe(GameBoard board, PlayerType player, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        if (CountKings(cells, player) != 1) return true;

        // 炎上のマスに打った駒はすぐ消失するので、通り道をふさげない
        if (board.Cells.IsHazard(x, y)) return !IsInCheck(cells, board.Cells, NONE, NONE, player);

        // 打った駒は利きの通り道をふさぐ「仮の駒」として扱う
        return !IsInCheck(cells, board.Cells, GameHelper.CalcPanelNum(x, y), NONE, player);
    }

    // 歩を打って相手を詰ませる手か（打ち歩詰め）
    public static bool IsUchifuzume(GameBoard board, PlayerType player, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        BoardCells cellStates = board.Cells;
        PlayerType opponent = Opponent(player);
        if (CountKings(cells, opponent) != 1) return false;

        // 炎上のマスに打った歩はすぐ消失するので、王手にならない
        if (cellStates.IsHazard(x, y)) return false;

        // 打った歩のすぐ前に相手の王将がいなければ王手ではない
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;
        int front = GameHelper.CalcPanelNum(x + forwardX, y);
        if (front == NONE) return false;
        GamePiece king = cells[front];
        if (king == null || king.player != opponent || king.type != PieceType.King) return false;

        int pawn = GameHelper.CalcPanelNum(x, y);
        return !CanEscapePawnCheck(cells, cellStates, pawn, front, opponent);
    }

    // 歩で王手された側が、駒を動かして王手を回避できるか
    // （歩の王手は王将に隣接しているので、持ち駒を打って防ぐことはできない）
    static bool CanEscapePawnCheck(GamePiece[] cells, BoardCells cellStates, int pawn, int pawnAttack, PlayerType defender)
    {
        for (int from = 0; from < cells.Length; from++)
        {
            GamePiece piece = cells[from];
            if (piece == null || piece.player != defender) continue;

            for (int to = 0; to < cells.Length; to++)
            {
                if (to == from) continue;
                GamePiece target = cells[to];
                if (target != null && target.player == defender) continue;
                if (!CanAttack(cells, cellStates, pawn, from, to)) continue;

                // 王将が炎上・落とし穴のマスへ逃げても消失するだけなので、回避にならない
                int toX, toY;
                GameHelper.CalcPanelPosition(out toX, out toY, to);
                if (piece.type == PieceType.King && cellStates.IsHazard(toX, toY)) continue;

                GamePiece[] sim = SimulateMove(cells, cellStates, piece, from, to);

                // 打った歩を取ったら、仮の歩は盤上から消える
                bool capturedPawn = (to == pawn);
                if (!IsInCheck(sim, cellStates, capturedPawn ? NONE : pawn, capturedPawn ? NONE : pawnAttack, defender))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // 盤面の配列をコピーして、from の駒を to へ動かした後の配列を作る（炎上・落とし穴のマスに入った駒は消失する）
    static GamePiece[] SimulateMove(GamePiece[] cells, BoardCells cellStates, GamePiece piece, int from, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        GamePiece[] sim = (GamePiece[])cells.Clone();
        sim[to] = (cellStates != null && cellStates.IsHazard(toX, toY)) ? null : piece;
        sim[from] = null;
        return sim;
    }

    // blocker: 駒オブジェクトのない仮の駒（打つ駒）がいるマス
    // blockerAttack: その仮の駒が利きを持つマス（打ち歩詰めの判定で、歩が王将を狙うマス）
    static bool IsInCheck(GamePiece[] cells, BoardCells cellStates, int blocker, int blockerAttack, PlayerType player)
    {
        if (CountKings(cells, player) != 1) return false;

        int king = FindKing(cells, player);
        if (king == NONE) return false;
        if (king == blockerAttack) return true;

        PlayerType attacker = Opponent(player);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].player == attacker && CanAttack(cells, cellStates, blocker, i, king))
            {
                return true;
            }
        }
        return false;
    }

    // cells[from] の駒が to のマスに利いているか（駒の動きと、途中に駒がないか）
    static bool CanAttack(GamePiece[] cells, BoardCells cellStates, int blocker, int from, int to)
    {
        GamePiece piece = cells[from];
        if (piece == null || from == to) return false;

        int fromX, fromY, toX, toY;
        GameHelper.CalcPanelPosition(out fromX, out fromY, from);
        GameHelper.CalcPanelPosition(out toX, out toY, to);

        // CanMove は駒自身の座標を使うので、一時的に配列上の位置に合わせる
        int savedX = piece.X;
        int savedY = piece.Y;
        piece.X = fromX;
        piece.Y = fromY;
        bool canMove = piece.CanMove(toX, toY);
        piece.X = savedX;
        piece.Y = savedY;
        if (!canMove) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight && IsPathBlocked(cells, cellStates, blocker, fromX, fromY, toX, toY))
        {
            return false;
        }
        return true;
    }

    static bool IsPathBlocked(GamePiece[] cells, BoardCells cellStates, int blocker, int fromX, int fromY, int toX, int toY)
    {
        if (GameHelper.IsPathBlocked(cells, cellStates, fromX, fromY, toX, toY)) return true;
        if (blocker == NONE) return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) return false;

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

    static int CountKings(GamePiece[] cells, PlayerType player)
    {
        int count = 0;
        foreach (GamePiece piece in cells)
        {
            if (piece != null && piece.player == player && piece.type == PieceType.King) count++;
        }
        return count;
    }

    static int FindKing(GamePiece[] cells, PlayerType player)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].player == player && cells[i].type == PieceType.King) return i;
        }
        return NONE;
    }
}
