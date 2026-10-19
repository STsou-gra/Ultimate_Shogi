using UnityEngine;

// Unityのメニューから作成できるようにするアトリビュート
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Gain Cost")]
public class GainCostEffect : SkillEffect
{
  [SerializeField] private int amount = 2; // 回復量（インスペクターで変更可能）

  public override void Execute(SkillContext context)
  {
    // 1. 引数の context から、実行したプレイヤーやGameSystemの情報を引っ張り出す
    GameSystem system = context.GameSystem;
    PlayerType player = context.Player;

    // 2. 使用したプレイヤーにポイントを加算する
    system.AddPoint(player, amount);

    Debug.Log($"{player} のコストを {amount} 回復しました！");
  }
}