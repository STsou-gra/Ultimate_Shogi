// 駒を倒す処理（取り・消失）1回分の情報。GameSystem.CapturePiece / VanishPiece が作り、割り込み・通知に渡す
public class PieceDefeat
{
    public GamePiece Piece { get; }
    public DefeatKind Kind { get; }
    public PlayerType? By { get; }   // 取った側（消失のときは null）
    public string Reason { get; }    // 消失の理由（"爆発" など。取りのときは "取り"）

    // 倒された時点の情報（駒の GameObject は倒した直後に破棄されるので、通知ではこちらを使う）
    public PieceType Type { get; }
    public PlayerType Owner { get; }
    public int X { get; }
    public int Y { get; }
    public bool WasPromoted { get; }
    public bool WasInitial { get; }

    public PieceDefeat(GamePiece piece, DefeatKind kind, PlayerType? by, string reason)
    {
        Piece = piece;
        Kind = kind;
        By = by;
        Reason = reason;
        Type = piece.type;
        Owner = piece.player;
        X = piece.X;
        Y = piece.Y;
        WasPromoted = piece.isPromoted;
        WasInitial = piece.IsInitial;
    }
}

// 駒を倒す処理への割り込み（盾・時間停止・アンデッドなど、これから入る仕組み用）。
// GameSystem.AddDefeatInterceptor で登録すると、取り・消失のたびに登録順に呼ばれる。
// true を返すとその場では倒さない（盾で防いだ・時間停止の予定にした など。駒はそのまま盤に残る）
public interface IPieceDefeatInterceptor
{
    bool TryIntercept(PieceDefeat defeat);
}
