using UnityEngine;

public class PiecePawn : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;

        // プレイヤー1は上(-x)、2は下(+x)
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;

        return (dx == forwardX && dy == 0);
    }
}
