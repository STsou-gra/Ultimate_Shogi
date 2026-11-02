using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Effects/Double Turn")]
public class DoubleTurnEffect : SkillEffect
{
  // 時間停止中は使えない（時間停止中は 1 回行動しても手番が終わらないので意味がない）
  public override bool CanUse(SkillContext context)
  {
    return !context.GameSystem.IsTimeStopped;
  }

  public override void Execute(SkillContext context)
  {
    GameSystem system = context.GameSystem;
    PlayerType self = context.Player;

    // ゲームシステム側に「追加ターン」のフラグを立てる
    system.GrantExtraTurn(self);

    Debug.Log($"{self} は追加行動権を得た！このターン2回動かせます。");
  }
}