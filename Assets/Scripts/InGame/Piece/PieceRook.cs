using UnityEngine;

public class PieceRook : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 竜王：縦横どこまでも ＋ 周囲1マス（斜め4方向も1マスだけ動ける）
            return (dx == 0 || dy == 0) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1);
        }

        // 飛：縦横どこまでも
        return (dx == 0 || dy == 0);
    }
}
