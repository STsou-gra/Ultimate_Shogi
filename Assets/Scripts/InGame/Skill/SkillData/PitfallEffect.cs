using UnityEngine;

// 通常魔法「落とし穴」（Prototype の src/defs/magic/pitfall.js）。
// 空きマス（駒がなく、破壊・炎上・落とし穴でないマス）に落とし穴を仕掛ける。
// 入った駒は敵味方問わず消失し、穴も消える（GameSystem.EnterCell）。穴は両プレイヤーに見える
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Pitfall")]
public class PitfallEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "落とし穴を仕掛ける空きマスを選択";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    return MagicKit.IsEmptyNormal(context.GameBoard, pos);
  }

  public override void Execute(SkillContext context)
  {
    Vector2Int at = context.Targets[0];
    SpecialContext tl = context.Timeline;
    PlayerType owner = context.Player;
    GameSystem system = context.GameSystem;

    // 魔法陣に闇が吸い込まれる → 落とし穴が開く（穴の見た目は BoardCellView がマスの変化で出す）
    if (context.Fx != null) context.Fx.PitfallCast(at, owner, context.Time);
    if (tl == null)
    {
      system.SetTrap(at.x, at.y, owner);
      return;
    }
    tl.Wait(0.4f);
    if (context.Fx != null) context.Fx.PitfallSet(at, owner, tl.Time);
    tl.ScheduleState(() =>
    {
      if (system.SetTrap(at.x, at.y, owner)) Debug.Log($"({at.x}, {at.y}) に落とし穴を仕掛けた");
    });
    tl.Wait(0.4f);
  }
}
