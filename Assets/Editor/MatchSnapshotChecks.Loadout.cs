using System.Collections.Generic;
using UnityEngine;

// 魔法スキルの編成画面（DeckSelectController・SkillCardView・SkillDetailPopup）の決まりの確認（MatchSnapshotChecks の続き。EditMode テストに並ぶ）。
// Prototype のコミット 80dd2f9（src/scenes/LoadoutScene.js・src/ui/SkillCard.js・src/defs/magic/index.js・tests/core/actions.test.js）に合わせたもの
public static partial class MatchSnapshotChecks
{
    // 魔力の火花はデバッグ用：定義は SkillDatabase に残る（棋譜の再生で Id から引ける）が、編成の候補・CPU の編成・ルール説明には出ない
    public static void CheckDebugMagicIsNotSelectable()
    {
        SkillDatabase db = SkillDatabase.LoadDefault();
        True(db != null, "Resources/SkillDatabase がない");
        SkillDefinition spark = null;
        foreach (SkillDefinition s in db.allSkills)
        {
            if (s != null && s.Id == "TestSpark") spark = s;
        }
        True(spark != null, "魔力の火花（TestSpark_Definition）が SkillDatabase から消えている（昔の棋譜を再生できなくなる）");
        True(spark.debugOnly && !spark.Selectable, "魔力の火花がデバッグ用（debugOnly）になっていない");
        True(!db.Selectable.Contains(spark), "魔力の火花が編成の候補（SkillDatabase.Selectable）に入っている");

        for (uint seed = 1; seed <= 200; seed++)
        {
            foreach (SkillDefinition s in CpuLoadout.Choose(db.allSkills, seed))
            {
                True(s != spark, $"CPU の編成に魔力の火花が入った（seed {seed}）");
            }
        }

        string rules = RulesText(new RulesSources { Magics = db.allSkills });
        True(!rules.Contains(spark.skillName), "ルール説明に魔力の火花が載っている");
        True(rules.Contains("盾"), "ルール説明に盾がない（魔法の一覧が空になっている）");
    }

    // カードの説明の書き出し・「詳細」の説明と補足は、ルール説明と同じ文
    public static void CheckSkillDetailMatchesRulesBook()
    {
        SkillDatabase db = SkillDatabase.LoadDefault();
        True(db != null, "Resources/SkillDatabase がない");
        RulesSources src = new RulesSources { Magics = db.allSkills };
        ForgeRecipeBook book = ForgeRecipeBook.LoadDefault();
        if (book != null) src.ForgeRecipes = book.recipes;
        string rules = RulesText(src);

        int withDetails = 0;
        foreach (SkillDefinition s in db.Selectable)
        {
            string desc = RulesBook.MagicDescription(s);
            True(desc.Length > 0, $"{s.skillName} の説明文が空");
            True(rules.Contains(desc), $"{s.skillName} の説明文がルール説明の一覧と違う");
            True(RulesBook.MagicTagText(s).StartsWith(SkillRanks.Label(s.rank)), $"{s.skillName} の詳細の印にランクがない");

            List<RulesLine> lines = RulesBook.MagicDetailLines(s, src);
            if (lines.Count > 0) withDetails++;
            foreach (RulesLine line in lines)
            {
                True(line.Kind != RulesLineKind.Heading, $"{s.skillName} の補足に見出しが入っている");
                True(rules.Contains(line.Text), $"{s.skillName} の補足「{line.Text}」がルール説明の「魔法の補足」にない");
            }
            string formatted = SkillDetailPopup.FormatDetails(lines);
            foreach (RulesLine line in lines) True(formatted.Contains(line.Text), $"{s.skillName} の補足が詳細のポップアップの文に入っていない");
        }
        True(withDetails > 0, "補足のある魔法が 1 つもない（MagicDetailLines が空を返している）");
    }

    static string RulesText(RulesSources src)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (RulesPage page in RulesBook.Build(src)) sb.Append(page.ToPlainText());
        return sb.ToString();
    }
}
