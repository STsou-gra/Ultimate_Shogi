using System.Collections.Generic;
using UnityEngine;

// 通常魔法「ボム」（Prototype の src/defs/magic/bomb.js）。対象を 2 段階で選ぶ（ワープと同じ UsesDestination）：
//  1. 爆弾を投げる自分の駒（狙えるマスが 1 つ以上あるもの）
//  2. 狙うマス：その駒の 2 マス先の前方 1×3（左前・前・右前）のうち、そこを中心とした 3×3 にどちらの王将もいないマス
// 狙ったマスを中心に 3×3 が爆発し（盤外にはみ出した分は何も起こらない）、範囲の駒は敵味方問わず消失する
// （取りではないので盾は無効。火薬は誘爆せずにそのまま消える。MatchConfig.ChainExplosion なら爆発する）。
// 2 マス先が盤外の駒・狙えるマスがない駒は選べない。破壊マスも狙える（落ちて爆発する）。
// 対象選択中は、1 段階目は自分の駒に合わせると狙えるマスを、2 段階目は狙うマスに合わせると爆発の 3×3 を盤に塗る
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Bomb")]
public class BombEffect : SkillEffect
{
  public const string Reason = "ボム";

  public override int TargetCount => 1;
  public override bool UsesDestination => true;

  public override string GetTargetPrompt(int index)
  {
    return "爆弾を投げる自分の駒を選択";
  }

  public override string GetDestinationPrompt()
  {
    return "狙うマスを選択（2マス先の左前・前・右前。そこを中心に3×3が爆発）";
  }

  // 狙えるマスがある自分の駒（Prototype の getTargets）
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    return MagicKit.IsOwnPiece(context, piece) && MagicKit.BombAimSquares(context.GameBoard, piece).Count > 0;
  }

  // 選んだ駒（context.Targets[0]）が to を狙えるか（Prototype の getDestinations）
  public override bool IsValidDestination(SkillContext context, Vector2Int to)
  {
    if (context.Targets.Count == 0) return false;
    GamePiece piece = context.PieceAt(context.Targets[0]);
    if (!MagicKit.IsOwnPiece(context, piece)) return false;
    return MagicKit.BombAimSquares(context.GameBoard, piece).Contains(to);
  }

  // 1 段階目：カーソルの自分の駒が狙えるマス（Prototype の preview。origin なし）
  public override List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    GamePiece piece = context.PieceAt(cursor);
    if (!MagicKit.IsOwnPiece(context, piece)) return null;
    return MagicKit.BombAimSquares(context.GameBoard, piece);
  }

  // 2 段階目：狙うマスを中心とした爆発の 3×3（Prototype の preview。origin あり）
  public override List<Vector2Int> GetDestinationAreaCells(SkillContext context, Vector2Int cursor)
  {
    return MagicKit.SquareArea(cursor.x, cursor.y, 1);
  }

  public override void Execute(SkillContext context)
  {
    if (context.Targets.Count == 0 || !context.Destination.HasValue) return;
    GamePiece caster = context.PieceAt(context.Targets[0]);
    SpecialContext tl = context.Timeline;
    GameSystem system = context.GameSystem;
    if (caster == null) return;
    Vector2Int at = context.Destination.Value;
    List<Vector2Int> area = MagicKit.SquareArea(at.x, at.y, 1);

    // 範囲の駒（消える前に集める）
    List<GamePiece> victims = MagicKit.PiecesIn(context.GameBoard, area); // 巨人は 1 回だけ

    if (tl == null)
    {
      foreach (GamePiece q in victims) system.VanishPiece(q, Reason);
      return;
    }

    // 1. 投擲：駒から爆弾が放物線を描いて飛ぶ → 2. 着弾と同時に爆発
    if (context.Fx != null) context.Fx.BombThrow(caster, at, area, tl.Time);
    tl.Wait(MagicFx.BombFlightTime);
    if (context.Fx != null) context.Fx.BombBlast(at, area, tl.Time);
    tl.Wait(0.15f);

    // 3. 範囲の駒はすべて消失（取りではないので盾は関係ない。火薬は MatchConfig.ChainExplosion のときだけ誘爆する）。
    //    駒は、狙ったマスから外へ吹き飛ぶ（Prototype の originCell: at）
    List<Vector2Int> chain = new List<Vector2Int>();
    int[] lost = new int[2];
    foreach (GamePiece q in victims)
    {
      if (!tl.IsAlive(q)) continue;
      Vector2Int pos = new Vector2Int(q.X, q.Y);
      if (MatchConfig.ChainExplosion && SpecialContext.IsExplosive(q)) chain.Add(pos);
      PlayerType owner = q.player;
      if (tl.Vanish(q, Reason, new Vector2Int(System.Math.Sign(pos.x - at.x), System.Math.Sign(pos.y - at.y)))) lost[(int)owner]++;
    }
    tl.HitBoss(area); // 討伐任務：3×3 がボスに重なれば当たる
    tl.Wait(0.5f);
    foreach (Vector2Int c in chain) system.Explode(c.x, c.y, tl.Time);

    int total = lost[0] + lost[1];
    Debug.Log(total > 0
      ? $"({at.x}, {at.y}) でボムが炸裂！ 駒 {total} 枚が消失（先手 {lost[0]}・後手 {lost[1]}）"
      : $"({at.x}, {at.y}) でボムが炸裂！ 巻き込まれた駒はなかった");
    tl.Wait(0.4f);
  }
}
