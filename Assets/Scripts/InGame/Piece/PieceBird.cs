using UnityEngine;

// 鳥（Prototype の src/defs/pieces.js の TORI）
// 前の 1×3 マス（左前・前・右前）にしか動けず、成らない。常に翼状態（PieceRules.IsWingType）：
// マスの効果を受けない（破壊マスにも入れる・打てる、炎上・落とし穴のマスに入っても消失しない（落とし穴は残る））。
// 火薬のマスに入っても爆発させず、火薬を取る（味方の火薬のマスには入れない。PieceRules.Detonates）。
// いるマスが破壊されると消失する（魔法「翼」の駒とは違う）。爆発・隕石・破壊砲などの消失は防げない。取られると相手の持ち駒になる。
// 左右の白い翼は GameTickEffects が付ける（羽ばたく）
public class PieceBird : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        int dx = toX - X;
        int dy = toY - Y;
        int forwardX = (player == PlayerType.Player1) ? 1 : -1; // Player1 は +X が前
        return dx == forwardX && Mathf.Abs(dy) <= 1;
    }
}
