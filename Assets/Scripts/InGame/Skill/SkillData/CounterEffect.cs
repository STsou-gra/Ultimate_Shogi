using UnityEngine;

// 通常魔法「カウンター」（Prototype の src/defs/magic/counter.js・tests/core/counter.test.js）。
// カウンター状態ではない自分の駒（王将も可。アンデッドは不可。Prototype の巨人駒は Unity にはまだない）を 1 つ選び、カウンター状態にする。
// カウンター状態の駒は、相手の「取り」（移動・突進・光線・狙撃・剣・ファランクスの槍・場外への押し出しなど）を 1 回だけ無効にし、
// 攻撃した駒を元のマスへ戻す（突進技は手前で止まる。盾と同じ戻し方）。戻った駒が前方 1×3 マス（左前・前・右前）にいれば、
// そこへ動いて取る（反撃も「取り」なので盾は有効。移動回数には数えないが、敵陣に入る・出ると成る）。
// 消失（爆発・隕石・破壊砲など）と、停止世界の中での取りには効かない。盾状態でもあれば先にカウンターが効き、盾は残る。
// 取られて持ち駒になると解除される。判定は GameSystem.CapturePiece、反撃は GameSystem.ResolveCounters
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Counter")]
public class CounterEffect : SkillEffect
{
  public override int TargetCount => 1;

  public override string GetTargetPrompt(int index)
  {
    return "カウンター状態にする自分の駒を選択";
  }

  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    return MagicKit.IsOwnPiece(context, piece) && !piece.hasCounter;
  }

  public override void Execute(SkillContext context)
  {
    GamePiece piece = context.PieceAt(context.Targets[0]);
    if (piece == null) return;
    context.GameSystem.GiveCounter(piece);
    if (context.Timeline != null) context.Timeline.Wait(0.5f);
  }
}
