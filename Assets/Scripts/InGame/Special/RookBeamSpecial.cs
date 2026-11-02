using UnityEngine;

// 必殺技：飛「龍王旋風」（Prototype の src/defs/specials/hi.js）
// 縦横 4 方向に衝撃波を放ち、各方向で最初に当たった相手の駒を取る（火薬なら爆発。味方の駒・破壊マスで止まる）
[CreateAssetMenu(menuName = "UltimateShogi/Special Moves/Rook Beam (龍王旋風)")]
public class RookBeamSpecial : SpecialMove
{
    public RookBeamSpecial()
    {
        SetDefaults("龍王旋風", 3, 0x69f0ae, "縦横4方向に衝撃波を放ち、各方向で最初に当たった相手の駒を取る。");
    }

    public override bool CanUse(SpecialContext context)
    {
        return SpecialKit.AnyBeamHits(context, context.Caster, SpecialKit.Orthogonal);
    }

    public override void Apply(SpecialContext context)
    {
        SpecialKit.FireBeams(context, context.Caster, SpecialKit.Orthogonal, color);
    }
}
