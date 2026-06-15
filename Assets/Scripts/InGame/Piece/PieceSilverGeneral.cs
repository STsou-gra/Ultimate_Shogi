using UnityEngine;

public class PieceSilverGeneral : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 銀：前3方向 ＋ 斜め後ろ2方向
        return (forwardX == -1 && Mathf.Abs(forwardY) <= 1) || (forwardX == 1 && Mathf.Abs(forwardY) == 1);
    }
}
