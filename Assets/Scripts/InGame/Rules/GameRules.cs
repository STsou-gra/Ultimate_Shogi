using UnityEngine;
using System.Collections.Generic;

public interface IGameRule
{
    bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board);
    bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board);
}

/// <summary>
/// 駒自体の動き、進路上に障害物がないか、移動先に自分の駒がないかなどの基本移動ルール
/// </summary>
public class BaseMoveRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        // 基本の動き（各駒の子クラスの CanMove）
        if (!piece.CanMove(toX, toY)) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight)
        {
            if (GameHelper.IsPathBlocked(board.GetBoardData(), piece.X, piece.Y, toX, toY))
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
/// 王将が2枚以上ある間は、そのプレイヤーに王手・詰みは発生しない（1枚のときのみ適用）
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
        return IsInCheck(board.GetBoardData(), NONE, NONE, player);
    }

    // その移動の後に、自分の王将が取られる状態にならないか（詰みの判定で、王手を防げる手かを調べる）
    public static bool IsMoveSafe(GameBoard board, GamePiece piece, int toX, int toY)
    {
        GamePiece[] cells = board.GetBoardData();
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

        GamePiece[] sim = (GamePiece[])cells.Clone();
        sim[to] = piece;
        sim[from] = null;
        return !IsInCheck(sim, NONE, NONE, piece.player);
    }

    // 持ち駒を打った後に、自分の王将が取られる状態にならないか（詰みの判定で、合駒で王手を防げるかを調べる）
    public static bool IsDropSafe(GameBoard board, PlayerType player, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        if (CountKings(cells, player) != 1) return true;

        // 打った駒は利きの通り道をふさぐ「仮の駒」として扱う
        return !IsInCheck(cells, GameHelper.CalcPanelNum(x, y), NONE, player);
    }

    // 歩を打って相手を詰ませる手か（打ち歩詰め）
    public static bool IsUchifuzume(GameBoard board, PlayerType player, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        PlayerType opponent = Opponent(player);
        if (CountKings(cells, opponent) != 1) return false;

        // 打った歩のすぐ前に相手の王将がいなければ王手ではない
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;
        int front = GameHelper.CalcPanelNum(x + forwardX, y);
        if (front == NONE) return false;
        GamePiece king = cells[front];
        if (king == null || king.player != opponent || king.type != PieceType.King) return false;

        int pawn = GameHelper.CalcPanelNum(x, y);
        return !CanEscapePawnCheck(cells, pawn, front, opponent);
    }

    // 歩で王手された側が、駒を動かして王手を回避できるか
    // （歩の王手は王将に隣接しているので、持ち駒を打って防ぐことはできない）
    static bool CanEscapePawnCheck(GamePiece[] cells, int pawn, int pawnAttack, PlayerType defender)
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
                if (!CanAttack(cells, pawn, from, to)) continue;

                GamePiece[] sim = (GamePiece[])cells.Clone();
                sim[to] = piece;
                sim[from] = null;

                // 打った歩を取ったら、仮の歩は盤上から消える
                bool capturedPawn = (to == pawn);
                if (!IsInCheck(sim, capturedPawn ? NONE : pawn, capturedPawn ? NONE : pawnAttack, defender))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // blocker: 駒オブジェクトのない仮の駒（打つ駒）がいるマス
    // blockerAttack: その仮の駒が利きを持つマス（打ち歩詰めの判定で、歩が王将を狙うマス）
    static bool IsInCheck(GamePiece[] cells, int blocker, int blockerAttack, PlayerType player)
    {
        if (CountKings(cells, player) != 1) return false;

        int king = FindKing(cells, player);
        if (king == NONE) return false;
        if (king == blockerAttack) return true;

        PlayerType attacker = Opponent(player);
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].player == attacker && CanAttack(cells, blocker, i, king))
            {
                return true;
            }
        }
        return false;
    }

    // cells[from] の駒が to のマスに利いているか（駒の動きと、途中に駒がないか）
    static bool CanAttack(GamePiece[] cells, int blocker, int from, int to)
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
        if (piece.type != PieceType.Knight && IsPathBlocked(cells, blocker, fromX, fromY, toX, toY))
        {
            return false;
        }
        return true;
    }

    static bool IsPathBlocked(GamePiece[] cells, int blocker, int fromX, int fromY, int toX, int toY)
    {
        if (GameHelper.IsPathBlocked(cells, fromX, fromY, toX, toY)) return true;
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
