using UnityEngine;

public class PieceKnight : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 桂：2つ前、左右1つ
        return (forwardX == -2 && Mathf.Abs(forwardY) == 1);
    }
}
