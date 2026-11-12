using System.Collections.Generic;

// CPU の思考（Prototype の src/cpu/search.js）：今の局面で指す行動（MatchAction）を決める。
// 盤面のスナップショット（BoardSnapshot）と BoardSimulator だけを使う（GameObject・UnityEngine の API に依存しないので、別スレッドでも動く）。
//
//  Lv0  合法手からランダム（動作確認用）
//  Lv1  1 手読み：合法手を 1 つずつ複製した局面で試し、評価値（CpuEvaluate）がいちばん高い手。評価値には「次に相手が取れる駒」「相手が王を取れるか」も入る
//  Lv2  2 手読み：Lv1 の上位の手について、相手のいちばん良い応手（相手から見て。評価値を最も下げる手）まで試し、その結果がいちばん良い手
//
// ・対局の結果に関わる乱数は先読みしない：手を試すときは、複製した局面の乱数（rngState）を CPU 自身の乱数で置き換える
// ・CPU の乱数のシードは盤面から作るので、同じ局面・同じレベルなら同じ手になる（時間切れにならない限り。Seed で変えられる）
// ・駒錬成は候補にしない（手番を使わないので、1 手読みでは損得を測りにくい）
// ・対象を 2 つ選ぶ魔法（駒入替：駒 × 駒で数百通り）は、魔法ごとに MultiStepCap 通りまで CPU の乱数で間引いて試す
// ・停止世界（時間停止）の中では、1 回の行動ごとに「その行動のあと時間停止を解除したらどうなるか」で評価する。
//   停止世界そのものを使う手は、時間停止中の行動を Lv1 で続けて試してから評価する（時間が許せば）。
//   時間停止中の効果は、シミュレーションでは予定にせずすぐ起こす近似（BoardSimulator.ApplyForSearch）
//
// 画面を止めないように、思考は Step を呼ぶたびに少しずつ進む（手を 1 つ試すごとに区切る）。
// 別スレッドなら Step を最後まで呼び続け（Run）、スレッドが使えない環境（WebGL）では 1 フレームに数ミリ秒ずつ呼ぶ（CpuPlayer）
public sealed class CpuSearch
{
    public sealed class Options
    {
        public int Level = 1;
        public uint? Seed;               // 省略すると盤面から作る
        public double TimeLimitMs = 1500; // 考える時間の上限（Now で測る）
        public int Top = 10;              // Lv2 で応手まで読む手の数
        public System.Func<double> Now;   // 経過時間（ミリ秒）を返す関数。省略すると Stopwatch
    }

    // 対象を 2 つ選ぶ魔法を、魔法ごとに試す数の上限
    public const int MultiStepCap = 40;

    readonly BoardSnapshot root;
    readonly List<MatchAction> rootLegal;
    readonly Options o;
    readonly PlayerType me;
    readonly GameRng rng;
    readonly System.Func<double> now;
    readonly IEnumerator<bool> routine;
    double deadline;

    public bool Done { get; private set; }
    public MatchAction Result { get; private set; }

    // 試した手の数（ログ・テスト用）
    public int Simulated { get; private set; }

    // state で、手番のプレイヤーが指す行動を考える。rootLegal は今の局面の合法手（GameSystem.LegalActions。省略すると BoardSimulator で作る）
    public CpuSearch(BoardSnapshot state, Options options, List<MatchAction> rootLegal = null)
    {
        root = state;
        o = options ?? new Options();
        this.rootLegal = rootLegal;
        me = state.Current;
        uint seed = o.Seed ?? SeedOf(state);
        rng = new GameRng(unchecked(seed ^ (uint)(o.Level * 0x9e3779b1L)));
        if (o.Now != null) now = o.Now;
        else
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            now = () => sw.Elapsed.TotalMilliseconds;
        }
        routine = Think();
    }

    // 同期で最後まで考える（テスト・別スレッド用）
    public static MatchAction Choose(BoardSnapshot state, Options options, List<MatchAction> rootLegal = null)
    {
        CpuSearch search = new CpuSearch(state, options, rootLegal);
        search.Run();
        return search.Result;
    }

    public void Run()
    {
        while (Step()) { }
    }

    // 少し考える。まだ続きがあれば true
    public bool Step()
    {
        if (Done) return false;
        if (routine.MoveNext()) return true;
        Done = true;
        return false;
    }

    bool OutOfTime => now() > deadline;

    // ------------------------------------------------------------

    IEnumerator<bool> Think()
    {
        List<MatchAction> all = rootLegal ?? BoardSimulator.LegalActions(root, me);
        List<MatchAction> legal = all.FindAll(a => a.type != MatchActionType.Forge);
        if (legal.Count == 0)
        {
            Result = all.Count > 0 ? all[0] : null;
            yield break;
        }
        if (o.Level <= 0)
        {
            Result = rng.Pick(legal);
            yield break;
        }
        List<MatchAction> tried = Candidates(legal);
        deadline = now() + o.TimeLimitMs;

        // 1 手読み
        List<Scored> scored = new List<Scored>();
        foreach (MatchAction a in Order(tried))
        {
            if (scored.Count > 0 && OutOfTime) break;
            BoardSnapshot c = Simulate(root, a);
            yield return true;
            if (c == null) continue;
            double v = CpuEvaluate.Evaluate(c, me);
            if (IsTimeStopCast(root, a) && !OutOfTime)
            {
                Holder<BoardSnapshot> after = new Holder<BoardSnapshot>();
                IEnumerator<bool> e = TimeStopRollout(c, after);
                while (e.MoveNext()) yield return true;
                v = System.Math.Max(v, CpuEvaluate.Evaluate(after.Value, me));
            }
            scored.Add(new Scored { Action = a, After = c, Value = v + rng.Next() * 0.01 }); // 同点なら CPU の乱数で選ぶ
        }
        if (scored.Count == 0)
        {
            Result = rng.Pick(tried);
            yield break;
        }
        scored.Sort((x, y) => y.Value.CompareTo(x.Value));
        if (o.Level == 1 || scored[0].Value >= CpuEvaluate.Win / 2)
        {
            Result = scored[0].Action;
            yield break;
        }

        // 2 手読み：上位の手について、相手のいちばん良い応手のあとの評価値（時間切れで読み切れなかった手は、ほかに読み切った手があれば使わない）
        Scored best = null;
        for (int i = 0; i < scored.Count && i < o.Top; i++)
        {
            if (best != null && OutOfTime) break;
            Holder<Reply> r = new Holder<Reply>();
            IEnumerator<bool> e = ReplyValue(scored[i].After, best != null ? best.Value : double.NegativeInfinity, r);
            while (e.MoveNext()) yield return true;
            if (!r.Value.Complete && best != null) break;
            if (best == null || r.Value.Value > best.Value) best = new Scored { Action = scored[i].Action, Value = r.Value.Value };
        }
        Result = best != null ? best.Action : scored[0].Action;
    }

    sealed class Scored
    {
        public MatchAction Action;
        public BoardSnapshot After;
        public double Value;
    }

    struct Reply
    {
        public double Value;
        public bool Complete;
    }

    sealed class Holder<T>
    {
        public T Value;
    }

    // 候補の行動：錬成を除き、対象を 2 つ選ぶ魔法は魔法ごとに MultiStepCap 通りまで rng で間引く（並びは元のまま）。
    // 上限以下なら rng を使わない
    List<MatchAction> Candidates(List<MatchAction> list)
    {
        List<MatchAction> res = list.FindAll(a => a.type != MatchActionType.Forge);
        Dictionary<string, List<MatchAction>> groups = new Dictionary<string, List<MatchAction>>();
        List<string> order = new List<string>();
        foreach (MatchAction a in res)
        {
            if (a.type != MatchActionType.Magic || a.TargetCount < 2) continue;
            if (!groups.TryGetValue(a.skill, out List<MatchAction> g))
            {
                g = new List<MatchAction>();
                groups[a.skill] = g;
                order.Add(a.skill);
            }
            g.Add(a);
        }
        HashSet<MatchAction> drop = new HashSet<MatchAction>();
        foreach (string id in order)
        {
            List<MatchAction> g = groups[id];
            if (g.Count <= MultiStepCap) continue;
            List<MatchAction> shuffled = rng.Shuffle(g);
            for (int i = MultiStepCap; i < shuffled.Count; i++) drop.Add(shuffled[i]);
        }
        return drop.Count > 0 ? res.FindAll(a => !drop.Contains(a)) : res;
    }

    // 試す順番：取る手・必殺技・狙撃を先に（時間切れになっても、よさそうな手は試し終えているように）→ 移動・打つ → 魔法
    List<MatchAction> Order(List<MatchAction> list)
    {
        List<int> index = new List<int>();
        List<int> rank = new List<int>();
        for (int i = 0; i < list.Count; i++)
        {
            index.Add(i);
            rank.Add(Rank(list[i]));
        }
        index.Sort((x, y) => rank[x] != rank[y] ? rank[x] - rank[y] : x - y);
        return index.ConvertAll(i => list[i]);
    }

    int Rank(MatchAction a)
    {
        switch (a.type)
        {
            case MatchActionType.Move:
            {
                SnapshotPiece t = root.At(a.to.x, a.to.y);
                return t != null && t.owner != (int)me ? 0 : 1; // 取る手は先に（Prototype は移動をすべて 1）
            }
            case MatchActionType.Drop: return 1;
            case MatchActionType.Magic: return 2;
            default: return 0;
        }
    }

    // 複製した局面で行動 a を試した結果の局面（実行できなければ null）。
    // 時間停止中で、まだ自分の行動が続くなら、そこで時間停止を解除した結果にする（取り・爆発などを解決してから評価する）
    BoardSnapshot Simulate(BoardSnapshot s, MatchAction a)
    {
        Simulated++;
        BoardSnapshot c = s.Clone();
        PlayerType actor = s.Current;
        c.rngState = (uint)(rng.Next() * 4294967296.0); // 結果の乱数を先読みしない
        if (!BoardSimulator.ApplyForSearch(c, a)) return null;
        if (c.timeStopped && c.timeStopOwner == (int)actor && c.Current == actor && !c.gameOver && !IsTimeStopCast(s, a))
        {
            BoardSimulator.ApplyForSearch(c, MatchAction.ReleaseTimeStop());
        }
        return c;
    }

    static bool IsTimeStopCast(BoardSnapshot s, MatchAction a)
    {
        return a.type == MatchActionType.Magic && !s.timeStopped && BoardSimulator.MagicKindOf(s, s.Current, a.skill) == MagicKind.TimeStop;
    }

    // 停止世界を使った直後の局面 c0 から、時間停止中の行動を Lv1 で続けて選び、解除したあとの局面を result に入れる
    IEnumerator<bool> TimeStopRollout(BoardSnapshot c0, Holder<BoardSnapshot> result)
    {
        BoardSnapshot c = c0.Clone();
        for (int i = 0; i < 20 && c.timeStopped && c.Current == me && !c.gameOver && !OutOfTime; i++)
        {
            MatchAction bestAction = null;
            double bestValue = double.NegativeInfinity;
            foreach (MatchAction a in Candidates(BoardSimulator.LegalActions(c, c.Current)))
            {
                BoardSnapshot r = Simulate(c, a);
                yield return true;
                if (r == null) continue;
                double v = CpuEvaluate.Evaluate(r, me);
                if (bestAction == null || v > bestValue)
                {
                    bestAction = a;
                    bestValue = v;
                }
            }
            if (bestAction == null || bestAction.type == MatchActionType.ReleaseTimeStop) break;
            BoardSimulator.ApplyForSearch(c, bestAction);
        }
        if (c.timeStopped && c.Current == me) BoardSimulator.ApplyForSearch(c, MatchAction.ReleaseTimeStop());
        result.Value = c;
    }

    // 局面 c（自分が手を指した直後）で、相手のいちばん良い応手のあとの評価値（自分から見て）。
    // cutoff 以下になることが分かったら、そこで打ち切る（ほかの候補より悪いことが確定したので）。時間切れで読み切れなければ Complete = false
    IEnumerator<bool> ReplyValue(BoardSnapshot c, double cutoff, Holder<Reply> result)
    {
        if (c.gameOver || c.Current == me)
        {
            result.Value = new Reply { Value = CpuEvaluate.Evaluate(c, me), Complete = true };
            yield break;
        }
        double worst = double.PositiveInfinity;
        foreach (MatchAction a in Order(Candidates(BoardSimulator.LegalActions(c, c.Current))))
        {
            if (OutOfTime)
            {
                result.Value = new Reply { Value = double.IsPositiveInfinity(worst) ? CpuEvaluate.Evaluate(c, me) : worst, Complete = false };
                yield break;
            }
            BoardSnapshot r = Simulate(c, a);
            yield return true;
            if (r == null) continue;
            worst = System.Math.Min(worst, CpuEvaluate.Evaluate(r, me));
            if (worst <= cutoff || worst <= -CpuEvaluate.Win / 2) break;
        }
        result.Value = new Reply { Value = double.IsPositiveInfinity(worst) ? CpuEvaluate.Evaluate(c, me) : worst, Complete = true };
    }

    // ------------------------------------------------------------

    // 盤面から CPU の乱数のシードを作る（FNV-1a。同じ局面なら同じ値。Unity では GameSystem.StateHash を Seed に渡してもよい）
    public static uint SeedOf(BoardSnapshot s)
    {
        uint h = 2166136261u;
        void Mix(int v)
        {
            unchecked
            {
                h ^= (uint)v;
                h *= 16777619u;
            }
        }
        Mix(s.current);
        Mix(s.turnNumber);
        Mix(s.moveNumber);
        Mix(s.timeStopped ? 1 : 0);
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p == null)
            {
                Mix(-1);
                continue;
            }
            Mix((int)p.type * 4 + p.owner * 2 + (p.promoted ? 1 : 0));
            Mix(p.moveCount);
        }
        foreach (SnapshotPlayer p in s.players)
        {
            Mix(p.energy);
            foreach (int n in p.hand) Mix(n);
        }
        return h;
    }
}
