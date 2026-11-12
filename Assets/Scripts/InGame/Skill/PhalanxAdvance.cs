using System.Collections.Generic;
using UnityEngine;

// ファランクス（必殺魔法）の自動行動（Prototype の src/core/turn.js の runPhalanx・phalanxAct・phalanxStrike）。
// 持ち主の手番の行動のあと・手番交代の前に、ファランクス状態の駒を盤の奥（相手の陣地側）にいる駒から順に動かす。
//  1. 1 マス前進する。盤外・破壊マス・駒（味方・相手とも）で塞がっていたら前進しない。
//     炎上・落とし穴のマスなら入って消失、火薬なら爆発（SpecialContext.Step）。敵陣に入る・出ると成る
//  2. 生き残っていれば（前進したかどうかに関係なく）前方 1×3 マスの相手の駒を取る（盾は有効。相手の火薬は爆発）
// 盾が割れてもファランクス状態は続く。取られて持ち駒になると解除される（駒の GameObject ごと消える）
public class PhalanxAdvance : TurnEndAction
{
    public static readonly Color PhalanxColor = SpecialMove.Hex(0xffc400);

    // 演出の長さ（MagicFx の PhalanxStep・PhalanxThrust と合わせる）
    public const float StepPause = 0.15f;  // 槍が弾んでから動き出すまで
    public const float MoveTime = 0.3f;    // 1 マス前進
    public const float ThrustTime = 0.12f; // 槍の穂先が届くまで

    public override string Title => "ファランクス";
    public override string CutInName => "ファランクス前進！";
    public override string GetCutInSub(PlayerType owner) => $"{SideName(owner)}・必殺魔法";
    public override Color Color => PhalanxColor;
    public override string GetStatusText(PlayerType owner) => $"{SideName(owner)}のファランクスが前進！";

    public override bool HasWork(GameSystem system, PlayerType owner)
    {
        GameBoard board = system != null ? system.Board : null;
        return board != null && Pieces(board, owner).Count > 0;
    }

    // owner のファランクス状態の駒を、盤の奥（相手の陣地側）にいる駒から順に並べる（同じ段は筋の小さい方から。Prototype の phalanxPieces）
    public static List<GamePiece> Pieces(GameBoard board, PlayerType owner)
    {
        List<GamePiece> list = new List<GamePiece>();
        foreach (GamePiece p in board.GetBoardData())
        {
            if (p != null && p.player == owner && p.hasPhalanx) list.Add(p);
        }
        int f = SpecialContext.Forward(owner);
        list.Sort((a, b) =>
        {
            int byRank = (b.X - a.X) * f;
            return byRank != 0 ? byRank : a.Y - b.Y;
        });
        return list;
    }

    public override void Run(TurnEndContext context)
    {
        foreach (GamePiece p in Pieces(context.Board, context.Owner))
        {
            if (!context.Timeline.IsAlive(p)) continue; // 先に動いた駒の爆発などで消えた
            Act(context, p);
            if (AnyKingLost(context.Board)) return;      // 王将を取った・巻き込んだ（勝敗は GameSystem が判定する）
        }
        context.Timeline.Wait(0.2f);
    }

    // 駒 p の自動行動：1 マス前進（塞がっていれば前進しない）→ 前方 1×3 の相手の駒を取る
    static void Act(TurnEndContext context, GamePiece p)
    {
        SpecialContext tl = context.Timeline;
        GameSystem system = context.GameSystem;
        int f = SpecialContext.Forward(p.player);
        int fromX = p.X, fromY = p.Y;
        int tx = p.X + f, ty = p.Y;
        string name = HandUIManager.GetJapaneseName(p.type);

        if (context.Fx != null) context.Fx.PhalanxStep(p, tl.Time);
        tl.Wait(StepPause);

        GamePiece q = SpecialContext.InBounds(tx, ty) ? tl.PieceAt(tx, ty) : null;
        // 盤外・破壊マス（翼の駒は除く）・駒で塞がっていれば前進しない（鳥は火薬のマスでも止まる＝爆発させない）
        if (SpecialContext.InBounds(tx, ty) && !tl.IsBlockedFor(p, tx, ty) && (q == null || PieceRules.Detonates(p, q)))
        {
            Debug.Log($"{TurnEndAction.SideName(p.player)}：ファランクスの{name}が ({fromX}, {fromY}) → ({tx}, {ty}) へ前進{(q != null ? "（火薬に突入！）" : "")}");
            tl.Afterimage(p, PhalanxColor);
            bool? alive = tl.Step(p, tx, ty, PieceMoveStyle.Normal, MoveTime);
            if (alive != true) return; // 炎上・落とし穴・火薬で消失した
            tl.Landed(p, 3);
            // 敵陣に入る・出ると成る（Prototype の tryPromote。自動で成る）
            if (system.CanPromote(p, fromX, p.X))
            {
                p.isPromoted = true;
                tl.Schedule(p.UpdateVisual);
            }
        }
        else
        {
            Debug.Log($"{TurnEndAction.SideName(p.player)}：ファランクスの{name}（({fromX}, {fromY})）は前が塞がっていて前進できない");
            if (tl.Fx != null) tl.Fx.Bump(p, new Vector2Int(f, 0), tl.Time);
            tl.Wait(0.14f);
        }
        Strike(context, p);
    }

    // ファランクスの槍：前方 1×3 の相手の駒を取る（盾は有効。火薬は爆発）
    static void Strike(TurnEndContext context, GamePiece p)
    {
        SpecialContext tl = context.Timeline;
        List<GamePiece> targets = MagicKit.FrontEnemies(context.Board, p);
        if (targets.Count == 0) return;

        if (context.Fx != null) context.Fx.PhalanxThrust(p, targets, tl.Time);
        tl.Wait(ThrustTime);

        int f = SpecialContext.Forward(p.player);
        foreach (GamePiece q in targets)
        {
            if (!tl.IsAlive(q) || SpecialContext.IsExplosive(q)) continue;
            string name = HandUIManager.GetJapaneseName(q.type);
            int qx = q.X, qy = q.Y;
            if (tl.Capture(q, new Vector2Int(f, q.Y - p.Y), p))
            {
                Debug.Log($"({qx}, {qy})：{q.player} の{name}をファランクスの槍で取った！");
            }
        }
        foreach (GamePiece q in targets)
        {
            if (tl.IsAlive(q) && SpecialContext.IsExplosive(q)) tl.Explode(q.X, q.Y);
        }
        tl.Wait(0.2f);
    }

    static bool AnyKingLost(GameBoard board)
    {
        bool p1 = false, p2 = false;
        foreach (GamePiece q in board.GetBoardData())
        {
            if (q == null || q.type != PieceType.King) continue;
            if (q.player == PlayerType.Player1) p1 = true;
            else p2 = true;
        }
        return !p1 || !p2;
    }
}
