using System.Collections.Generic;
using UnityEngine;

// 超常魔法「アンデッド・パラダイス」（Prototype の src/defs/magic/undead_paradise.js・src/core/effects.js の reviveUndead）。
// 倒された自分の初期配置の駒（王将以外。取り・消失どちらでも。MatchState.FallenInitial）を、
// 自陣のランダムな空きマス（駒がなく、破壊・炎上・落とし穴でないマス）に、成っていない状態でアンデッドとして復活させる。
//  ・空きマスが足りない分は復活しない（記録はまとめて消える。Prototype と同じ）。相手の持ち駒は減らない
//  ・マスは対局の乱数（GameRng）でシャッフルして、倒された順に割り当てる
//  ・アンデッドは持ち主が操作できず、必殺技も使えず、自分の魔法の対象にも選べない。持ち主の手番の終わりに自動で動く（UndeadMarch）。
//    相手の攻撃は通常どおり受け、倒されると消失する（成仏）
// 演出：自陣が瘴気に染まる → 駒が次々と地面から這い上がる →「アンデッド 復活！」（MagicFx.Undead.cs）
[CreateAssetMenu(menuName = "UltimateShogi/Effects/UndeadParadise")]
public class UndeadParadiseEffect : SkillEffect
{
  public const float RiseInterval = 0.12f; // 1 体ずつ這い上がる間隔

  // 復活できる駒と、自陣の空きマスが 1 つ以上あるときに使える
  public override bool CanUse(SkillContext context)
  {
    MatchState match = context.GameSystem != null ? context.GameSystem.Match : null;
    return MagicKit.RevivableFallen(match, context.Player).Count > 0
        && MagicKit.OwnZoneEmptySquares(context.GameBoard, context.Player).Count > 0;
  }

  public override void Execute(SkillContext context)
  {
    GameSystem system = context.GameSystem;
    MatchState match = system.Match;
    PlayerType owner = context.Player;
    SpecialContext tl = context.Timeline;
    MagicFx fx = tl != null ? context.Fx : null;

    List<PieceType> fallen = MagicKit.RevivableFallen(match, owner);
    List<Vector2Int> squares = match.Rng.Shuffle(MagicKit.OwnZoneEmptySquares(context.GameBoard, owner));
    int n = Mathf.Min(fallen.Count, squares.Count); // 空きマスが足りない分は復活しない
    match.ClearFallenInitial(owner);

    // 自陣が瘴気に染まり、倒れた駒が次々と地面から這い上がる
    if (fx != null)
    {
      fx.UndeadVeil(owner, n, tl.Time);
      tl.Wait(0.35f);
    }

    Dictionary<PieceType, int> counts = new Dictionary<PieceType, int>();
    List<PieceType> kinds = new List<PieceType>();
    for (int i = 0; i < n; i++)
    {
      GamePiece piece = system.ReviveUndead(fallen[i], owner, squares[i].x, squares[i].y);
      if (piece == null) continue;
      if (fx != null) fx.UndeadRise(piece, tl.Time);
      if (!counts.ContainsKey(fallen[i]))
      {
        counts[fallen[i]] = 0;
        kinds.Add(fallen[i]);
      }
      counts[fallen[i]]++;
      if (tl != null) tl.Wait(RiseInterval);
    }

    if (fx != null)
    {
      tl.Wait(0.3f);
      fx.UndeadDone(owner, tl.Time);
    }

    List<string> names = kinds.ConvertAll(t => HandUIManager.GetJapaneseName(t) + (counts[t] > 1 ? $"×{counts[t]}" : ""));
    int lost = fallen.Count - n;
    Debug.Log($"{TurnEndAction.SideName(owner)}：{string.Join("・", names)} がアンデッドとして復活した！" +
        (lost > 0 ? $"（空きマスが足りず、{lost} 体は復活できなかった）" : ""));
    if (tl != null) tl.Wait(0.6f);
  }
}
