using System.Collections.Generic;
using UnityEngine;

// CPU の魔法スキルの編成（Prototype の CpuController.chooseLoadout）：ランクごとに、候補からランダムに選ぶ。
// 編成画面（DeckSelectController）が CPU の分をこれで決める（人の分の枠だけを画面に出す）
public static class CpuLoadout
{
    // 枠の順（0〜2 = 通常、3 = 必殺、4 = 超常。SkillRanks）の魔法。候補が足りない枠は null
    public static SkillDefinition[] Choose(IEnumerable<SkillDefinition> all, uint seed)
    {
        GameRng rng = new GameRng(seed);
        SkillDefinition[] slots = new SkillDefinition[SkillRanks.SlotCount];
        foreach (SkillRank rank in SkillRanks.All)
        {
            List<SkillDefinition> candidates = new List<SkillDefinition>();
            if (all != null)
            {
                foreach (SkillDefinition s in all)
                {
                    if (s != null && s.rank == rank && !candidates.Contains(s)) candidates.Add(s);
                }
            }
            List<SkillDefinition> picked = rng.Shuffle(candidates);
            int first = SkillRanks.FirstSlot(rank);
            for (int i = 0; i < SkillRanks.Pick(rank) && i < picked.Count; i++) slots[first + i] = picked[i];
        }
        return slots;
    }

    public static string Describe(SkillDefinition[] slots)
    {
        List<string> names = new List<string>();
        foreach (SkillDefinition s in slots)
        {
            if (s != null) names.Add(s.skillName);
        }
        return string.Join("・", names);
    }
}
