using System.Collections.Generic;
using UnityEngine;

// 時間停止（超常魔法「停止世界」）の状態（Prototype の src/core/timeStop.js の s.timeStop）。
// 時間停止中は GameSystem.TimeStop にあり、それ以外は null。予定にする処理・解除の処理は GameSystem.TimeStop.cs。
//
//  ・Remaining / Total … 残り / 最初に行動できた回数（移動・打つ・魔法・必殺技・狙撃が各 1 回。錬成は数えない）
//  ・Ending            … 回数を使い切った・「解除」を押した（このあと予定をまとめて処理する）
//  ・Moves            … 時間停止中の駒の移動（1 マスごと）の記録。盾で押し戻すときに来た道をたどるのに使う
//  ・Pending          … 効果の予定。記録した順に、時間が動き出したときに処理する
//  ・Held             … 盤のデータから外して、その場に残している駒（取られる予定の駒・消える予定の駒・爆発する予定の火薬）。
//                       以降の判定では存在しないものとして扱う（その上を通ったり止まったりできる。持ち駒は打てない）
// Prototype は状態を JSON にするため駒・移動を id・添字で持つが、Unity では参照で持つ
public class TimeStopState
{
    public PlayerType Owner { get; }
    public int Total { get; }
    public int Remaining { get; set; }
    public bool Ending { get; set; }
    // 停止世界を発動した行動のあと（その行動は回数に数えない）
    public bool JustStarted { get; set; } = true;

    public List<TimeStopMove> Moves { get; } = new List<TimeStopMove>();
    public List<TimeStopPending> Pending { get; } = new List<TimeStopPending>();
    public HashSet<GamePiece> Held { get; } = new HashSet<GamePiece>();

    public TimeStopState(PlayerType owner, int moves)
    {
        Owner = owner;
        Total = moves;
        Remaining = moves;
    }

    // 予定 e のもとになった移動が、盾で押し戻されて取り消されたか
    public static bool IsUndone(TimeStopPending e)
    {
        return e.Move != null && e.Move.Undone;
    }

    // 予定 e のために残している駒（取られる予定の駒・消える予定の駒・爆発する予定の火薬）。なければ null
    public GamePiece HeldPiece(TimeStopPending e)
    {
        GamePiece q = e.Target;
        return q != null && Held.Contains(q) ? q : null;
    }

    // 盤のデータから外して残している駒か
    public bool IsHeld(GamePiece p)
    {
        return p != null && Held.Contains(p);
    }

    // 消失・爆発の予定ができた駒か（その駒はもう動かせず、必殺技・狙撃も使えない）
    public bool IsFrozen(GamePiece p)
    {
        if (p == null) return false;
        foreach (TimeStopPending e in Pending)
        {
            if (e.Piece == p && (e.Kind == TimeStopKind.Vanish || e.Kind == TimeStopKind.Explode) && !IsUndone(e)) return true;
        }
        return false;
    }

    // (x, y) に予定の駒が残っているか（持ち駒を打てない・魔法の空きマスにしない）
    public bool IsHeldSquare(int x, int y)
    {
        foreach (TimeStopPending e in Pending)
        {
            GamePiece q = HeldPiece(e);
            if (!e.Done && q != null && q.X == x && q.Y == y) return true;
        }
        return false;
    }

    // (x, y) にマスの状態の変化（落とし穴の設置・破壊・炎上）の予定があるか
    public bool HasPendingCell(int x, int y)
    {
        foreach (TimeStopPending e in Pending)
        {
            if (e.Kind == TimeStopKind.Cell && !e.Done && e.At.x == x && e.At.y == y) return true;
        }
        return false;
    }

    // 駒 p にもう成りの予定があるか（成りの予定は 1 つの駒につき 1 回）
    public bool HasPendingPromote(GamePiece p)
    {
        foreach (TimeStopPending e in Pending)
        {
            if (e.Kind == TimeStopKind.Promote && e.Piece == p && !IsUndone(e)) return true;
        }
        return false;
    }

    // 駒 p の時間停止中の最後の移動（取り消されていないもの）。to を指定すると、そのマスへの移動のときだけ返す。なければ null
    public TimeStopMove LastMove(GamePiece p, Vector2Int? to = null)
    {
        if (p == null) return null;
        for (int i = Moves.Count - 1; i >= 0; i--)
        {
            TimeStopMove m = Moves[i];
            if (m.Piece != p || m.Undone) continue;
            return !to.HasValue || m.To == to.Value ? m : null;
        }
        return null;
    }

    // owner の王将が、予定の駒として残っているか（取られる予定の王将はまだ倒されていない）
    public bool HasHeldKing(PlayerType owner)
    {
        foreach (GamePiece q in Held)
        {
            if (q != null && q.player == owner && q.type == PieceType.King) return true;
        }
        return false;
    }
}

// 時間停止中の 1 マスの移動
public class TimeStopMove
{
    public GamePiece Piece;
    public Vector2Int From;
    public Vector2Int To;
    public bool Captured;   // 移動先の相手の駒を取る移動（火薬を除く）
    public bool Bounced;    // その取りが、時間が動き出したときに盾に防がれた
    public bool Undone;     // 盾で押し戻されて取り消された
    public bool Counted;    // 必殺技の移動回数に数えた（取り消したら戻す）
}

public enum TimeStopKind { Capture, Explode, Vanish, Promote, Cell }
public enum TimeStopCellEffect { Trap, Break, Burn }

// 時間停止の予定 1 つ（Prototype の pending の 1 件）
public class TimeStopPending
{
    public TimeStopKind Kind;
    public GamePiece Piece;        // 関係する駒（取った駒・火薬に入った駒・マスに入った駒・成る駒。なければ null）
    public TimeStopMove Move;      // その駒の移動の記録（盾で押し戻されたら、この予定は飛ばす。なければ null）
    public Vector2Int At;          // マス
    public bool Done;              // 処理済み

    public GamePiece Target;       // 取られる駒・消える駒（隕石・破壊砲など）・爆発する火薬（なければ null）
    public PieceType TargetType;   // ログ用（Target の種類）
    public PlayerType ByOwner;     // 取り：取った側
    public string Reason;          // 消失の理由（炎上・落とし穴・隕石 など）
    public Vector2Int HitDirection; // 倒されるときに飛ばされる向き（演出用）

    public TimeStopCellEffect CellEffect; // マスの変化の種類
    public PlayerType CellOwner;          // 落とし穴を仕掛けたプレイヤー
    public int Turns;                     // 炎上のターン数
}
