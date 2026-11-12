using System.Collections.Generic;
using UnityEngine;

// 通常魔法「ワープ」（Prototype の src/defs/magic/warp.js）。対象を 2 段階で選ぶ：
//  1. ワープさせる自分の駒（王将も可。アンデッドは不可。時間停止中に消失・爆発の予定ができた駒も不可）
//  2. 行き先のマス（駒がいない通常のマス＝破壊・炎上・落とし穴でないマス。自陣・中立。自分の駒が 1 つでも敵陣にいれば敵陣も）
// 移動ではないので、取り・成り・必殺技の移動回数にはならない。行き所のない駒になるマス
// （成っていない歩・香の最奥段、桂の奥 2 段。持ち駒を打つときと同じ）には飛ばせない。時間停止中も使える
[CreateAssetMenu(menuName = "UltimateShogi/Effects/Warp")]
public class WarpEffect : SkillEffect
{
  public override int TargetCount => 1;
  public override bool UsesDestination => true;

  public override string GetTargetPrompt(int index)
  {
    return "ワープさせる自分の駒を選択";
  }

  public override string GetDestinationPrompt()
  {
    return "ワープの行き先のマスを選択";
  }

  // 行き先が 1 つ以上ある自分の駒（Prototype の getTargets）
  public override bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    GamePiece piece = context.PieceAt(pos);
    if (!IsWarpable(context, piece)) return false;
    for (int x = 0; x < BoardCells.Size; x++)
    {
      for (int y = 0; y < BoardCells.Size; y++)
      {
        if (CanWarpTo(context, piece, new Vector2Int(x, y))) return true;
      }
    }
    return false;
  }

  // 選んだ駒（context.Targets[0]）を to へ飛ばせるか（Prototype の getDestinations）
  public override bool IsValidDestination(SkillContext context, Vector2Int to)
  {
    if (context.Targets.Count == 0) return false;
    GamePiece piece = context.PieceAt(context.Targets[0]);
    return IsWarpable(context, piece) && CanWarpTo(context, piece, to);
  }

  // ワープさせられる自分の駒か（アンデッド・時間停止で消失・爆発の予定ができた駒は不可。予定から逃げられないように）
  static bool IsWarpable(SkillContext context, GamePiece piece)
  {
    return MagicKit.IsOwnPiece(context, piece) && !context.GameSystem.IsFrozenInTimeStop(piece);
  }

  // 持ち主の駒 piece を to へ飛ばせるか（Prototype の warp.js の destinations）
  static bool CanWarpTo(SkillContext context, GamePiece piece, Vector2Int to)
  {
    GameBoard board = context.GameBoard;
    if (!MagicKit.IsEmptyNormal(board, to)) return false;
    if (BoardZones.IsEnemyZone(piece.player, to.x) && !HasInvaded(board, piece.player)) return false;
    return !MagicKit.IsDeadEnd(piece, to.x);
  }

  // 自分の駒が 1 つでも敵陣にいるか（いれば敵陣にもワープできる。ワープさせる駒自身が敵陣にいてもよい）
  static bool HasInvaded(GameBoard board, PlayerType owner)
  {
    for (int x = 0; x < BoardCells.Size; x++)
    {
      if (!BoardZones.IsEnemyZone(owner, x)) continue;
      for (int y = 0; y < BoardCells.Size; y++)
      {
        GamePiece q = board.GetPieceAt(x, y);
        if (q != null && q.player == owner) return true;
      }
    }
    return false;
  }

  public override void Execute(SkillContext context)
  {
    if (context.Targets.Count == 0 || !context.Destination.HasValue) return;
    GamePiece piece = context.PieceAt(context.Targets[0]);
    Vector2Int from = context.Targets[0];
    Vector2Int to = context.Destination.Value;
    SpecialContext tl = context.Timeline;
    if (piece == null) return;
    string name = HandUIManager.GetJapaneseName(piece.type);

    if (tl == null)
    {
      context.GameBoard.UpdateBoardData(from.x, from.y, to.x, to.y);
      piece.X = to.x;
      piece.Y = to.y;
      piece.MoveTo(GameHelper.CalcPanelLocation(to.x, to.y));
      context.GameSystem.EnterCell(piece, to.x, to.y);
      return;
    }

    // 1. 元のマスと行き先に魔法陣が開き、駒に光が吸い込まれる
    if (context.Fx != null) context.Fx.WarpOut(from, to, tl.Time);
    tl.Wait(MagicFx.WarpOutTime);

    // 2. 駒がその場で縦に縮んで消え、行き先で伸びて現れる（盤面のデータはすぐ動く）
    if (!tl.Teleport(piece, to.x, to.y)) return;
    if (context.Fx != null) context.Fx.WarpMove(piece, to, tl.Time);
    tl.Wait(MagicFx.WarpMoveTime);

    // 3. 行き先に現れた駒から光が弾ける
    if (context.Fx != null) context.Fx.WarpIn(to, tl.Time);
    Debug.Log($"{TurnEndAction.SideName(context.Player)}：({from.x}, {from.y}) の{name}を ({to.x}, {to.y}) へワープさせた！");
    tl.Wait(0.2f);
  }
}
