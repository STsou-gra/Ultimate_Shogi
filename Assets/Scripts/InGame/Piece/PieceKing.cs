using UnityEngine;

public class PieceKing : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        int dx =  Mathf.Abs(toX - X);
        int dy = Mathf.Abs(toY - Y);
        // 全方向に1マス以内
        return (dx <= 1 && dy <= 1);
    }
}
