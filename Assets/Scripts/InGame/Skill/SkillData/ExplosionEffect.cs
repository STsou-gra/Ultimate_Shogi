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
    Vector2Int center = context.Targets[0];

    Debug.Log($"中心位置 ({center.x}, {center.y}) で大爆発が発生！");

    // 火薬の爆発と同じ処理（中心から3x3の駒が「消失」：エナジー・持ち駒化なし）。
    // このスキルでも王将は撃破できない（範囲内の王将は残る。MatchConfig.ExplosionSparesKing）。
    // 範囲内の火薬は誘爆せずに消える（MatchConfig.ChainExplosion が false のとき）
    context.GameSystem.Explode(center.x, center.y);
  }
}
