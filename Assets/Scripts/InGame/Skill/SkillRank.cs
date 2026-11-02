using UnityEngine;

// 魔法スキルのランク（Prototype の src/defs/magic/tiers.js の MAGIC_TIERS）
//  通常（normal）/ 必殺（ultimate）/ 超常（transcendent）
// 対局前の編成で、ランクごとに Pick 個ずつ選ぶ（候補が Pick 個以下なら全部が自動で入る）。
// 並び順（enum の順）が、編成画面の枠・対局中のスキルボタンの並び順になる
public enum SkillRank { Normal, Ultimate, Transcendent }

public static class SkillRanks
{
    public static readonly SkillRank[] All = { SkillRank.Normal, SkillRank.Ultimate, SkillRank.Transcendent };

    // 編成の枠の数（通常 3 + 必殺 1 + 超常 1）。対局中のスキルボタン（GameSystem の skillSlotUIs）も同じ数
    public const int SlotCount = 5;

    // そのランクから選ぶ数
    public static int Pick(SkillRank rank)
    {
        return rank == SkillRank.Normal ? 3 : 1;
    }

    public static string Label(SkillRank rank)
    {
        switch (rank)
        {
            case SkillRank.Ultimate: return "必殺魔法";
            case SkillRank.Transcendent: return "超常魔法";
            default: return "通常魔法";
        }
    }

    public static string ShortLabel(SkillRank rank)
    {
        switch (rank)
        {
            case SkillRank.Ultimate: return "必殺";
            case SkillRank.Transcendent: return "超常";
            default: return "通常";
        }
    }

    // 枠の色（Prototype の MAGIC_TIERS の css）
    public static Color ColorOf(SkillRank rank)
    {
        switch (rank)
        {
            case SkillRank.Ultimate: return Hex(0xffd54a);
            case SkillRank.Transcendent: return Hex(0xea80fc);
            default: return Hex(0xb3e5fc);
        }
    }

    // デッキの枠 slot（0 始まり）のランク：0〜2 = 通常、3 = 必殺、4 = 超常
    public static SkillRank RankOfSlot(int slot)
    {
        int first = 0;
        foreach (SkillRank rank in All)
        {
            if (slot < first + Pick(rank)) return rank;
            first += Pick(rank);
        }
        return SkillRank.Transcendent;
    }

    // そのランクの最初の枠
    public static int FirstSlot(SkillRank rank)
    {
        int first = 0;
        foreach (SkillRank r in All)
        {
            if (r == rank) return first;
            first += Pick(r);
        }
        return first;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
