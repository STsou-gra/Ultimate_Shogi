using UnityEngine;

// 必殺技：歩「突撃」（Prototype の src/defs/specials/fu.js）
// 前方へ一直線に限界まで突き進み、最初に当たった相手の駒を取ってそのマスで止まる。
// 味方の駒・破壊マス・壁の手前で止まる。炎上・落とし穴に入ると消失、火薬に突っ込むと爆発
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Pawn Charge (突撃)")]
public class PawnChargeSpecial : SpecialMove
{
    public PawnChargeSpecial()
    {
        SetDefaults("突撃", 2, 0xff7043, "前方へ一直線に限界まで突き進み、最初に当たった相手の駒を取ってそのマスで止まる。");
    }

    public override bool CanUse(SpecialContext context)
    {
        return SpecialKit.ChargePath(context, context.Caster, false).Count > 0;
    }

    public override void Apply(SpecialContext context)
    {
        SpecialKit.Charge(context, context.Caster, false);
    }
}
