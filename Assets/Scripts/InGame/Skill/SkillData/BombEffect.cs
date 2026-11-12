using System.Collections.Generic;
using UnityEngine;

// 通常魔法「ボム」（Prototype の src/defs/magic/bomb.js）。
// 自分の駒を 1 つ選び、その 2 マス先の前方 1×3 マス（左前・前・右前）に爆弾を 3 つ投げる。
// 投げたマスを中心にそれぞれ 3×3 が爆発し（合わせて駒の前方 1〜3 マス × 横 5 マス。盤外にはみ出した分は何も起こらない）、
// 範囲の駒は敵味方問わず消失する（取りではないので盾は無効。火薬は誘爆せずにそのまま消える。MatchConfig.ChainExplosion なら爆発する）。
// 範囲にどちらかの王将が入る駒・2 マス先が盤外になる駒は選べない。投げた駒自身は巻き込まれない。
// 対象選択中は、カーソルの自分の駒の爆発の範囲を盤に塗る（選べない駒なら赤）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Bomb")]
public class BombEffect : SkillEffect
{
  public const string Reason = "ボム";

  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "爆弾を投げる自分の駒を選択（2マス先の前方1×3を中心に、それぞれ3×3が爆発）";
  }

  // 2 マス先が盤内で、範囲に王将がいない自分の駒
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    if (!MagicKit.IsOwnPiece(context, piece)) return false;
    if (MagicKit.BombLandingSquares(piece).Count == 0) return false;
    return MagicKit.KingSquares(context.GameBoard, MagicKit.BombBlastSquares(piece)).Count == 0;
  }

  // カーソルの自分の駒の爆発の範囲（Prototype の preview）
  public override List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    GamePiece piece = context.PieceAt(cursor);
    if (!MagicKit.IsOwnPiece(context, piece)) return null;
    return MagicKit.BombBlastSquares(piece);
  }

  // 範囲の王将が選べない原因
  public override List<Vector2Int> GetAreaBlockers(SkillContext context, Vector2Int cursor, List<Vector2Int> area)
  {
    return MagicKit.KingSquares(context.GameBoard, area);
  }

  public override string BlockedText => "王将を巻き込む駒（赤）は選べません";

  public override void Execute(SkillContext context)
  {
    GamePiece caster = context.PieceAt(context.Targets[0]);
    SpecialContext tl = context.Timeline;
    GameSystem system = context.GameSystem;
    if (caster == null) return;
    List<Vector2Int> landing = MagicKit.BombLandingSquares(caster);
    List<Vector2Int> area = MagicKit.BombBlastSquares(caster);

    // 範囲の駒（消える前に集める）
    List<GamePiece> victims = new List<GamePiece>();
    foreach (Vector2Int s in area)
    {
      GamePiece q = context.PieceAt(s);
      if (q != null) victims.Add(q);
    }

    if (tl == null)
    {
      foreach (GamePiece q in victims) system.VanishPiece(q, Reason);
      return;
    }

    // 1. 投擲：駒から 3 つの爆弾が放物線を描いて飛ぶ → 2. 着弾と同時に爆発
    if (context.Fx != null) context.Fx.BombThrow(caster, landing, tl.Time);
    tl.Wait(MagicFx.BombFlightTime);
    if (context.Fx != null) context.Fx.BombBlast(landing, area, tl.Time);
    tl.Wait(0.15f);

    // 3. 範囲の駒はすべて消失（取りではないので盾は関係ない。火薬は MatchConfig.ChainExplosion のときだけ誘爆する）。
    //    駒は、いちばん近い爆弾のマスから外へ吹き飛ぶ
    List<Vector2Int> chain = new List<Vector2Int>();
    int[] lost = new int[2];
    foreach (GamePiece q in victims)
    {
      if (!tl.IsAlive(q)) continue;
      Vector2Int at = new Vector2Int(q.X, q.Y);
      if (MatchConfig.ChainExplosion && SpecialContext.IsExplosive(q)) chain.Add(at);
      Vector2Int c = NearestLanding(landing, at);
      PlayerType owner = q.player;
      if (tl.Vanish(q, Reason, new Vector2Int(System.Math.Sign(at.x - c.x), System.Math.Sign(at.y - c.y)))) lost[(int)owner]++;
    }
    tl.Wait(0.5f);
    foreach (Vector2Int c in chain) system.Explode(c.x, c.y, tl.Time);

    int total = lost[0] + lost[1];
    Debug.Log(total > 0
      ? $"ボムが炸裂！ 駒 {total} 枚が消失（先手 {lost[0]}・後手 {lost[1]}）"
      : "ボムが炸裂！ 巻き込まれた駒はなかった");
    tl.Wait(0.4f);
  }

  // マス s に一番近い、爆弾を投げたマス（駒が吹き飛ぶ向きの基準。筋の差で比べる）
  static Vector2Int NearestLanding(List<Vector2Int> landing, Vector2Int s)
  {
    Vector2Int best = landing[0];
    foreach (Vector2Int c in landing)
    {
      if (Mathf.Abs(c.y - s.y) < Mathf.Abs(best.y - s.y)) best = c;
    }
    return best;
  }
}
