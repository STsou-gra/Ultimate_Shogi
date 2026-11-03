using System.Collections.Generic;
using UnityEngine;

// 魔法スキルの一覧（編成の候補）。編成画面（DeckSelectController）とルール説明（RulesOverlayView）が使う。
// アセットは Resources/SkillDatabase。魔法を足すときは allSkills に足すと、編成の候補にもルール説明の一覧にも出る
[CreateAssetMenu(menuName = "UltimateShogi/Skill Database")]
public class SkillDatabase : ScriptableObject
{
    public const string ResourcePath = "SkillDatabase";

    public List<SkillDefinition> allSkills = new List<SkillDefinition>();

    public static SkillDatabase LoadDefault()
    {
        return Resources.Load<SkillDatabase>(ResourcePath);
    }
}
