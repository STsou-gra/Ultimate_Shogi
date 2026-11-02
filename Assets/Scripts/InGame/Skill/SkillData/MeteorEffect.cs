using System.Collections.Generic;
using UnityEngine;

// 必殺魔法「隕石」（Prototype の src/defs/magic/meteor.js）。
// 自陣か中立の、破壊されていないマスを中心に 3×3 へ隕石を落とす（3×3 は盤外にはみ出してもよい。はみ出した分は何も起こらない）。
// 範囲の駒は敵味方問わずすべて消失する（取りではないので盾では防げない。火薬は誘爆せず、そのまま消える）。
// その後、範囲の破壊されていないマスが 10 ターン炎上する。どちらかの王将が範囲にいる位置は選べない
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Meteor")]
public class MeteorEffect : SkillEffect
{
  public const string Reason = "隕石";
  public const int BurnTurns = 10;

  public override int TargetCount => 1;
  public override bool UsesArea => true;

  public override string GetTargetPrompt(int index)
  {
    return "隕石を落とす中心のマスを選択（自陣か中立。3×3 の駒は消失し、10ターン炎上）";
  }

  // 選べる中心のマスを全部光らせると範囲の表示と重なって見づらいので、範囲だけを塗る
  public override List<Vector2Int> GetPreviewCells(SkillContext context, Vector2Int cursor)
  {
    return new List<Vector2Int>();
  }

  // カーソル位置を中心とした 3×3（盤内の分だけ）
  public override List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    return MagicKit.SquareArea(cursor.x, cursor.y, 1);
  }

  // 中心が自陣・中立以外か破壊マスなら中心のマス、王将がいればそのマスが選べない原因
  public override List<Vector2Int> GetAreaBlockers(SkillContext context, Vector2Int cursor, List<Vector2Int> area)
  {
    List<Vector2Int> blockers = new List<Vector2Int>();
    bool centerOk = (BoardZones.IsOwnZone(context.Player, cursor.x) || BoardZones.ZoneOf(cursor.x) == BoardZone.Neutral)
                    && !context.GameBoard.Cells.IsBroken(cursor.x, cursor.y);
    if (!centerOk) blockers.Add(cursor);
    blockers.AddRange(MagicKit.KingSquares(context.GameBoard, area));
    return blockers;
  }

  public override string BlockedText => "中心は自陣か中立のマス（破壊マス以外）。王将を巻き込む範囲（赤）は選べません";

  public override void Execute(SkillContext context)
  {
    Vector2Int center = context.Targets[0];
    SpecialContext tl = context.Timeline;
    GameSystem system = context.GameSystem;
    List<Vector2Int> area = MagicKit.SquareArea(center.x, center.y, 1);

    // 落下予告（範囲が赤く明滅）→ 術者の側の斜め上から落下 → 着弾
    if (context.Fx != null) context.Fx.MeteorFall(center, area, context.Player, context.Time);
    if (tl == null)
    {
      foreach (Vector2Int s in area)
      {
        GamePiece q = context.PieceAt(s);
        if (q != null) system.VanishPiece(q, Reason);
      }
      foreach (Vector2Int s in area) system.SetBurning(s.x, s.y, BurnTurns);
      return;
    }
    tl.Wait(MagicFx.MeteorFallTime);

    // 範囲の駒はすべて消失（火薬も誘爆せず、そのまま消える）。駒は中心から外へ吹き飛ぶ
    int n = 0;
    foreach (Vector2Int s in area)
    {
      GamePiece q = context.PieceAt(s);
      if (q == null) continue;
      Vector2Int dir = new Vector2Int(System.Math.Sign(s.x - center.x), System.Math.Sign(s.y - center.y));
      if (tl.Vanish(q, Reason, dir)) n++;
    }
    tl.Wait(0.5f);

    // 範囲のマスを炎上させる（破壊マスは SetBurning 側で除外される）
    tl.Schedule(() =>
    {
      foreach (Vector2Int s in area) system.SetBurning(s.x, s.y, BurnTurns);
    });
    Debug.Log($"({center.x}, {center.y}) に隕石が落下！ {(n > 0 ? $"駒 {n} 個が消失し、" : "")}周囲が {BurnTurns} ターン炎上する！");
    tl.Wait(0.6f);
  }
}
