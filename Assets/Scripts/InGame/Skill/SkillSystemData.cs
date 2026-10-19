using System.Collections.Generic;
using UnityEngine;

// ==========================================
// 1. スキル実行に必要なコンテキスト（データ集約）
// ==========================================
public class SkillContext
{
  public GameSystem GameSystem { get; }
  public GameBoard GameBoard { get; }
  public PlayerType Player { get; }
  // プレイヤーが選んだ対象のマス（対象を選ぶスキルのみ使用）
  public List<Vector2Int> Targets { get; } = new List<Vector2Int>();

  public SkillContext(GameSystem system, GameBoard board, PlayerType player)
  {
    GameSystem = system;
    GameBoard = board;
    Player = player;
  }
}

// ==========================================
// 2. スキル効果の抽象クラス
// ==========================================
public abstract class SkillEffect : ScriptableObject
{
  // 発動前にプレイヤーが選ぶマスの数（0ならすぐ発動）
  public virtual int TargetCount => 0;

  // そのマスを次の対象として選べるか（選択済みの対象は context.Targets に入っている）
  public virtual bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    return true;
  }

  // 対象選択中に表示する案内文（index は何個目の対象か）
  public virtual string GetTargetPrompt(int index)
  {
    return "対象のマスを選択";
  }

  // 対象選択中に光らせるマス（既定では選べるマスと選択済みのマス）
  public virtual List<Vector2Int> GetPreviewCells(SkillContext context, Vector2Int cursor)
  {
    List<Vector2Int> cells = new List<Vector2Int>();
    for (int x = 0; x < 9; x++)
    {
      for (int y = 0; y < 9; y++)
      {
        Vector2Int pos = new Vector2Int(x, y);
        if (context.Targets.Contains(pos) || IsValidTarget(context, pos))
        {
          cells.Add(pos);
        }
      }
    }
    return cells;
  }

  public abstract void Execute(SkillContext context);
}

// ==========================================
// 3. スキルの定義（Executorの役割も統合）
// ==========================================
[CreateAssetMenu(menuName = "UltimateShogi/Skill Definition")]
public class SkillDefinition : ScriptableObject
{
  public string skillName;
  public Sprite icon;
  public int cost;
  [TextArea]
  public string description;
  public SkillEffect effect;

  // Executorを廃止し、定義自身に実行させる（冗長化の削減）
  public void Use(SkillContext context)
  {
    if (effect != null)
    {
      effect.Execute(context);
    }
    else
    {
      Debug.LogWarning($"{skillName} の効果(Effect)が設定されていません。");
    }
  }
}

// ==========================================
// 4. スキルデッキ
// ==========================================
[CreateAssetMenu(menuName = "UltimateShogi/Skill Deck")]
public class SkillDeck : ScriptableObject
{
  public List<SkillDefinition> skills = new List<SkillDefinition>(5);
}