using System.Collections.Generic;
using UnityEngine;

// 1 人分の魔法スキルの編成（Prototype の loadout）。
// 並びは枠の順（SkillRanks.RankOfSlot）：0〜2 = 通常魔法、3 = 必殺魔法、4 = 超常魔法。空き枠は null
[CreateAssetMenu(menuName = "UltimateShogi/Skill Deck")]
public class SkillDeck : ScriptableObject
{
    public List<SkillDefinition> skills = new List<SkillDefinition>(SkillRanks.SlotCount);
}
