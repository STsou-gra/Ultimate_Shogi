using UnityEngine;

// 火薬（Prototype の src/defs/pieces.js の KAYAKU）
// 自分では動けない。敵味方に関係なく、何かの駒が火薬のマスに入ると爆発し、周囲 3×3 の駒を消失させる（GameSystem.Explode）。
// 取っても持ち駒にならない（PieceRules.GoesToHand）。プレハブで hasFuse をオンにして、導火線の火花を出す
public class PieceGunpowder : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        return false;
    }
}
