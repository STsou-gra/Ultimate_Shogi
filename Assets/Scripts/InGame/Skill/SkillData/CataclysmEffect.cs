using System.Collections.Generic;
using UnityEngine;

// 天変地異で 1 マス分が動いた記録（GameSystem.ShuffleCells の結果。演出が元のマスから新しいマスへ飛ばすのに使う）
public struct CellMove
{
  public Vector2Int From;   // 元のマス
  public Vector2Int To;     // 新しいマス
  public GamePiece Piece;   // 一緒に動いた駒（空きマスなら null）
  public bool Burning;      // 炎上のマスだった（炎も一緒に飛ぶ）
  public bool Trap;         // 落とし穴のマスだった（穴も一緒に飛ぶ）
  public bool Wall;         // 壁のマスだった（壁も一緒に飛ぶ）
}

// 超常魔法「天変地異」（Prototype の src/defs/magic/cataclysm.js）。
// 破壊されていないすべてのマスを、置かれている駒・マスの状態（炎上と残りターン数・落とし穴・壁）ごとランダムに入れ替える
// （対局の乱数 GameRng を使う）。王将も含めて全部の駒が動く。破壊されたマスは動かない。
// マスごとの入れ替えなので、成り・火薬の爆発・炎上・落とし穴は発動しない。
// 盾状態・ファランクス状態・必殺技の移動回数もそのまま。敵陣に移った駒は、次にそこから動いたときに成れる。
// 演出：地鳴り → 駒とマスの状態が宙に浮く → 一斉に新しいマスへ飛ぶ → 着地（MagicFx.Cataclysm）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Cataclysm")]
public class CataclysmEffect : SkillEffect
{
  public override void Execute(SkillContext context)
  {
    GameSystem system = context.GameSystem;
    SpecialContext tl = context.Timeline;

    // 盤面を入れ替える（駒の X・Y・盤の配列・マスの状態。見た目はまだ元のマス）
    List<CellMove> moves = system.ShuffleCells();

    int moved = 0;
    foreach (CellMove m in moves)
    {
      if (m.Piece != null && m.From != m.To) moved++;
    }

    if (context.Fx != null && tl != null)
    {
      // 元のマスから新しいマスへ飛ばす（炎上・落とし穴・壁の表示も駒と一緒に飛ぶ）
      context.Fx.Cataclysm(moves, tl.Time);
      tl.Wait(MagicFx.CataclysmTime + 0.6f);
    }
    else
    {
      // 演出なし：駒を新しいマスに置き直す（マスの状態の表示は BoardCellView が付け替え済み）
      foreach (CellMove m in moves)
      {
        if (m.Piece != null) m.Piece.MoveTo(GameHelper.CalcPanelLocation(m.To.x, m.To.y));
      }
    }
    Debug.Log($"天変地異！ 盤上の駒 {moved} 枚が、マスの状態ごと入れ替わった！");
  }
}
