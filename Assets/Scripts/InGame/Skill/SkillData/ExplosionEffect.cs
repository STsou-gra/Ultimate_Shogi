using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Effects/Explosion")]
public class ExplosionEffect : SkillEffect
{
  // カーソルで爆発の中心マスを1つ選ぶ
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "爆発の中心マスを選択";
  }

  // カーソルを中心とした3x3マスを光らせて、爆発の範囲を見せる
  public override List<Vector2Int> GetPreviewCells(SkillContext context, Vector2Int cursor)
  {
    List<Vector2Int> cells = new List<Vector2Int>();
    for (int x = cursor.x - 1; x <= cursor.x + 1; x++)
    {
      for (int y = cursor.y - 1; y <= cursor.y + 1; y++)
      {
        if (x < 0 || x >= 9 || y < 0 || y >= 9) continue;
        cells.Add(new Vector2Int(x, y));
      }
    }
    return cells;
  }

  public override void Execute(SkillContext context)
  {
    GameBoard board = context.GameBoard;
    Vector2Int center = context.Targets[0];

    Debug.Log($"中心位置 ({center.x}, {center.y}) で大爆発が発生！");

    // 中心から3x3マスの範囲をループ処理
    for (int x = center.x - 1; x <= center.x + 1; x++)
    {
      for (int y = center.y - 1; y <= center.y + 1; y++)
      {
        // 盤面の外（マイナス座標や9マス以上）はスルーする
        if (x < 0 || x >= 9 || y < 0 || y >= 9) continue;

        GamePiece piece = board.GetPieceAt(x, y);
        if (piece == null) continue;

        // このスキルでは王将を撃破できない
        if (piece.type == PieceType.King)
        {
          Debug.Log($"({x}, {y}) の王将は爆発に耐えた。");
          continue;
        }

        // AddPieceToHand を呼ばずに直接消去することで「誰も駒を取得できない」状態にする
        board.RemovePieceAt(x, y);
        Debug.Log($"({x}, {y}) の駒が爆発に巻き込まれて消滅した。");
      }
    }
  }
}
