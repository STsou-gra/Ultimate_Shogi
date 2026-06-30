using UnityEngine;

public class PiecePawn : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;

        // プレイヤー1は上(-x)、2は下(+x)
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;

        if (isPromoted)
        {
            // と金（成金）：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        return (dx == forwardX && dy == 0);
    }
}
