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
