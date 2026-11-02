using UnityEngine;

// 必殺技：香「貫通槍」（Prototype の src/defs/specials/ky.js）
// 前方へ突き抜け、通過した相手の駒をすべて取る。味方の駒か盤端で止まる
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Lance Pierce (貫通槍)")]
public class LancePierceSpecial : SpecialMove
{
    public LancePierceSpecial()
    {
        SetDefaults("貫通槍", 2, 0x80d8ff, "前方へ突き抜け、通過した相手の駒をすべて取る。味方の駒か盤端で止まる。");
    }

    public override bool CanUse(SpecialContext context)
    {
        return SpecialKit.ChargePath(context, context.Caster, true).Count > 0;
    }

    public override void Apply(SpecialContext context)
    {
        SpecialKit.Charge(context, context.Caster, true);
    }
}
