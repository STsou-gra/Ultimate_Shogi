using UnityEngine;

// 通常魔法「魔力の火花」（Prototype の src/defs/magic/test_spark.js）。
// 【動作確認用】自分の駒を 1 つ選び、魔力の火花を散らす。盤面への効果はない（魔法の発動・カットイン・手番の流れの確認用。
// 実際の魔法が揃ったら SkillDatabase から外してよい）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Spark")]
public class SparkEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "火花を散らす自分の駒を選択（盤面への効果はない）";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    return MagicKit.IsOwnPiece(context, context.PieceAt(pos));
  }

  public override void Execute(SkillContext context)
  {
    Vector2Int at = context.Targets[0];
    if (context.Fx != null) context.Fx.Spark(at, context.Time);
    if (context.Timeline != null) context.Timeline.Wait(0.5f);
  }
}
