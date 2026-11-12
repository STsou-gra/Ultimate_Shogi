// 盤面のスナップショット（BoardSnapshot）で、必殺技・魔法を GameObject なしで判定・実行するための「種類」と「編成した魔法」。
// GameSystem.CaptureSnapshot が、SpecialMove・SkillEffect のクラスから種類を決めて入れる（GameSystem.Snapshot.cs の SpecialKindOf・MagicKindOf）。
// 判定・効果の中身は BoardSimulator.Skills.cs（元のクラスを変えたら、そちらも同じように変えること）
//
// 種類が Unknown（BoardSimulator が知らない技・魔法）のものがあるスナップショットは、必殺技・魔法の合法手を
// 作った局面の skillActions でしか分からない（行動を当てはめたあとは、その技・魔法は使えないものとして扱う）

// 必殺技の種類（Special/*.cs の 1 クラスに 1 つ）
public enum SpecialKind
{
    None = 0,      // 必殺技なし
    PawnCharge,    // 歩「突撃」（PawnChargeSpecial）
    LancePierce,   // 香「貫通槍」（LancePierceSpecial）
    KnightLeap,    // 桂「天馬跳躍」（KnightLeapSpecial）
    GoldShock,     // 金「金剛衝」（GoldShockSpecial）
    BishopBeam,    // 角「光芒四射」（BishopBeamSpecial）
    RookBeam,      // 飛「龍王旋風」（RookBeamSpecial）
    Unknown,       // BoardSimulator が知らない技
}

// 魔法の種類（Skill/SkillData/*Effect.cs の 1 クラスに 1 つ）
public enum MagicKind
{
    Unknown = 0,
    Shield,         // 盾（ShieldEffect）
    Sword,          // 剣（SwordEffect）
    Pitfall,        // 落とし穴（PitfallEffect）
    DoubleTurn,     // 2回行動（DoubleTurnEffect）
    Swap,           // 駒入替（SwapPieceEffect）
    Explosion,      // 爆発（ExplosionEffect）
    ReplicateKing,  // 王将複製（ReplicateKingEffect）
    Phalanx,        // ファランクス（PhalanxEffect）
    Meteor,         // 隕石（MeteorEffect）
    Cannon,         // 破壊砲（CannonEffect）
    Cataclysm,      // 天変地異（CataclysmEffect）
    WorldEnd,       // 世界の終わり（WorldEndEffect）
    UndeadParadise, // アンデッド・パラダイス（UndeadParadiseEffect）
    TimeStop,       // 停止世界（TimeStopEffect）
}

// 編成した魔法 1 つ分（SkillDefinition のうち、判定・効果に使う値）
[System.Serializable]
public class SnapshotSkill
{
    public string id = "";         // SkillDefinition.Id（行動の skill）
    public MagicKind kind;
    public int cost;
    public bool transcendent;      // 超常魔法（1 対局に 1 回・解放条件あり）
    public bool endsTurn;          // SkillDefinition.EndsTurn（手番を使う）

    public SnapshotSkill Clone()
    {
        return (SnapshotSkill)MemberwiseClone();
    }

    // 対象を選ぶ数（SkillEffect.TargetCount と同じ）
    public int TargetCount
    {
        get
        {
            switch (kind)
            {
                case MagicKind.Shield:
                case MagicKind.Sword:
                case MagicKind.Pitfall:
                case MagicKind.Explosion:
                case MagicKind.Phalanx:
                case MagicKind.Meteor:
                case MagicKind.Cannon:
                    return 1;
                case MagicKind.Swap:
                    return 2;
                default:
                    return 0;
            }
        }
    }

    // 範囲の向きの数（SkillEffect.OrientCount。今の魔法はすべて 1）
    public int OrientCount => 1;
}
