using System.Collections.Generic;
using UnityEngine;

// アンデッドの自動行動（Prototype の src/core/turn.js の undeadPieces・undeadMoves・chooseUndeadMove・runUndeadPhase）。
// 持ち主の手番の 1 手が終わるたび（手番交代の前。アンデッド・パラダイスを発動した手番も含む）、ファランクスのあとに、
// 持ち主のアンデッドを相手の王将に近い駒から順に、全員 1 手ずつ自動で動かす。
//  手の選び方: 相手の王将を取れる手 → 相手の王将とのチェビシェフ距離が一番縮まる手
//              →（同じ距離なら）相手の駒（火薬を除く）を取る手 →（それでも同じなら）対局の乱数（GameRng）で選ぶ
//  動けるマスは通常の駒の動き（BaseMoveRule。破壊マス・壁には入れず、走り駒はその手前で止まる）から、炎上・落とし穴のマスを除いたもの。動ける手がなければ動かない
//  成り（敵陣に入る・出ると自動で成る）・取り・火薬の爆発は通常の移動と同じ
// 相手の王将が 2 枚以上あるとき（王将複製）は、駒ごとにいちばん近い王将へ向かう（Prototype は王将が 1 枚の前提）
public class UndeadMarch : TurnEndAction
{
    public static readonly Color UndeadColor = SpecialMove.Hex(0x43a047);

    // 演出の長さ（MagicFx.UndeadMarchStart と合わせる）
    public const float StartPause = 0.25f; // 鬼火が散ってから動き出すまで
    public const float StepGap = 0.1f;     // 1 体動いてから次が動くまで

    private static readonly BaseMoveRule MoveRule = new BaseMoveRule();

    private static readonly MiniCutInStyle Style = new MiniCutInStyle
    {
        NameBottom = SpecialMove.Hex(0x69f0ae),
        Outline = SpecialMove.Hex(0x1a0026),
        Burst = new[] { SpecialMove.Hex(0x76ff03), SpecialMove.Hex(0xb2ff59), SpecialMove.Hex(0x69f0ae), SpecialMove.Hex(0xccff90), SpecialMove.Hex(0x9c4dff) },
    };

    public override string Title => "アンデッド";
    public override string CutInName => "アンデッド進軍！";
    public override string GetCutInSub(GameSystem system, PlayerType owner)
    {
        int n = system != null && system.Board != null ? Pieces(system.Board, owner).Count : 0;
        return $"{SideName(owner)}のアンデッド {n} 体";
    }
    public override Color Color => UndeadColor;
    public override MiniCutInStyle CutInStyle => Style;
    public override string GetStatusText(PlayerType owner) => $"{SideName(owner)}のアンデッドが進軍する……";

    // 動ける駒が 1 体でもいて、相手の王将がいれば動く
    public override bool HasWork(GameSystem system, PlayerType owner)
    {
        GameBoard board = system != null ? system.Board : null;
        if (board == null || Kings(board, BoardZones.Opponent(owner)).Count == 0) return false;
        foreach (GamePiece p in Pieces(board, owner))
        {
            if (Moves(board, p).Count > 0) return true;
        }
        return false;
    }

    // owner のアンデッド（盤の順）
    public static List<GamePiece> Pieces(GameBoard board, PlayerType owner)
    {
        List<GamePiece> list = new List<GamePiece>();
        foreach (GamePiece p in board.GetBoardData())
        {
            if (p != null && p.player == owner && p.isUndead) list.Add(p);
        }
        return list;
    }

    // アンデッド p が動けるマス（Prototype の undeadMoves：通常の動きのうち、炎上・落とし穴のマスには入らない）
    public static List<Vector2Int> Moves(GameBoard board, GamePiece p)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (board.Cells.IsHazard(x, y)) continue;
                if (MoveRule.IsLegalMove(p, x, y, board)) result.Add(new Vector2Int(x, y));
            }
        }
        return result;
    }

    public override void Run(TurnEndContext context)
    {
        GameBoard board = context.Board;
        SpecialContext tl = context.Timeline;
        PlayerType enemy = BoardZones.Opponent(context.Owner);

        // 相手の王将に近い駒から順に（同じ距離なら盤の順）
        List<GamePiece> order = Pieces(board, context.Owner);
        List<int> dist = order.ConvertAll(p => KingDistance(board, p, enemy));
        List<int> index = new List<int>();
        for (int i = 0; i < order.Count; i++) index.Add(i);
        index.Sort((a, b) => dist[a] != dist[b] ? dist[a] - dist[b] : a - b);

        List<GamePiece> sorted = index.ConvertAll(i => order[i]);
        if (context.Fx != null) context.Fx.UndeadMarchStart(sorted, tl.Time);
        tl.Wait(StartPause);

        foreach (GamePiece p in sorted)
        {
            if (Kings(board, enemy).Count == 0 || Kings(board, context.Owner).Count == 0) break; // 王将を取った・爆発に巻き込んだ
            if (!tl.IsAlive(p)) continue;
            Vector2Int? m = Choose(context.GameSystem, board, p);
            if (!m.HasValue) continue; // 動ける手がなければ動かない
            Move(context, p, m.Value.x, m.Value.y);
            tl.Wait(StepGap);
        }
        tl.Wait(0.2f);
    }

    // アンデッド p の次の 1 手（動ける手がなければ null。Prototype の chooseUndeadMove）
    public static Vector2Int? Choose(GameSystem system, GameBoard board, GamePiece p)
    {
        List<Vector2Int> moves = Moves(board, p);
        if (moves.Count == 0) return null;
        PlayerType enemy = BoardZones.Opponent(p.player);
        List<GamePiece> kings = Kings(board, enemy);
        if (kings.Count == 0) return null;

        // 相手の王将を取れる手が最優先
        foreach (Vector2Int m in moves)
        {
            GamePiece q = board.GetPieceAt(m.x, m.y);
            if (q != null && q.player == enemy && q.type == PieceType.King) return m;
        }

        // 向かう王将（いちばん近い王将）とのチェビシェフ距離が一番縮まる手
        GamePiece king = NearestKing(kings, p);
        int best = int.MaxValue;
        foreach (Vector2Int m in moves) best = Mathf.Min(best, Chebyshev(m.x, m.y, king.X, king.Y));
        List<Vector2Int> closest = moves.FindAll(m => Chebyshev(m.x, m.y, king.X, king.Y) == best);

        // 同じ距離なら相手の駒（火薬を除く）を取る手。それでも同じなら対局の乱数で選ぶ
        List<Vector2Int> takes = closest.FindAll(m =>
        {
            GamePiece q = board.GetPieceAt(m.x, m.y);
            return q != null && q.player != p.player && !PieceRules.IsExplosive(q.type);
        });
        List<Vector2Int> pool = takes.Count > 0 ? takes : closest;
        GameRng rng = system != null && system.Match != null ? system.Match.Rng : null;
        return rng != null ? rng.Pick(pool) : pool[0];
    }

    // 1 手動かす（Prototype の play.js の move：取り・火薬の爆発・成り。移動回数も数える）
    static void Move(TurnEndContext context, GamePiece p, int tx, int ty)
    {
        SpecialContext tl = context.Timeline;
        GameSystem system = context.GameSystem;
        int fromX = p.X, fromY = p.Y;
        GamePiece target = tl.PieceAt(tx, ty);
        string name = HandUIManager.GetJapaneseName(p.type);
        string tail = target == null ? "" : SpecialContext.IsExplosive(target) ? "（火薬に突入！）"
            : $"（{SideName(target.player)}の{HandUIManager.GetJapaneseName(target.type)}を取る）";
        Debug.Log($"{SideName(p.player)}のアンデッド：{name} ({fromX}, {fromY}) → ({tx}, {ty}){tail}");

        PieceMoveStyle style = PieceMoveAnimator.StyleFor(p);
        float duration = style == PieceMoveStyle.Jump ? 0.35f : 0.28f;
        bool? alive = tl.Step(p, tx, ty, style, duration);
        if (alive != true) return; // 爆発で消えた・取りが防がれて元のマスに残った
        tl.Landed(p, 2);
        p.moveCount++;
        // 敵陣に入る・出ると成る（Prototype の tryPromote。自動で成る）
        if (system.CanPromote(p, fromX, p.X))
        {
            p.isPromoted = true;
            tl.Schedule(p.UpdateVisual);
        }
    }

    static List<GamePiece> Kings(GameBoard board, PlayerType owner)
    {
        List<GamePiece> list = new List<GamePiece>();
        foreach (GamePiece q in board.GetBoardData())
        {
            if (q != null && q.player == owner && q.type == PieceType.King) list.Add(q);
        }
        return list;
    }

    static GamePiece NearestKing(List<GamePiece> kings, GamePiece p)
    {
        GamePiece best = null;
        int bestD = int.MaxValue;
        foreach (GamePiece k in kings)
        {
            int d = Chebyshev(p.X, p.Y, k.X, k.Y);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        return best;
    }

    static int KingDistance(GameBoard board, GamePiece p, PlayerType enemy)
    {
        GamePiece king = NearestKing(Kings(board, enemy), p);
        return king != null ? Chebyshev(p.X, p.Y, king.X, king.Y) : int.MaxValue;
    }

    static int Chebyshev(int ax, int ay, int bx, int by)
    {
        return Mathf.Max(Mathf.Abs(ax - bx), Mathf.Abs(ay - by));
    }
}
