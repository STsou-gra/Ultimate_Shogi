using UnityEngine;

// 必殺技：角「光芒四射」（Prototype の src/defs/specials/ka.js）
// 斜め 4 方向に光線を放ち、各方向で最初に当たった相手の駒を取る（火薬なら爆発。味方の駒・破壊マス・壁で止まる）
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Bishop Beam (光芒四射)")]
public class BishopBeamSpecial : SpecialMove
{
    public BishopBeamSpecial()
    {
        SetDefaults("光芒四射", 3, 0xea80fc, "斜め4方向に光線を放ち、各方向で最初に当たった相手の駒を取る。");
    }

    public override bool CanUse(SpecialContext context)
    {
        return SpecialKit.AnyBeamHits(context, context.Caster, SpecialKit.Diagonal);
    }

    public override void Apply(SpecialContext context)
    {
        SpecialKit.FireBeams(context, context.Caster, SpecialKit.Diagonal, color);
    }
}
