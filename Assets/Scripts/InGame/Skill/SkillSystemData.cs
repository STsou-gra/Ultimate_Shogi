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