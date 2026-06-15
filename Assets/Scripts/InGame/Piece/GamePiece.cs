using UnityEngine;

public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn}
public enum PlayerType { Player1, Player2 }
public abstract class GamePiece : MonoBehaviour
{
    public PieceType type;
    public PlayerType player;
    public int piecePoint;

    public int X { get; set; }
    public int Y { get; set; }

    public abstract bool CanMove(int toX, int toY);
    // 駒を移動させるメソッド
    public void MoveTo(Vector2 targetLocation)
    {
        transform.localPosition = targetLocation;
    }
}
