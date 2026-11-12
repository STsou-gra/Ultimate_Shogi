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

  // 自分の駒であればどの駒でも選べる（同じ駒を2回は選べない。アンデッドは自分の魔法の対象に選べない）。
  // 翼状態の駒は破壊マス・落とし穴のマスにいられるので、入れ替えると翼のない駒がそのマスに移る組み合わせは選べない
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    if (context.Targets.Contains(pos)) return false;
    GamePiece piece = context.GameBoard.GetPieceAt(pos.x, pos.y);
    if (!MagicKit.IsOwnPiece(context, piece)) return false;
    if (context.Targets.Count == 0) return true;
    Vector2Int first = context.Targets[0];
    GamePiece other = context.GameBoard.GetPieceAt(first.x, first.y);
    return CanStandOn(context.GameBoard, piece, first) && CanStandOn(context.GameBoard, other, pos);
  }

  // 駒 p が入れ替えで pos のマスに移れるか（破壊マス・落とし穴は翼状態の駒だけ）
  static bool CanStandOn(GameBoard board, GamePiece p, Vector2Int pos)
  {
    if (p == null) return true;
    BoardCell cell = board.Cells.At(pos.x, pos.y);
    if (cell == null) return false;
    if (PieceRules.IsBlockedFor(board.Cells, p, pos.x, pos.y)) return false;
    return !cell.HasTrap || PieceRules.HasWing(p);
  }

  public override void Execute(SkillContext context)
  {
    // 盤面上の駒の位置をスワップする
    context.GameBoard.SwapPiecePosition(context.Targets[0], context.Targets[1]);
    Debug.Log($"{context.Player} は駒の位置を入れ替えた！");
  }
}
