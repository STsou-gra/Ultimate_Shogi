using System.Collections.Generic;
using UnityEngine;

// 必殺魔法「破壊砲」（Prototype の src/defs/magic/cannon.js）。
// 縦 3×9（連続する 3 筋。カーソル位置を中央の筋とする。盤の端では内側へずらす）の範囲に、術者の側から盤の端から端へ破壊光線を放つ。
// 範囲内の駒はすべて消失し（取りではないので盾は無効・火薬は爆発しない）、マスは破壊される（対局中は戻らない）。
// どちらかの王将がいる範囲は選べない。向きは縦のみ（横 9×3 も入れるなら OrientCount を 2 にして Band の horizontal を切り替える）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Cannon")]
public class CannonEffect : SkillEffect
{
  public const string Reason = "破壊砲";

  // 演出の長さ（MagicFx の CannonCharge・CannonFire と合わせる）
  public const float ChargeTime = 0.65f;
  public const float FireTime = 0.25f;

  public override int TargetCount => 1;
  public override bool UsesArea => true;

  public override string GetTargetPrompt(int index)
  {
    return "破壊光線を放つ範囲を選択（縦3×9。範囲の駒は消失し、マスは破壊される）";
  }

  // 範囲だけを塗る（選べる中心のマスは光らせない）
  public override List<Vector2Int> GetPreviewCells(SkillContext context, Vector2Int cursor)
  {
    return new List<Vector2Int>();
  }

  public override List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    return MagicKit.Band(cursor.x, cursor.y, false);
  }

  public override List<Vector2Int> GetAreaBlockers(SkillContext context, Vector2Int cursor, List<Vector2Int> area)
  {
    return MagicKit.KingSquares(context.GameBoard, area);
  }

  public override string BlockedText => "王将がいる範囲（赤）は選べません";

  public override string GetOrientLabel(int orient)
  {
    return "縦3×9";
  }

  public override void Execute(SkillContext context)
  {
    Vector2Int at = context.Targets[0];
    SpecialContext tl = context.Timeline;
    GameSystem system = context.GameSystem;
    PlayerType owner = context.Player;
    List<Vector2Int> cells = MagicKit.Band(at.x, at.y, false);
    int f = SpecialContext.Forward(owner); // 光線の進む向き（術者の側から相手の側へ）

    List<GamePiece> victims = new List<GamePiece>();
    foreach (Vector2Int c in cells)
    {
      GamePiece q = context.PieceAt(c);
      if (q != null) victims.Add(q);
    }

    if (tl == null)
    {
      foreach (GamePiece q in victims) system.VanishPiece(q, Reason);
      foreach (Vector2Int c in cells) system.BreakCell(c.x, c.y);
      return;
    }

    // 1. 充填 → 2. 発射
    if (context.Fx != null) context.Fx.CannonCharge(cells, owner, tl.Time);
    tl.Wait(ChargeTime);
    if (context.Fx != null) context.Fx.CannonFire(cells, owner, tl.Time);
    tl.Wait(FireTime);

    // 3. 範囲内の駒をすべて消失させる（盾は関係なく、火薬も爆発しない）。駒は光線の進む向きへ吹き飛ぶ
    int[] lost = new int[2];
    foreach (GamePiece q in victims)
    {
      PlayerType side = q.player;
      if (tl.Vanish(q, Reason, new Vector2Int(f, 0))) lost[side == PlayerType.Player1 ? 0 : 1]++;
    }
    tl.Wait(0.25f);

    // 4. 範囲内のマスをすべて破壊状態にする（光線の進む向きに、術者の側から崩れていく）
    List<Vector2Int> order = new List<Vector2Int>(cells);
    order.Sort((a, b) => a.x * f - b.x * f);
    int prev = int.MinValue;
    foreach (Vector2Int c in order)
    {
      int step = c.x * f;
      if (prev != int.MinValue && step != prev) tl.Wait(0.035f);
      prev = step;
      tl.Schedule(() => system.BreakCell(c.x, c.y));
    }
    if (context.Fx != null) context.Fx.CannonDone(cells, owner, tl.Time);
    Debug.Log($"破壊砲が筋 {cells[0].y}〜{cells[cells.Count - 1].y} を撃ち抜いた！ 駒 {lost[0] + lost[1]} 枚が消失（先手 {lost[0]}・後手 {lost[1]}）");
    tl.Wait(0.5f);
  }
}
