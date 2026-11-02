using UnityEngine;

// 必殺魔法「ファランクス」（Prototype の src/defs/magic/phalanx.js）。
// 盾状態の自分の駒（王将以外）を1つ選び、ファランクス状態にする（盾はそのまま）。
// ファランクス状態の駒は、持ち主の手番の行動のあと・手番交代の前に自動で 1 マス前進し、前方 1×3 マスの相手の駒を取る
// （自動行動は PhalanxAdvance。GameSystem が手番交代の前に呼ぶ）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Phalanx")]
public class PhalanxEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "ファランクス状態にする盾状態の自分の駒を選択（王将以外）";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    return MagicKit.IsOwnPiece(context, piece) && piece.hasShield && !piece.hasPhalanx && piece.type != PieceType.King;
  }

  public override void Execute(SkillContext context)
  {
    GamePiece piece = context.PieceAt(context.Targets[0]);
    if (piece == null) return;
    context.GameSystem.GivePhalanx(piece);
    if (context.Timeline != null) context.Timeline.Wait(0.6f);
  }
}
