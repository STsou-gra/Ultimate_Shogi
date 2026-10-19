using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Effects/Swap Piece")]
public class SwapPieceEffect : SkillEffect
{
  // 入れ替える自分の駒を2つ選ぶ
  public override int TargetCount => 2;

  public override string GetTargetPrompt(int index)
  {
    return (index == 0) ? "入れ替える1つ目の駒を選択" : "入れ替える2つ目の駒を選択";
  }

  // 自分の駒であればどの駒でも選べる（同じ駒を2回は選べない）
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    if (context.Targets.Contains(pos)) return false;
    GamePiece piece = context.GameBoard.GetPieceAt(pos.x, pos.y);
    return piece != null && piece.player == context.Player;
  }

  public override void Execute(SkillContext context)
  {
    // 盤面上の駒の位置をスワップする
    context.GameBoard.SwapPiecePosition(context.Targets[0], context.Targets[1]);
    Debug.Log($"{context.Player} は駒の位置を入れ替えた！");
  }
}
