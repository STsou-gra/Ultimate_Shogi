using UnityEngine;

public class PieceRook : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        // 飛：縦横どこまでも
        return (dx == 0 || dy == 0);
    }
}
