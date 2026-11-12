using UnityEngine;

// 通常魔法「壁」（Prototype の src/defs/magic/wall.js）。
// 自陣か中立の空きマス（駒がなく、破壊・炎上・落とし穴・壁でないマス）に壁を建てる（GameSystem.SetWall）。
// 壁のマスは敵味方問わず移動・通過・打ち込みができない：
//  ・走り駒（香・角・飛）・突進技（突撃・貫通槍）は壁の手前で止まり、光線（光芒四射・龍王旋風）は壁に当たって止まる
//  ・押し出された駒（金剛衝）は壁にぶつかって止まる。ファランクスの前進・アンデッドの自動行動も壁を通れない
//  ・桂の跳躍（通常の移動・天馬跳躍）は飛び越せる。狙撃はさえぎらない
// 壁は対局終了まで残る（取り・爆発・隕石・炎上では壊れない）。マスが破壊されると壁ごと崩れる（破壊砲・世界の終わり）。
// 天変地異ではマスと一緒に動く。時間停止中は設置の予定になる（時間が動き出してから建つ）。見た目は BoardCellView
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Wall")]
public class WallEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "壁を建てる空きマスを選択（自陣か中立）";
  }

  // 自陣か中立の、駒がいない通常状態のマス（Prototype の getTargets：emptyNormalSquares を自陣・中立に絞る）
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    if (!MagicKit.IsEmptyNormal(context.GameBoard, pos)) return false;
    return BoardZones.IsOwnZone(context.Player, pos.x) || BoardZones.ZoneOf(pos.x) == BoardZone.Neutral;
  }

  public override void Execute(SkillContext context)
  {
    Vector2Int at = context.Targets[0];
    SpecialContext tl = context.Timeline;
    PlayerType owner = context.Player;
    GameSystem system = context.GameSystem;

    // 魔法陣から土煙が立ち上る → 壁がせり上がる（壁の見た目は BoardCellView がマスの変化で出す）
    if (context.Fx != null) context.Fx.WallCast(at, owner, context.Time);
    if (tl == null)
    {
      system.SetWall(at.x, at.y, owner);
      return;
    }
    tl.Wait(0.35f);
    if (context.Fx != null) context.Fx.WallSet(at, owner, tl.Time);
    tl.Schedule(() =>
    {
      if (system.SetWall(at.x, at.y, owner)) Debug.Log($"({at.x}, {at.y}) に壁を建てた");
    });
    tl.Wait(0.4f);
  }
}
