using UnityEngine;

public class PieceLance : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 成香：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 香：前どこまでも（本来は間に駒がないか判定が必要ですが、まずは方向だけ）
        return (forwardX < 0 && forwardY == 0);
    }
}
