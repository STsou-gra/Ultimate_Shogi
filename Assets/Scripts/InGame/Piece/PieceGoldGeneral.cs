using UnityEngine;

public class PieceGoldGeneral : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        // 金：前後左右4方向 ＋ 斜め前2方向
        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        return (Mathf.Abs(forwardX) <= 1 && Mathf.Abs(forwardY) <= 1) && !(forwardX == 1 && Mathf.Abs(forwardY) == 1);
    }
}
