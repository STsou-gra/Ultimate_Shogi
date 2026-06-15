using UnityEngine;

public class PieceLance : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 香：前どこまでも（本来は間に駒がないか判定が必要ですが、まずは方向だけ）
        return (forwardX < 0 && forwardY == 0);
    }
}
