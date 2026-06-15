using UnityEngine;

public class PieceBishop : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        // 角：斜めどこまでも
        return (Mathf.Abs(dx) == Mathf.Abs(dy));
    }
}