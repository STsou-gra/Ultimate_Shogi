using UnityEngine;

// 超常魔法「停止世界」（Prototype の src/defs/magic/time_stop.js・src/core/play.js の startTimeStop）。
// 時間を止め、MatchConfig.TimeStopMoves（10）回まで続けて行動できる（移動・打つ・魔法・必殺技・狙撃が各 1 回。同じ駒でもよい。錬成は数えない）。
// 取り・エナジー・爆発・成り・マスの変化は、時間が動き出してからまとめて起こる。途中で「解除」（Q キー・ボタン）もできる。
// 決まりと処理は GameSystem.TimeStop.cs、見た目は TimeStopView・MagicFx.TimeStop.cs
//
// 演出：盤の中央に時計 → 針が猛スピードで回って止まる → 衝撃波・閃光とともに世界の色が反転する →（手番の終わりに）「時よ止まれ」
[CreateAssetMenu(menuName = "UltimateShogi/Effects/TimeStop")]
public class TimeStopEffect : SkillEffect
{
  // 時間停止中は使えない（超常魔法は 1 対局に 1 回なので、通常は起きない）
  public override bool CanUse(SkillContext context)
  {
    return !context.GameSystem.IsTimeStopped;
  }

  public override void Execute(SkillContext context)
  {
    GameSystem system = context.GameSystem;
    SpecialContext tl = context.Timeline;
    PlayerType owner = context.Player;

    if (tl == null)
    {
      system.StartTimeStop(owner, MatchConfig.TimeStopMoves);
      return;
    }

    // 時計の針が止まった瞬間に時間が止まる（色が反転し始める）
    if (context.Fx != null) context.Fx.TimeStopClock(tl.Time);
    tl.Wait(MagicFx.TimeStopClockTime);
    tl.ScheduleState(() => system.StartTimeStop(owner, MatchConfig.TimeStopMoves));
    if (context.Fx != null) context.Fx.TimeStopFreeze(tl.Time);
    tl.Wait(0.6f);
  }
}
