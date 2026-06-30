using UnityEngine;

public class PieceSilverGeneral : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 成銀：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 銀：前3方向 ＋ 斜め後ろ2方向
        return (forwardX == -1 && Mathf.Abs(forwardY) <= 1) || (forwardX == 1 && Mathf.Abs(forwardY) == 1);
    }
}
