using UnityEngine;

public class PiecePawn : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        // 動きの形は PieceMoves（GameObject に依存しない盤面のシミュレーションと同じ判定）
        return PieceMoves.CanMoveByPattern(type, player, isPromoted, toX - X, toY - Y);
    }
}
