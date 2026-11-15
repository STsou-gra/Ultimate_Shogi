using System.Collections.Generic;
using UnityEngine;

// 魔法スキルの一覧（編成の候補）。編成画面（DeckSelectController）とルール説明（RulesOverlayView）が使う。
// アセットは Resources/SkillDatabase。魔法を足すときは allSkills に足すと、編成の候補にもルール説明の一覧にも出る。
// デバッグ用の魔法（SkillDefinition.debugOnly。今は魔力の火花）は allSkills に残したまま、編成・CPU・ルール説明からは外す
// （Selectable を使う。allSkills は棋譜の再生などで Id から魔法を引くのに使う）
[CreateAssetMenu(menuName = "UltimateShogi/Skill Database")]
public class SkillDatabase : ScriptableObject
{
    public const string ResourcePath = "SkillDatabase";

    public List<SkillDefinition> allSkills = new List<SkillDefinition>();

    // 編成で選べる魔法（デバッグ用を除く。並びは allSkills の順）
    public List<SkillDefinition> Selectable => SelectableOf(allSkills);

    public static List<SkillDefinition> SelectableOf(IEnumerable<SkillDefinition> skills)
    {
        List<SkillDefinition> result = new List<SkillDefinition>();
        if (skills == null) return result;
        foreach (SkillDefinition s in skills)
        {
            if (s != null && s.Selectable && !result.Contains(s)) result.Add(s);
        }
        return result;
    }

    public static SkillDatabase LoadDefault()
    {
        return Resources.Load<SkillDatabase>(ResourcePath);
    }
}
