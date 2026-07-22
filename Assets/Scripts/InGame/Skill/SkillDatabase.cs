using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Skill Database")]
public class SkillDatabase : ScriptableObject
{
    public List<SkillDefinition> allSkills = new List<SkillDefinition>();
}
