using System.Collections.Generic;
using UnityEngine;

// アンデッドの自動行動（Prototype の src/core/turn.js の undeadPieces・undeadMoves・chooseUndeadMove・runUndeadPhase）。
// 持ち主の手番の 1 手が終わるたび（手番交代の前。アンデッド・パラダイスを発動した手番も含む）、ファランクスのあとに、
// 持ち主のアンデッドを相手の王将に近い駒から順に、全員 1 手ずつ自動で動かす。
//  手の選び方: 相手の王将を取れる手 → 相手の王将とのチェビシェフ距離が一番縮まる手
//              →（同じ距離なら）相手の駒（火薬を除く）を取る手 →（それでも同じなら）対局の乱数（GameRng）で選ぶ
//  動けるマスは通常の駒の動き（BaseMoveRule。破壊マス・壁には入れず、走り駒はその手前で止まる）から、炎上・落とし穴のマスを除いたもの。動ける手がなければ動かない
//  成り（敵陣に入る・出ると自動で成る）・取り・火薬の爆発は通常の移動と同じ。カウンターに取りを無効にされたら、1 体ごとに反撃を処理する
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

    // 動ける駒が 1 体でもいて、相手の王将がいれば動く。
    // 合戦将棋型では tick ごとに呼ばれるので、List を作らずに有無だけ調べる（結果は RaidGoals・Kings・Pieces・Moves の Count > 0 と同じ）
    public override bool HasWork(GameSystem system, PlayerType owner)
    {
        GameBoard board = system != null ? system.Board : null;
        if (board == null) return false;
        board.Pieces(scratch);
        if (system.IsRaid)
        {
            // 討伐任務：相手の王将の代わりにボス（倒れたあとは残っている相手の駒）へ進む
            if (!HasRaidGoal(system, board, owner)) return false;
        }
        else if (!HasPiece(scratch, BoardZones.Opponent(owner), PieceType.King)) return false;
        foreach (GamePiece p in scratch)
        {
            if (p != null && p.player == owner && p.isUndead && HasAnyMove(board, p)) return true;
        }
        return false;
    }

    // HasWork の作業用（メインスレッドの中だけで使う）
    private static readonly List<GamePiece> scratch = new List<GamePiece>();

    // RaidGoals(system, board, owner).Count > 0 と同じ：ボスが生きていればボスの占めるマス（BossCells の w × h）があるか、
    // 倒れたあとは owner 以外の駒があるか（PiecesInPrototypeOrder と同じく全マスを GetPieceAt で調べる）
    static bool HasRaidGoal(GameSystem system, GameBoard board, PlayerType owner)
    {
        BossState boss = system.Boss;
        if (boss != null && boss.hp > 0) return boss.w > 0 && boss.h > 0;
        for (int x = BoardCells.Size - 1; x >= 0; x--)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                GamePiece q = board.GetPieceAt(x, y);
                if (q != null && q.player != owner) return true;
            }
        }
        return false;
    }

    // Kings(board, owner).Count > 0 と同じ（type の駒が owner に 1 枚でもあるか）
    static bool HasPiece(List<GamePiece> pieces, PlayerType owner, PieceType type)
    {
        foreach (GamePiece q in pieces)
        {
            if (q != null && q.player == owner && q.type == type) return true;
        }
        return false;
    }

    // Moves(board, p).Count > 0 と同じ（動けるマスが 1 つでもあるか。見つかった時点でやめる）
    static bool HasAnyMove(GameBoard board, GamePiece p)
    {
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (PieceRules.IsHazardFor(board.Cells, p, x, y)) continue;
                if (MoveRule.IsLegalMove(p, x, y, board)) return true;
            }
        }
        return false;
    }

    // owner のアンデッド（盤の順）
    public static List<GamePiece> Pieces(GameBoard board, PlayerType owner)
    {
        List<GamePiece> list = new List<GamePiece>();
        foreach (GamePiece p in board.Pieces())
        {
            if (p != null && p.player == owner && p.isUndead) list.Add(p);
        }
        return list;
    }

    // アンデッド p が動けるマス（Prototype の undeadMoves：通常の動きのうち、炎上・落とし穴のマスには入らない。翼状態の駒は消失しないので入る）
    public static List<Vector2Int> Moves(GameBoard board, GamePiece p)
    {
        List<Vector2Int> result = new List<Vector2Int>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (PieceRules.IsHazardFor(board.Cells, p, x, y)) continue;
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
        if (context.GameSystem.IsRaid)
        {
            RunRaid(context);
            return;
        }

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
            context.GameSystem.ResolveCounters(tl); // 取りがカウンターに無効にされたら反撃される（Prototype の move の resolveCounters）
            tl.Wait(StepGap);
        }
        tl.Wait(0.2f);
    }

    // ------------------------------------------------------------
    //  討伐任務（Prototype の undeadGoals・runUndeadPhase。BoardSimulator.Boss.cs の RunUndeadRaid と同じ）
    // ------------------------------------------------------------

    // 盤上の駒（Prototype の allPieces の順：段の大きい方から、各段は筋の小さい方から。巨人は 1 回だけ）
    static List<GamePiece> PiecesInPrototypeOrder(GameBoard board)
    {
        List<GamePiece> res = new List<GamePiece>();
        for (int x = BoardCells.Size - 1; x >= 0; x--)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                GamePiece q = board.GetPieceAt(x, y);
                if (q != null && !res.Contains(q)) res.Add(q);
            }
        }
        return res;
    }

    // owner のアンデッドが目指すマス：ボスの占めるマス（ボスが倒れたあとは、残っている相手の駒のマス）
    static List<Vector2Int> RaidGoals(GameSystem system, GameBoard board, PlayerType owner)
    {
        List<Vector2Int> res = new List<Vector2Int>();
        BossState boss = system.Boss;
        if (boss != null && boss.hp > 0)
        {
            foreach (MatchSquare c in BossEngine<GamePiece>.BossCells(boss)) res.Add(new Vector2Int(c.x, c.y));
            return res;
        }
        foreach (GamePiece q in PiecesInPrototypeOrder(board))
        {
            if (q.player != owner) res.Add(new Vector2Int(q.X, q.Y));
        }
        return res;
    }

    static int GoalDistance(int x, int y, List<Vector2Int> goals)
    {
        int best = int.MaxValue;
        foreach (Vector2Int g in goals) best = Mathf.Min(best, Chebyshev(x, y, g.x, g.y));
        return best;
    }

    // 討伐任務の進軍：ボスに近い駒から順に 1 手ずつ。ボスのマスに入る手（＝ボスへの攻撃。駒は跳ね返る）が最優先。
    // なければいちばん近づく手（同じなら相手の駒を取る手 → 対局の乱数）
    void RunRaid(TurnEndContext context)
    {
        GameBoard board = context.Board;
        GameSystem system = context.GameSystem;
        SpecialContext tl = context.Timeline;
        List<Vector2Int> goals = RaidGoals(system, board, context.Owner);
        if (goals.Count == 0) return;
        List<GamePiece> order = PiecesInPrototypeOrder(board).FindAll(p => p.player == context.Owner && p.isUndead);
        if (order.Count == 0) return;

        List<int> dist = order.ConvertAll(p => GoalDistance(p.X, p.Y, goals));
        List<int> index = new List<int>();
        for (int i = 0; i < order.Count; i++) index.Add(i);
        index.Sort((a, b) => dist[a] != dist[b] ? dist[a] - dist[b] : a - b);
        List<GamePiece> sorted = index.ConvertAll(i => order[i]);
        if (context.Fx != null) context.Fx.UndeadMarchStart(sorted, tl.Time);
        tl.Wait(StartPause);

        foreach (GamePiece p in sorted)
        {
            if (system.IsMatchDecided()) break;
            if (!tl.IsAlive(p)) continue;
            List<Vector2Int> now = RaidGoals(system, board, context.Owner);
            List<Vector2Int> moves = Moves(board, p);
            if (moves.Count == 0 || now.Count == 0) continue;
            Vector2Int pick;
            int kill = moves.FindIndex(m => now.Contains(m));
            if (kill >= 0) pick = moves[kill];
            else
            {
                int best = int.MaxValue;
                foreach (Vector2Int m in moves) best = Mathf.Min(best, GoalDistance(m.x, m.y, now));
                List<Vector2Int> closest = moves.FindAll(m => GoalDistance(m.x, m.y, now) == best);
                List<Vector2Int> takes = closest.FindAll(m =>
                {
                    GamePiece q = board.GetPieceAt(m.x, m.y);
                    return q != null && q.player != p.player && !PieceRules.Detonates(p, q);
                });
                List<Vector2Int> pool = takes.Count > 0 ? takes : closest;
                GameRng rng = system.Match != null ? system.Match.Rng : null;
                pick = rng != null ? rng.Pick(pool) : pool[0];
            }
            Move(context, p, pick.x, pick.y);
            system.ResolveCounters(tl);
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
            return q != null && q.player != p.player && !PieceRules.Detonates(p, q);
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
        string tail = target == null ? "" : PieceRules.Detonates(p, target) ? "（火薬に突入！）"
            : $"（{SideName(target.player)}の{HandUIManager.GetJapaneseName(target.type)}を取る）";
        Debug.Log($"{SideName(p.player)}のアンデッド：{name} ({fromX}, {fromY}) → ({tx}, {ty}){tail}");

        PieceMoveStyle style = PieceMoveAnimator.StyleFor(p);
        float duration = style == PieceMoveStyle.Jump ? 0.35f : 0.28f;
        // 討伐任務：ボス・防壁への攻撃（跳ね返る）も移動として数え、攻撃したマスが敵陣なら成る
        bool boss = tl.BossAt(tx, ty) || (target != null && target.player != p.player && SpecialContext.IsWallPiece(target));
        bool? alive = tl.Step(p, tx, ty, style, duration, false);
        if (alive == null && boss && tl.IsAlive(p))
        {
            p.moveCount++;
            if (system.CanPromote(p, fromX, tx))
            {
                p.isPromoted = true;
                tl.Schedule(p.UpdateVisual);
            }
            return;
        }
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
        foreach (GamePiece q in board.Pieces())
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
