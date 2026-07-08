using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Effects/Swap Piece")]
public class SwapPieceEffect : SkillEffect
{
  public override void Execute(SkillContext context)
  {
    GameBoard board = context.GameBoard;
    PlayerType self = context.Player;

    // 1. 自分の王将(King)の位置を探す
    Vector2Int kingPos = board.FindPiece(self, PieceType.King);
    // 2. 自陣の歩兵(Pawn)など、入れ替え対象を探す
    Vector2Int targetPos = board.FindPiece(self, PieceType.Pawn);

    if (kingPos != Vector2Int.down && targetPos != Vector2Int.down)
    {
      // 盤面上の駒の位置をスワップする
      board.SwapPiecePosition(kingPos, targetPos);
      Debug.Log($"{self} は王将の位置を入れ替えて危険を回避した！");
    }
  }
}