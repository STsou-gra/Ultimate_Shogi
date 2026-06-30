using UnityEngine;

public class PieceBishop : GamePiece
{
    public override bool CanMove(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 竜馬：斜めどこまでも ＋ 周囲1マス（上下左右4方向も1マスだけ動ける）
            return (Mathf.Abs(dx) == Mathf.Abs(dy)) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1);
        }

        // 角：斜めどこまでも
        return (Mathf.Abs(dx) == Mathf.Abs(dy));
    }
}