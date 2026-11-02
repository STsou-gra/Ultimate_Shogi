using System.Collections.Generic;
using UnityEngine;

// 通常魔法「剣」（Prototype の src/defs/magic/sword.js）。
// 自分の駒（王将も可）を1つ選び、その駒の前方1×3マス（左前・前・右前）にいる相手の駒をすべて取る。
// 範囲内の火薬は（敵味方問わず）爆発する。取りなので盾で防がれる。取った駒は持ち駒になる。
// 対象選択中は、カーソルの駒の前方1×3を盤に塗る（相手の駒がいなければ赤）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Sword")]
public class SwordEffect : SkillEffect
{
  // 構えてから斬撃の弧が前方の駒に届くまで（MagicFx.SwordSlash と合わせる）
  public const float WindUp = 0.15f;
  public const float SweepTime = 0.18f;

  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "剣を振るう自分の駒を選択（前方1×3の相手の駒を取る）";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    return MagicKit.IsOwnPiece(context, piece) && MagicKit.FrontEnemies(context.GameBoard, piece).Count > 0;
  }

  // カーソルの自分の駒の前方 1×3
  public override List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    GamePiece piece = context.PieceAt(cursor);
    if (!MagicKit.IsOwnPiece(context, piece)) return null;
    return MagicKit.FrontSquares(context.GameBoard, piece);
  }

  // 前方に相手の駒がいなければ、範囲全体が選べない
  public override List<Vector2Int> GetAreaBlockers(SkillContext context, Vector2Int cursor, List<Vector2Int> area)
  {
    GamePiece piece = context.PieceAt(cursor);
    if (piece != null && MagicKit.FrontEnemies(context.GameBoard, piece).Count > 0) return new List<Vector2Int>();
    return new List<Vector2Int>(area);
  }

  public override string BlockedText => "その駒の前方1×3には相手の駒がいません";

  public override void Execute(SkillContext context)
  {
    GamePiece caster = context.PieceAt(context.Targets[0]);
    SpecialContext tl = context.Timeline;
    if (caster == null || tl == null) return;

    // 範囲内の駒：相手の駒は取る（CapturePiece 経由。盾なら防がれる）、火薬は敵味方問わず爆発、それ以外の味方の駒は無視
    List<GamePiece> inRange = new List<GamePiece>();
    foreach (Vector2Int s in MagicKit.FrontSquares(context.GameBoard, caster))
    {
      GamePiece q = context.GameBoard.GetPieceAt(s.x, s.y);
      if (q != null) inRange.Add(q);
    }

    // 構え → 前方へ斬撃の弧
    int forward = SpecialContext.Forward(caster.player);
    if (context.Fx != null) context.Fx.SwordSlash(caster, forward, inRange, tl.Time);
    tl.Wait(WindUp + SweepTime);

    foreach (GamePiece q in inRange)
    {
      if (!tl.IsAlive(q) || q.player == context.Player || SpecialContext.IsExplosive(q)) continue;
      string name = HandUIManager.GetJapaneseName(q.type);
      if (tl.Capture(q, new Vector2Int(forward, q.Y - caster.Y), caster))
      {
        Debug.Log($"({q.X}, {q.Y})：{q.player} の{name}を剣で斬った！");
      }
    }
    foreach (GamePiece q in inRange)
    {
      if (tl.IsAlive(q) && SpecialContext.IsExplosive(q)) tl.Explode(q.X, q.Y);
    }
    tl.Wait(0.2f);
  }
}
