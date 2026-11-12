using System.Collections.Generic;
using UnityEngine;

// 必殺技 1 回分の実行環境（Prototype の src/core/context.js の、必殺技が使う部分）。
//
// 盤面のデータはその場ですぐに変わり、演出は「発動からの秒数」Time に合わせて SpecialMoveFx が後から再生する。
// 技は Step・Capture・Push などを順に呼ぶだけでよい（呼ぶたびに Time が進み、演出の順番がそろう）。
// 駒を倒すのは必ず GameSystem.CapturePiece（取り）/ VanishPiece（消失）/ Explode を通す（このクラスもそれを呼ぶ）
public class SpecialContext
{
    // 周りの駒 1 つ分（Neighbors の結果）
    public struct Neighbor
    {
        public GamePiece Piece;
        public int DX; // 発動駒から見た向き（Unity の X ＝ 段）
        public int DY; // 〃（Unity の Y ＝ 筋）
    }

    public GameSystem GameSystem { get; }
    public GameBoard Board { get; }
    public GamePiece Caster { get; }
    public PlayerType Owner { get; }
    public SpecialMoveFx Fx { get; }          // 演出（null なら演出なしで盤面だけ変える）

    // 対象を選ぶ技で選んだマス（選ばない技は null）
    public Vector2Int? Target { get; set; }

    // 演出の時刻（発動からの秒数）。Step・Wait などで進む。
    // いくつかの駒を同時に動かすときは、開始時刻に戻してから 1 つずつ処理する（金剛衝）
    public float Time { get; set; }

    // 次に倒される駒の演出用（SpecialMoveFx が GameSystem.PieceDefeated で読む）
    public float HitTime { get; private set; }           // 倒される瞬間
    public Vector2Int HitDirection { get; private set; } // 飛ばされる向き（(0,0) ならその場で落ちて消える）

    public SpecialContext(GameSystem system, GameBoard board, GamePiece caster, SpecialMoveFx fx)
    {
        GameSystem = system;
        Board = board;
        Caster = caster;
        Owner = caster != null ? caster.player : PlayerType.Player1;
        Fx = fx;
    }

    // 発動する駒がない行動（魔法スキル）の時間軸。Caster は null
    public SpecialContext(GameSystem system, GameBoard board, PlayerType owner, SpecialMoveFx fx)
    {
        GameSystem = system;
        Board = board;
        Caster = null;
        Owner = owner;
        Fx = fx;
    }

    // ------------------------------------------------------------
    //  盤面を見る（判定だけ。CanUse・GetTargets からも使う）
    // ------------------------------------------------------------
    public BoardCells Cells => Board.Cells;

    // 前方（Unity の盤は X が段。Player1 は +X へ進む）
    public static int Forward(PlayerType player)
    {
        return player == PlayerType.Player1 ? 1 : -1;
    }

    public static bool InBounds(int x, int y) => BoardCells.InBounds(x, y);
    public GamePiece PieceAt(int x, int y) => Board.GetPieceAt(x, y);
    public bool IsBroken(int x, int y) => Cells != null && Cells.IsBroken(x, y);
    public bool IsHazard(int x, int y) => Cells != null && Cells.IsHazard(x, y); // 炎上・落とし穴（入ると消失）
    public static bool IsExplosive(GamePiece q) => q != null && PieceRules.IsExplosive(q.type);
    // 駒ごとのマスの効果（翼状態の駒は破壊マスにも入れて、炎上・落とし穴で消失しない。PieceRules の翼状態の判定）
    public bool IsBlockedFor(GamePiece p, int x, int y) => PieceRules.IsBlockedFor(Cells, p, x, y); // 入れない・通れない（破壊マス・壁）
    public bool IsHazardFor(GamePiece p, int x, int y) => PieceRules.IsHazardFor(Cells, p, x, y);   // 入ると消失（炎上・落とし穴）

    // 駒がまだ盤上にいるか
    public bool IsAlive(GamePiece p)
    {
        return p != null && Board.GetPieceAt(p.X, p.Y) == p;
    }

    // 駒 p が (x, y) に入れるか：破壊マス以外の 空き / 相手の駒 / 火薬（Prototype の board.js の canLandOn）。
    // 翼状態の駒は破壊マスにも入れる。鳥は味方の火薬には入れない
    public bool CanLandOn(GamePiece p, int x, int y)
    {
        if (!InBounds(x, y) || IsBlockedFor(p, x, y)) return false;
        return PieceRules.CanLandOn(p, PieceAt(x, y));
    }

    // 周囲 8 マスの駒
    public List<Neighbor> Neighbors(GamePiece p)
    {
        List<Neighbor> result = new List<Neighbor>();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                GamePiece q = PieceAt(p.X + dx, p.Y + dy);
                if (q != null) result.Add(new Neighbor { Piece = q, DX = dx, DY = dy });
            }
        }
        return result;
    }

    // ------------------------------------------------------------
    //  時間・演出
    // ------------------------------------------------------------

    // 次に倒される駒の演出を、今の時刻 Time・向き dir にする（時間停止の解除で、予定をまとめて処理するときに使う）
    public void MarkHit(Vector2Int dir)
    {
        HitTime = Time;
        HitDirection = dir;
    }

    // 演出を sec 秒待つ（このあとの演出・取りの見た目が遅れる）
    public void Wait(float sec)
    {
        if (sec > 0f) Time += sec;
    }

    // 今の時刻 Time に action を行う（演出がなければすぐ行う）
    public void Schedule(System.Action action)
    {
        if (action == null) return;
        if (Fx != null) Fx.At(Time, action);
        else action();
    }

    // 残像（Prototype の ghost）
    public void Afterimage(GamePiece p, Color color)
    {
        if (Fx != null) Fx.Afterimage(p, color, Time);
    }

    // 着地の衝撃（Prototype の landed）
    public void Landed(GamePiece p, int power)
    {
        if (Fx != null) Fx.Landed(p, power, Time);
    }

    // ------------------------------------------------------------
    //  盤面を変える
    // ------------------------------------------------------------

    // 駒 p を (tx, ty) へ動かす（Prototype の play.js の stepPiece）。duration 秒かけて動く見た目にする。
    //  ・移動先の相手の駒は取る（CapturePiece）
    //  ・移動先が火薬なら重なった瞬間に爆発する（王将以外の p も吹き飛ぶ。鳥は爆発させずに火薬を取る：PieceRules.Detonates）
    //  ・炎上・落とし穴のマスなら消失する（EnterCell。翼状態の駒は消失しない）
    // 戻り値: 移動先に入れたら true / 消失したら false / 取りが防がれて元のマスに残ったら null
    public bool? Step(GamePiece p, int tx, int ty, PieceMoveStyle style, float duration)
    {
        if (!IsAlive(p) || !InBounds(tx, ty)) return false;

        GamePiece target = PieceAt(tx, ty);
        Vector2Int dir = new Vector2Int(System.Math.Sign(tx - p.X), System.Math.Sign(ty - p.Y));
        float arrive = Time + duration;
        GameSystem.RecordTimeStopMove(p, tx, ty); // 時間停止中は 1 マスごとの移動を記録する（盾で押し戻すときに使う）

        if (target != null && target != p)
        {
            if (PieceRules.Detonates(p, target))
            {
                // 火薬のマスに重なった → 即爆発（爆発の演出が p を火薬のマスまで運ぶ）
                if (Fx != null) Fx.MovePiece(p, tx, ty, style, duration, Time);
                GameSystem.Explode(tx, ty, arrive, p);
                Time = arrive;
                if (!IsAlive(p)) return false;
                if (Board.HasPieceAt(tx, ty)) return null;
                MoveData(p, tx, ty);
                return EnterCell(p, tx, ty);
            }

            HitTime = arrive;
            HitDirection = dir;
            if (!GameSystem.CapturePiece(target, p.player, p))
            {
                // 取りが防がれた（盾など）：ぶつかって元のマスへ戻る
                if (Fx != null) Fx.Bump(p, dir, Time);
                Time = arrive;
                return null;
            }
        }

        MoveData(p, tx, ty);
        if (Fx != null) Fx.MovePiece(p, tx, ty, style, duration, Time);
        else p.MoveTo(GameHelper.CalcPanelLocation(tx, ty));
        Time = arrive;
        return EnterCell(p, tx, ty);
    }

    // 相手の駒 target を取る（CapturePiece）。dir は飛ばされる向き（演出用）。attacker は取ろうとした駒（省略すると Caster）。
    // 取れたら true（盾で防がれたら false）
    public bool Capture(GamePiece target, Vector2Int dir, GamePiece attacker = null)
    {
        if (!IsAlive(target)) return false;
        HitTime = Time;
        HitDirection = dir;
        return GameSystem.CapturePiece(target, Owner, attacker != null ? attacker : Caster);
    }

    // 駒 q を消失させる（VanishPiece。エナジー・持ち駒化なし。盾では防げない）。dir は飛ばされる向き（演出用。(0,0) ならその場で消える）。
    // 隕石・破壊砲など。火薬もこれで消すと爆発しない。消えたら true
    public bool Vanish(GamePiece q, string reason, Vector2Int dir)
    {
        if (!IsAlive(q)) return false;
        HitTime = Time;
        HitDirection = dir;
        return GameSystem.VanishPiece(q, reason);
    }

    // (x, y) の火薬を爆発させる（爆発の演出は今の時刻から）
    public void Explode(int x, int y)
    {
        GameSystem.Explode(x, y, Time);
    }

    // 駒 q を (dx, dy) へ 1 マス押し出す（Prototype の play.js の pushPiece。金剛衝）。
    //  盤外 … 取り（持ち駒になる。火薬は持ち駒にならない）
    //  空き・火薬 … そのマスへ動く（炎上・落とし穴なら消失、火薬なら爆発）。翼状態の駒は破壊マスへも押し出される
    //  駒・破壊マス … ぶつかって止まる（鳥は火薬のマスでも止まる＝爆発させない）
    public void Push(GamePiece q, int dx, int dy)
    {
        if (!IsAlive(q)) return;
        int tx = q.X + dx;
        int ty = q.Y + dy;
        Vector2Int dir = new Vector2Int(dx, dy);

        if (!InBounds(tx, ty))
        {
            if (Fx != null) Fx.PushOut(q, Time);
            if (!Capture(q, dir) && Fx != null) Fx.Bump(q, dir, Time);
            return;
        }

        GamePiece d = PieceAt(tx, ty);
        if (!IsBlockedFor(q, tx, ty) && (d == null || PieceRules.Detonates(q, d)))
        {
            Afterimage(q, SpecialMove.Hex(0xffd54a));
            Step(q, tx, ty, PieceMoveStyle.Dash, 0.14f);
            return;
        }

        if (Fx != null) Fx.Bump(q, dir, Time); // 押し出し先が塞がっている
    }

    // 盤面のデータだけ動かす（見た目は呼び出し側）
    void MoveData(GamePiece p, int tx, int ty)
    {
        Board.UpdateBoardData(p.X, p.Y, tx, ty);
        p.X = tx;
        p.Y = ty;
    }

    // マスの効果（炎上・落とし穴で消失）。残れたら true
    bool EnterCell(GamePiece p, int x, int y)
    {
        HitTime = Time;
        HitDirection = Vector2Int.zero;
        return GameSystem.EnterCell(p, x, y);
    }
}
