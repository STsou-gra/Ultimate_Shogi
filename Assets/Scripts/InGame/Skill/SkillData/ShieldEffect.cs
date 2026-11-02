using UnityEngine;

// 通常魔法「盾」（Prototype の src/defs/magic/shield.js）。
// 盾状態ではない自分の駒を1つ選び、盾状態にする。盾状態の駒は、相手の「取り」を1回だけ防ぐ（盾は割れる）。
// 消失（爆発・炎上・落とし穴など）は防げない。判定は GameSystem.CapturePiece の中で行う
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Shield")]
public class ShieldEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "盾状態にする自分の駒を選択";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    return MagicKit.IsOwnPiece(context, piece) && !piece.hasShield;
  }

  public override void Execute(SkillContext context)
  {
    GamePiece piece = context.PieceAt(context.Targets[0]);
    if (piece == null) return;
    context.GameSystem.GiveShield(piece);
    if (context.Timeline != null) context.Timeline.Wait(0.5f);
  }
}
