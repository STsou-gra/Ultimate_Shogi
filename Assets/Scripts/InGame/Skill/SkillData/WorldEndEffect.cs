using System.Collections.Generic;
using UnityEngine;

// 超常魔法「世界の終わり」（Prototype の src/defs/magic/world_end.js・src/core/play.js の startWorldEnd・worldEndCollapse）。
// 発動すると対局終了まで、1 ターンごとにランダムなマスが MatchConfig.WorldEndCells（3）つ崩れ落ちる（破壊。そこにいた駒は消失）。
//  ・発動した瞬間に 1 回目が崩れ、次のターンからはターンの終わり（後手の手番のあと）ごとに崩れる（WorldEndCollapse）
//  ・崩れるマスは直前に赤く点滅する。王将がいるマスは選ばない。残りのマスが足りなければ残り全部が崩れる
//  ・両者が発動すると効果が重なる（1 ターンに 6 マス）。発動中は盤の下に「世界の終わり 進行中」と出る（WorldEndView）
// 崩れるマスは対局の乱数（GameRng）で選ぶ
[CreateAssetMenu(menuName = "UltimateShogi/Effects/WorldEnd")]
public class WorldEndEffect : SkillEffect
{
  public const string Title = "世界の終わり";
  public const float BreakInterval = 0.12f; // マスが 1 つずつ崩れていく間隔

  public override void Execute(SkillContext context)
  {
    GameSystem system = context.GameSystem;
    SpecialContext tl = context.Timeline;
    PlayerType owner = context.Player;

    if (tl == null)
    {
      system.StartWorldEnd(owner);
      Collapse(system, MatchConfig.WorldEndCells, null, null);
      return;
    }

    // 画面が赤黒く暗くなり、ひびが走る → 世界の終わりの開始（盤の下の表示・背景の赤み）→ 発動したターンの分を崩す
    if (context.Fx != null)
    {
      context.Fx.WorldEndDoom(tl.Time);
      tl.Wait(MagicFx.DoomTime);
    }
    tl.Schedule(() => system.StartWorldEnd(owner));
    tl.Wait(0.3f);
    Collapse(system, MatchConfig.WorldEndCells, tl, context.Fx);
  }

  // 世界の終わりの崩壊 1 回分：崩れるマスを n 個選び（対局の乱数）、赤く点滅させてから 1 マスずつ崩す（駒は消失）。
  // 盤面は Timeline の時刻どおりに変わる（tl が null ならすぐ崩す）。崩れるマスを返す
  public static List<Vector2Int> Collapse(GameSystem system, int n, SpecialContext tl, MagicFx fx)
  {
    List<Vector2Int> picked = system.PickWorldEndCells(n);
    if (picked.Count == 0) return picked;

    List<string> victims = new List<string>();
    foreach (Vector2Int c in picked)
    {
      GamePiece q = system.Board.GetPieceAt(c.x, c.y);
      if (q != null && !PieceRules.HasMagicWing(q)) victims.Add($"{TurnEndAction.SideName(q.player)}の{HandUIManager.GetJapaneseName(q.type)}");
    }
    string where = string.Join("・", picked.ConvertAll(c => $"({c.x}, {c.y})"));
    string log = $"世界の終わり：{where} が崩れ落ちた" + (victims.Count > 0 ? $"（{string.Join("・", victims)}が消失）" : "");

    if (tl == null)
    {
      foreach (Vector2Int c in picked) system.BreakCell(c.x, c.y);
      Debug.Log(log);
      return picked;
    }

    // 崩れるマスが赤く点滅する → 1 マスずつ崩れ落ちる（マスの板が抜け落ちる見た目は BoardCellView）
    if (fx != null) fx.CollapseWarning(picked, tl.Time);
    tl.Wait(MagicFx.CollapseWarningTime);
    foreach (Vector2Int c in picked)
    {
      tl.Schedule(() => system.BreakCell(c.x, c.y));
      tl.Wait(BreakInterval);
    }
    if (fx != null) fx.Collapsed(tl.Time);
    tl.Schedule(() => Debug.Log(log));
    tl.Wait(0.6f);
    return picked;
  }
}
