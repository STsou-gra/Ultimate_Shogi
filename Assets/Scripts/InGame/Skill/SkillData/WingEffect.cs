using UnityEngine;

// 通常魔法「翼」（Prototype の src/defs/magic/wing.js）。
// 翼状態ではない自分の駒（火薬・鳥以外。王将も可。アンデッドは不可）を 1 つ選び、翼状態にする（両脇に白い翼）。
// 翼状態の駒はマスの効果を受けない（PieceRules.HasWing）：破壊マスにも移動でき（押し出し・ファランクスの前進も）、
// 炎上・落とし穴のマスに入っても消失しない（落とし穴は閉じずに残る）。走り駒・突進は破壊・炎上・落とし穴のマスと火薬の上を通り抜け、
// 通った火薬は爆発しない（火薬のマスに止まれば通常どおり爆発）。いるマスが壊れても消失しない。
// 破壊砲の光線・隕石・爆発など駒に直接当たる効果は受ける。破壊マスの上の翼の駒は、翼のない駒では取れない（狙撃・魔法・光線・槍は届く）。
// 取られて持ち駒になると解除される。壁は翼状態でも通れない（PieceRules.IsWallCell。Unity にはまだ壁がない）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Wing")]
public class WingEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "翼状態にする自分の駒を選択";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    // 鳥ははじめから翼状態。火薬には付けられない
    return MagicKit.IsOwnPiece(context, piece) && !PieceRules.HasWing(piece) && !PieceRules.IsExplosive(piece.type);
  }

  public override void Execute(SkillContext context)
  {
    GamePiece piece = context.PieceAt(context.Targets[0]);
    if (piece == null) return;
    context.GameSystem.GiveWing(piece);
    if (context.Timeline != null) context.Timeline.Wait(0.5f);
  }
}
