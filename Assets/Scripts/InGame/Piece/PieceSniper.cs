using UnityEngine;

// 狙撃手（Prototype の src/defs/pieces.js の SNIPER）
// 自分では動けず、成らない。自分の手番に「狙撃」すると、王将以外の相手の駒を盤上のどこからでも取れる
// （射程無制限・間の駒は無視。GameSystem の狙撃モード）。取られると相手の持ち駒になる
public class PieceSniper : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        // 動きの形は PieceMoves（GameObject に依存しない盤面のシミュレーションと同じ判定）
        return PieceMoves.CanMoveByPattern(type, player, isPromoted, toX - X, toY - Y);
    }
}
