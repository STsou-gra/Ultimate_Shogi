// 合戦将棋型（手番のないリアルタイム制）のシミュレーション（Match/BoardSimulator.Realtime.cs）と CPU・局面・ハッシュのテスト。
//  ・Prototype の tests/core/kassen.test.js を移したもの（座標は x = 段・y = 筋に直した）：時間とエナジー・手番がない・連打の間隔・
//    「N ターン」の読み替え（打ち込み禁止 60 秒・炎上 20 秒・ファランクス 3 秒・世界の終わり 10 秒・停止世界 10 回か 10 秒）・決定性
//  ・Prototype の棋譜 tests/kifus/kassen-1.json・kassen-2.json（PrototypeKifus/ に写した）を再生し、全行動のあとの盤面のハッシュが
//    Prototype の hash.js の値と一致するか（PrototypeHash：BoardSnapshot から Prototype と同じ JSON の文字列を作る）
//  ・合戦将棋型の CPU（先読みは手番のない局面でも使えるか）・MatchHash の合戦将棋型の項目・MatchSnapshot の時間の項目
//
// 座標：Prototype の (x = 筋, y = 段。先手は下 y = 6〜8) は Unity の (x = 8 − y, y = x)（x = 段。先手は x = 0〜2）
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;

static partial class Program
{
    static void TestKassen()
    {
        TestKassenClock();
        TestKassenNoTurns();
        TestKassenCooldown();
        TestKassenDurations();
        TestKassenTimeStop();
        TestKassenDeterminism();
        TestKassenCpu();
        TestKassenHashAndState();
        TestPrototypeKifus();
    }

    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------

    static readonly int Sec = MatchPolicy.SecToTicks(1f);
    static BattleRuleset K => BattleRulesets.Kassen;

    // 合戦将棋型の初期配置（Prototype の setupGame：エナジー 0・第1ターン・第1手・時間 0）。編成は Unity の Id
    static BoardSnapshot Kassen(uint seed = 1, string[] p1 = null, string[] p2 = null)
    {
        BoardSnapshot s = Initial();
        s.players[0].energy = 0;
        s.recipes.Clear();
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Gunpowder, cost = 3 });
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Sniper, cost = 5 });
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Bird, cost = 2 });
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Giant, cost = 5 });
        string[] lo = { "Shield", "Sword", "Pitfall", "Meteor", "TimeStop" };
        Known(s, p1 ?? lo, p2 ?? lo, seed);
        s.ruleset = BattleRulesets.Kassen.Id;
        s.turnNumber = 1;
        s.moveNumber = 1;
        return s;
    }

    static MatchAction Own(MatchAction a, PlayerType owner)
    {
        a.Owner = owner;
        return a;
    }

    static MatchAction KMv(PlayerType owner, int fx, int fy, int tx, int ty) => Own(Mv(fx, fy, tx, ty), owner);

    static void ClearToKings(BoardSnapshot s)
    {
        foreach (SnapshotPiece p in s.pieces.ToList())
        {
            if (p.type != PieceType.King) s.Remove(p);
        }
    }

    // ------------------------------------------------------------
    //  時間とエナジー（kassen.test.js「合戦将棋型：時間とエナジー」）
    // ------------------------------------------------------------
    static void TestKassenClock()
    {
        BoardSnapshot s = Kassen();
        Check(s.IsRealtime && s.clockTick == 0 && s.clockTime == 0, "合戦: 開始時は時間 0");
        Check(s.players[0].energy == 0 && s.players[1].energy == 0, "合戦: 開始時のエナジーは 0");
        BoardSimulator.Advance(s, Sec - 1);
        Check(s.players[0].energy == 0 && s.players[1].energy == 0, "合戦: 1 秒たつまでエナジーは増えない");
        BoardSimulator.Tick(s);
        Check(s.players[0].energy == 1 && s.players[1].energy == 1, "合戦: 1 秒（20 tick）で両者 +1");
        BoardSimulator.Advance(s, Sec * 9);
        Check(s.players[0].energy == 10 && s.players[1].energy == 10, "合戦: 10 秒で 10");
        Check(s.clockTick == Sec * 10 && s.clockTime == Sec * 10, "合戦: tick と対局の時間");
        Check(s.turnNumber == 1 && s.Current == P1, "合戦: ターン数・手番は変わらない");

        // 本将棋型では Tick しても何も起きない
        BoardSnapshot h = Initial();
        string before = h.ToJson();
        BoardSimulator.Tick(h);
        Check(h.ToJson() == before, "本将棋型では Tick しても何も変わらない");

        // MatchPolicy の数値（Prototype の kassen.js）
        Check(Sec == 20 && MatchPolicy.SecToTicks(K.PieceCooldownSec) == 6, "合戦: 1 秒 = 20 tick・連打の間隔 6 tick");
        Check(MatchPolicy.SecToTicks(K.Durations.DropLockSec) == 1200 && MatchPolicy.SecToTicks(K.Durations.TimeStopSec) == 200, "合戦: 打ち込み禁止 1200 tick・停止世界 200 tick");
        Check(K.CpuIntervalSec.Select(MatchPolicy.SecToTicks).SequenceEqual(new[] { 40, 30, 24 }), "合戦: CPU の間隔 40／30／24 tick");
    }

    // ------------------------------------------------------------
    //  手番がない（「合戦将棋型：手番がない」）
    // ------------------------------------------------------------
    static void TestKassenNoTurns()
    {
        BoardSnapshot s = Kassen();
        // Prototype (2,6)→(2,5) は Unity (2,2)→(3,2)
        Check(BoardSimulator.Apply(s, KMv(P1, 2, 2, 3, 2)), "合戦: 先手が動かす");
        Check(BoardSimulator.Apply(s, KMv(P1, 2, 6, 3, 6)), "合戦: 先手が続けて動かす");
        Check(BoardSimulator.Apply(s, KMv(P2, 6, 2, 5, 2)), "合戦: 後手も動かす");
        Check(s.Current == P1 && s.moveNumber == 4, "合戦: 手番は交代せず、手数だけ進む");
        Check(s.At(3, 2).Owner == P1 && s.At(5, 2).Owner == P2, "合戦: 両者の駒が動いた");

        // 相手の駒は動かせない・owner がなければ current（先手）の行動
        BoardSnapshot t = Kassen();
        Check(!BoardSimulator.CanApply(t, KMv(P2, 2, 2, 3, 2)), "合戦: 相手の駒は動かせない");
        Check(BoardSimulator.ActorOf(t, Mv(2, 2, 3, 2)) == P1, "合戦: owner がなければ先手");

        // 合法手はどちらの席でも返し、どの行動も owner を持ち、CanApply と一致する
        BoardSnapshot u = Kassen(3);
        BoardSimulator.Advance(u, Sec * 30);
        foreach (PlayerType owner in new[] { P1, P2 })
        {
            List<MatchAction> legal = BoardSimulator.LegalActions(u, owner);
            Check(legal.Count > 20, $"合戦: {owner} の合法手がある（{legal.Count}）");
            Check(legal.All(a => a.owner == (int)owner && BoardSimulator.CanApply(u, a)), $"合戦: {owner} の合法手は owner つきで当てはめられる");
            HashSet<string> keys = new HashSet<string>(legal.Select(a => a.Key));
            bool same = true;
            foreach (SnapshotPiece p in u.pieces.ToList())
            {
                for (int x = 0; x < 9; x++)
                {
                    for (int y = 0; y < 9; y++)
                    {
                        MatchAction a = KMv(owner, p.x, p.y, x, y);
                        a.promote = BoardSimulator.IsPromoteChoiceAllowed(u, p, x, y, true) && !BoardSimulator.IsPromoteChoiceAllowed(u, p, x, y, false);
                        if (BoardSimulator.CanApply(u, a) != keys.Contains(a.Key)) same = false;
                    }
                }
            }
            Check(same, $"合戦: {owner} の全マスの移動について、合法手と CanApply が一致");
        }
        Check(!BoardSimulator.LegalActions(u, P1).Any(a => a.type == MatchActionType.Magic && a.skill == "DoubleTurn"), "合戦: 2回行動はない");
        // 打ち歩詰めは禁止しない（詰みがない）
        BoardSnapshot v = Kassen();
        ClearToKings(v);
        v.clockTime = MatchPolicy.SecToTicks(K.Durations.DropLockSec);
        Put(v, PieceType.Lance, P2, 8, 3); Put(v, PieceType.Lance, P2, 8, 5);
        Put(v, PieceType.GoldGeneral, P1, 6, 4);
        v.players[0].hand[BoardSnapshot.HandIndex(PieceType.Pawn)] = 1;
        Check(BoardSimulator.CanDropTo(v, P1, PieceType.Pawn, 7, 4), "合戦: 打ち歩詰めでも打てる");
    }

    // ------------------------------------------------------------
    //  連打の間隔（「同じ駒の連打の間隔」）
    // ------------------------------------------------------------
    static void TestKassenCooldown()
    {
        BoardSnapshot s = Kassen();
        // Prototype (4,8)→(4,7)（王）は Unity (0,4)→(1,4)
        Check(BoardSimulator.Apply(s, KMv(P1, 0, 4, 1, 4)), "連打: 王を動かす");
        MatchAction back = KMv(P1, 1, 4, 0, 4);
        Check(!BoardSimulator.CanApply(s, back), "連打: 動いた駒はすぐ動かせない");
        Check(!BoardSimulator.LegalActions(s, P1).Any(a => a.from.x == 1 && a.from.y == 4), "連打: 合法手にも出ない");
        Check(BoardSimulator.CanApply(s, KMv(P1, 2, 2, 3, 2)), "連打: ほかの駒は動かせる");
        Check(s.At(1, 4).cooldownUntil == 6, "連打: 間隔が終わる tick（0 + 6）");
        BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.PieceCooldownSec) - 1);
        Check(!BoardSimulator.CanApply(s, back), "連打: 5 tick ではまだ");
        BoardSimulator.Tick(s);
        Check(BoardSimulator.CanApply(s, back), "連打: 6 tick で動かせる");
        // 打った駒・錬成には付かない
        BoardSnapshot t = Kassen();
        t.players[0].hand[BoardSnapshot.HandIndex(PieceType.GoldGeneral)] = 1;
        Check(BoardSimulator.Apply(t, Own(MatchAction.Drop(PieceType.GoldGeneral, new Vector2Int(3, 4)), P1)), "連打: 打つ");
        Check(t.At(3, 4).cooldownUntil == 0 && BoardSimulator.CanApply(t, KMv(P1, 3, 4, 4, 4)), "連打: 打った駒はすぐ動かせる");
    }

    // ------------------------------------------------------------
    //  「N ターン」の読み替え（「合戦将棋型：ターン数の読み替え」）
    // ------------------------------------------------------------
    static void TestKassenDurations()
    {
        // 敵陣への打ち込み禁止は開始から 60 秒
        {
            BoardSnapshot s = Kassen();
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.GoldGeneral)] = 1;
            Check(BoardSimulator.IsDropLocked(s) && BoardSimulator.CanDropTo(s, P1, PieceType.GoldGeneral, 4, 4), "打ち込み: 開始時は禁止の期間・中立には打てる");
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.GoldGeneral, 7, 0), "打ち込み: 開始時は敵陣に打てない");
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.Durations.DropLockSec) - 1);
            Check(BoardSimulator.IsDropLocked(s), "打ち込み: 59.95 秒はまだ禁止");
            BoardSimulator.Tick(s);
            Check(!BoardSimulator.IsDropLocked(s) && BoardSimulator.CanDropTo(s, P1, PieceType.GoldGeneral, 7, 0), "打ち込み: 60 秒で解禁");
        }
        // 炎上 10 ターン = 20 秒（期限の tick で鎮火する）
        {
            BoardSnapshot s = Kassen(1, new[] { "Meteor" }, new string[0]);
            ClearToKings(s);
            BoardSimulator.Advance(s, 7);
            s.players[0].energy = 10;
            Check(BoardSimulator.Apply(s, Own(Mg("Meteor", (3, 4)), P1)), "炎上: 隕石");
            SnapshotCell c = s.Cell(3, 4);
            Check(c.IsBurning && c.burnUntil == 7 + 10 * MatchPolicy.SecToTicks(K.Durations.BurnSecPerTurn), $"炎上: 期限は対局の時間 + 400（{c.burnUntil}）");
            BoardSimulator.Advance(s, Sec * 20 - 1);
            Check(c.IsBurning, "炎上: 19.95 秒はまだ燃えている");
            BoardSimulator.Tick(s);
            Check(!c.IsBurning && c.burnUntil == -1, "炎上: 20 秒で鎮火");
        }
        // ファランクスは 3 秒ごとに自動で前進する（両者とも）
        {
            BoardSnapshot s = Kassen();
            ClearToKings(s);
            SnapshotPiece p = Put(s, PieceType.SilverGeneral, P1, 2, 2); p.shield = true; p.phalanx = true;
            SnapshotPiece q = Put(s, PieceType.SilverGeneral, P2, 6, 6); q.shield = true; q.phalanx = true;
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.Durations.AutoActSec) - 1);
            Check(p.x == 2 && q.x == 6, "ファランクス: 3 秒たつまで動かない");
            BoardSimulator.Tick(s);
            Check(p.x == 3 && q.x == 5, "ファランクス: 3 秒で両者 1 マス前進");
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.Durations.AutoActSec));
            Check(p.x == 4 && q.x == 4, "ファランクス: 6 秒でもう 1 マス");
        }
        // 世界の終わりは 10 秒ごとに 3 マス崩れる（発動の瞬間にも 3 マス）
        {
            BoardSnapshot s = Kassen(5, new[] { "Shield", "WorldEnd" }, new string[0]);
            s.players[0].energy = 30;
            s.players[0].transcendentUnlocked = true;
            int Broken() => s.cells.Count(c => c.IsBroken);
            BoardSimulator.Advance(s, 3);
            Check(BoardSimulator.Apply(s, Own(Mg("WorldEnd"), P1)), "世界の終わり: 発動");
            Check(Broken() == 3 && s.players[0].worldEndSince == 3, "世界の終わり: 発動の瞬間に 3 マス・発動時刻は対局の時間");
            int n = MatchPolicy.SecToTicks(K.Durations.WorldEndIntervalSec);
            BoardSimulator.Advance(s, n - 3 - 1);
            Check(Broken() == 3, "世界の終わり: 10 秒の区切りまでは崩れない");
            BoardSimulator.Tick(s);
            Check(Broken() == 6, "世界の終わり: 10 秒で 3 マス");
            BoardSimulator.Advance(s, n);
            Check(Broken() == 9, "世界の終わり: 20 秒でさらに 3 マス");
        }
    }

    // ------------------------------------------------------------
    //  停止世界：10 回行動か 10 秒の早いほう
    // ------------------------------------------------------------
    static void TestKassenTimeStop()
    {
        {
            BoardSnapshot s = Kassen();
            s.players[0].energy = 31;
            s.players[0].transcendentUnlocked = true;
            Check(BoardSimulator.Apply(s, Own(Mg("TimeStop"), P1)), "停止世界: 発動");
            Check(s.timeStopped && s.timeStopOwner == 0 && s.timeStopElapsed == 0, "停止世界: 時間停止中");
            Check(!BoardSimulator.CanApply(s, KMv(P2, 6, 2, 5, 2)) && BoardSimulator.LegalActions(s, P2).Count == 0, "停止世界: 相手は動けない");
            int time = s.clockTime;
            int[] energy = { s.players[0].energy, s.players[1].energy };
            Check(BoardSimulator.ApplyForSearch(s, KMv(P1, 2, 2, 3, 2)), "停止世界: 発動した側は動ける（先読みの近似）");
            Check(s.timeStopRemaining == 9, "停止世界: 残り 9 回");
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.Durations.TimeStopSec) - 1);
            Check(s.timeStopped && s.clockTime == time && s.players[0].energy == energy[0] && s.players[1].energy == energy[1], "停止世界: 対局の時間・エナジーは止まる");
            BoardSimulator.Tick(s);
            Check(!s.timeStopped && s.timeStopElapsed == 0, "停止世界: 10 秒で解除");
            Check(BoardSimulator.CanApply(s, KMv(P2, 6, 2, 5, 2)), "停止世界: 解除のあと相手は動ける");
            BoardSimulator.Tick(s);
            Check(s.clockTime == time + 1, "停止世界: 解除のあと時間が進む");
        }
        {
            BoardSnapshot s = Kassen();
            s.players[0].energy = 31;
            s.players[0].transcendentUnlocked = true;
            BoardSimulator.Apply(s, Own(Mg("TimeStop"), P1));
            for (int i = 0; i < 10; i++)
            {
                Check(s.timeStopped, $"停止世界: {i + 1} 回目の前は時間停止中");
                MatchAction a = BoardSimulator.LegalActions(s, P1).First(x => x.type == MatchActionType.Move);
                BoardSimulator.ApplyForSearch(s, a);
                BoardSimulator.Advance(s, MatchPolicy.SecToTicks(0.4f));
            }
            Check(!s.timeStopped, "停止世界: 10 回行動したら解除（時間より先）");
        }
    }

    // ------------------------------------------------------------
    //  決定性（同じシードと同じ tick つきの行動の列なら同じ盤面。JSON の往復も挟む）
    // ------------------------------------------------------------
    static List<MatchAction> RandomKassen(uint seed, int ticks, out BoardSnapshot end)
    {
        BoardSnapshot s = Kassen(seed, new[] { "Shield", "Sword", "Pitfall", "Meteor", "WorldEnd" }, new[] { "Counter", "Bomb", "Shield", "Phalanx", "TimeStop" });
        GameRng r = new GameRng(seed * 7 + 1);
        List<MatchAction> log = new List<MatchAction>();
        for (int i = 0; i < ticks && !s.gameOver; i++)
        {
            if (r.Next() < 0.25)
            {
                PlayerType owner = r.Next() < 0.5 ? P1 : P2;
                List<MatchAction> legal = BoardSimulator.LegalActions(s, owner);
                if (legal.Count > 0 && !s.timeStopped)
                {
                    MatchAction a = r.Pick(legal).Clone();
                    a.t = s.clockTick;
                    if (BoardSimulator.Apply(s, a))
                    {
                        a.hash = MatchHash.Fnv1a(PrototypeHashText(s));
                        log.Add(a);
                    }
                }
            }
            BoardSimulator.Tick(s);
        }
        end = s;
        return log;
    }

    static void TestKassenDeterminism()
    {
        foreach (uint seed in new uint[] { 1, 2, 3 })
        {
            List<MatchAction> log = RandomKassen(seed, 1600, out BoardSnapshot a);
            Check(log.Count > 50, $"合戦の決定性: シード {seed} で行動が多い（{log.Count}）");
            BoardSnapshot s = Kassen(seed, new[] { "Shield", "Sword", "Pitfall", "Meteor", "WorldEnd" }, new[] { "Counter", "Bomb", "Shield", "Phalanx", "TimeStop" });
            int k = 0;
            bool ok = true;
            while (k < log.Count && ok)
            {
                while (k < log.Count && log[k].t == s.clockTick)
                {
                    MatchAction e = log[k].Clone();
                    e.hash = "";
                    e.t = -1;
                    if (!BoardSimulator.Apply(s, e) || MatchHash.Fnv1a(PrototypeHashText(s)) != log[k].hash)
                    {
                        ok = false;
                        Check(false, $"合戦の決定性: シード {seed} の {k + 1} 手目");
                        break;
                    }
                    k++;
                }
                if (s.clockTick % 97 == 0)
                {
                    BoardSnapshot back = BoardSnapshot.FromJson(s.ToJson());
                    Check(back != null && back.Diff(s, true).Count == 0, $"合戦の決定性: JSON の往復（tick {s.clockTick}）");
                    s = back.Clone();
                }
                BoardSimulator.Tick(s);
            }
            Check(ok && k == log.Count, $"合戦の決定性: シード {seed} の {log.Count} 手が同じハッシュ");
        }

        // ランダムな対局で、状態の形が崩れない
        RandomKassen(9, 3000, out BoardSnapshot end);
        Invariants(end, "合戦のランダムな対局");
        Check(end.KingCount(P1) > 0 || end.KingCount(P2) > 0, "合戦のランダムな対局: どちらかの王将が残る");
    }

    // ------------------------------------------------------------
    //  CPU（Prototype の search.js：手番のない局面では current を CPU の席にして渡す）
    // ------------------------------------------------------------
    static void TestKassenCpu()
    {
        BoardSnapshot s = Kassen();
        BoardSimulator.Advance(s, Sec * 5);
        foreach (PlayerType owner in new[] { P1, P2 })
        {
            foreach (int level in new[] { 0, 1, 2 })
            {
                BoardSnapshot view = s.Clone();
                view.current = (int)owner;
                string before = view.ToJson();
                MatchAction a = CpuSearch.Choose(view, Opt(level));
                Check(a != null && a.owner == (int)owner && BoardSimulator.CanApply(s, a), $"合戦の CPU: {owner} Lv{level} は自分の席の合法手を返す（{a}）");
                Check(view.ToJson() == before, $"合戦の CPU: {owner} Lv{level} は局面を書き換えない");
                Check(CpuSearch.Choose(view, Opt(level))?.Key == a?.Key, $"合戦の CPU: {owner} Lv{level} は同じ局面なら同じ手");
            }
        }

        // 王を取れるなら取る（後手の席でも）
        {
            BoardSnapshot t = Kassen();
            ClearToKings(t);
            Put(t, PieceType.Rook, P2, 4, 4);
            t.Remove(t.At(0, 4)); Put(t, PieceType.King, P1, 0, 4);
            t.current = (int)P2;
            MatchAction a = CpuSearch.Choose(t, Opt(1));
            Check(a != null && a.type == MatchActionType.Move && a.to.x == 0 && a.to.y == 4 && a.owner == 1, $"合戦の CPU: 王を取れるなら取る（{a}）");
        }

        // CPU 同士（Lv1 対 Lv2）で、間隔ごとに考えて指し続けても壊れない（RealtimeCpuPacer と同じ間隔）
        {
            BoardSnapshot g = Kassen(4);
            int[] next = { MatchPolicy.SecToTicks(K.CpuIntervalSec[1]), MatchPolicy.SecToTicks(K.CpuIntervalSec[2]) };
            int acts = 0;
            for (int i = 0; i < Sec * 90 && !g.gameOver; i++)
            {
                for (int o = 0; o < 2; o++)
                {
                    if (g.clockTick < next[o]) continue;
                    next[o] = g.clockTick + MatchPolicy.SecToTicks(K.CpuIntervalSec[o + 1]);
                    BoardSnapshot view = g.Clone();
                    view.current = o;
                    MatchAction a = CpuSearch.Choose(view, Opt(o + 1, 60));
                    if (a == null) continue;
                    if (g.timeStopped ? BoardSimulator.ApplyForSearch(g, a) : BoardSimulator.Apply(g, a)) acts++;
                }
                BoardSimulator.Tick(g);
            }
            g.current = 0;
            Invariants(g, "合戦の CPU 同士");
            Check(acts >= 10 && (g.gameOver || acts > 60), $"合戦の CPU 同士: 終局するか 90 秒まで指し続けた（{acts} 回・{(g.gameOver ? "終局" : "続行中")}）");
            Console.WriteLine($"kassen cpu vs cpu（Lv1 対 Lv2）: {acts} 回行動・{(g.gameOver ? $"tick {g.clockTick} で{(g.winner == 0 ? "先手" : g.winner == 1 ? "後手" : "引き分け")}の勝ち" : "90 秒で打ち切り")}");
        }
    }

    // ------------------------------------------------------------
    //  MatchHash の合戦将棋型の項目・MatchSnapshot の時間（JSON の往復）
    // ------------------------------------------------------------
    static void TestKassenHashAndState()
    {
        MatchSnapshot m = new MatchSnapshot();
        m.players.Add(new PlayerSnapshot { owner = 0 });
        m.players.Add(new PlayerSnapshot { owner = 1 });
        string honshogi = MatchHash.BuildText(m);
        Check(!honshogi.Contains("kassen") && !honshogi.Contains("versus"), "MatchHash: 本将棋型には戦闘型・時間を入れない（今までのハッシュのまま）");
        m.ruleset = "kassen";
        m.clock = new ClockSnapshot { active = true, tick = 30, time = 25 };
        m.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.Pawn, owner = 0, x = 3, y = 2, cooldownUntil = 12 });
        m.pieces.Add(new PieceSnapshot { id = 2, type = PieceType.Pawn, owner = 1, x = 2, y = 5, cooldownUntil = 20 });
        string kassen = MatchHash.BuildText(m);
        Check(kassen.EndsWith(",[\"versus\",\"kassen\",null],[30,25],[[2,5,20],[3,2,12]]]"), $"MatchHash: 合戦将棋型は設定・時間・連打の間隔（段 → 筋の順）を足す（{kassen.Substring(Math.Max(0, kassen.Length - 70))}）");
        m.clock.tick = 31;
        Check(MatchHash.BuildText(m) != kassen, "MatchHash: tick が違えば違うハッシュ");

        MatchSnapshot back = MatchSnapshot.FromJson(m.ToJson());
        Check(back != null && back.ruleset == "kassen" && back.clock.active && back.clock.tick == 31 && back.clock.time == 25, "MatchSnapshot: 戦闘型と時間が JSON で往復する");
        Check(back.pieces[0].cooldownUntil == 12 && MatchHash.BuildText(back) == MatchHash.BuildText(m), "MatchSnapshot: 連打の間隔が往復し、ハッシュも同じ");
        MatchSnapshot old = MatchSnapshot.FromJson("{\"format\":\"ultimate-shogi-state\",\"version\":1}");
        Check(old != null && old.ruleset == "honshogi" && old.clock != null && !old.clock.active, "MatchSnapshot: 戦闘型のない JSON は本将棋型");
        TimeStopSnapshot ts = new TimeStopSnapshot { active = true, elapsed = 37 };
        m.timeStop = ts;
        Check(MatchSnapshot.FromJson(m.ToJson()).timeStop.elapsed == 37, "MatchSnapshot: 停止世界の経過が往復する");

        BoardSnapshot b = Kassen();
        BoardSimulator.Advance(b, 33);
        BoardSimulator.Apply(b, KMv(P1, 2, 2, 3, 2));
        BoardSnapshot bb = BoardSnapshot.FromJson(b.ToJson());
        Check(bb != null && bb.IsRealtime && bb.clockTick == 33 && bb.At(3, 2).cooldownUntil == 39 && bb.Diff(b, true).Count == 0, "BoardSnapshot: 合戦将棋型の時間・連打の間隔が JSON で往復する");
    }

    // ------------------------------------------------------------
    //  Prototype の棋譜（tests/kifus/kassen-*.json）の再生
    // ------------------------------------------------------------

    // Prototype の魔法の id → Unity の SkillDefinition.Id（SnapshotSkill）
    static readonly Dictionary<string, SnapshotSkill> ProtoSkills = new Dictionary<string, SnapshotSkill>
    {
        ["shield"] = Sk("Shield", MagicKind.Shield, 3, true),
        ["sword"] = Sk("Sword", MagicKind.Sword, 4, true),
        ["pitfall"] = Sk("Pitfall", MagicKind.Pitfall, 2, true),
        ["counter"] = Sk("Counter", MagicKind.Counter, 3, true),
        ["bomb"] = Sk("Bomb", MagicKind.Bomb, 4, true),
        ["test_spark"] = Sk("TestSpark", MagicKind.Spark, 1, true),
        ["phalanx"] = Sk("Phalanx", MagicKind.Phalanx, 7, true),
        ["meteor"] = Sk("Meteor", MagicKind.Meteor, 10, true),
        ["cannon"] = Sk("Cannon", MagicKind.Cannon, 18, true),
        ["cataclysm"] = Sk("Cataclysm", MagicKind.Cataclysm, 30, true, true),
        ["world_end"] = Sk("WorldEnd", MagicKind.WorldEnd, 30, true, true),
        ["undead_paradise"] = Sk("UndeadParadise", MagicKind.UndeadParadise, 30, true, true),
        ["time_stop"] = Sk("TimeStop", MagicKind.TimeStop, 30, true, true),
    };

    static readonly Dictionary<string, PieceType> ProtoPieces = new Dictionary<string, PieceType>
    {
        ["OU"] = PieceType.King, ["HI"] = PieceType.Rook, ["KA"] = PieceType.Bishop, ["KI"] = PieceType.GoldGeneral,
        ["GI"] = PieceType.SilverGeneral, ["KE"] = PieceType.Knight, ["KY"] = PieceType.Lance, ["FU"] = PieceType.Pawn,
        ["KAYAKU"] = PieceType.Gunpowder, ["SNIPER"] = PieceType.Sniper, ["TORI"] = PieceType.Bird, ["KYOJIN"] = PieceType.Giant,
        ["KOONI"] = PieceType.Kooni,
    };

    static string ProtoName(PieceType t) => ProtoPieces.First(kv => kv.Value == t).Key;

    // Prototype のマス {x, y} → Unity のマス（x = 8 − y・y = x）
    static MatchSquare ProtoSq(JsonElement e) => new MatchSquare(8 - e.GetProperty("y").GetInt32(), e.GetProperty("x").GetInt32());

    static string FindKifu(string name)
    {
        string dir = AppContext.BaseDirectory;
        for (int i = 0; i < 8 && dir != null; i++)
        {
            string p = Path.Combine(dir, "PrototypeKifus", name);
            if (File.Exists(p)) return p;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        string cwd = Path.Combine(Directory.GetCurrentDirectory(), "PrototypeKifus", name);
        return File.Exists(cwd) ? cwd : null;
    }

    // Prototype の行動 → Unity の MatchAction（移動の成りは Prototype と同じく「成れるなら成る」）
    static MatchAction FromProto(BoardSnapshot s, JsonElement e)
    {
        string type = e.GetProperty("type").GetString();
        MatchAction a;
        switch (type)
        {
            case "move":
            {
                MatchSquare from = ProtoSq(e.GetProperty("from")), to = ProtoSq(e.GetProperty("to"));
                SnapshotPiece p = s.At(from.x, from.y);
                bool promote = p != null && BoardSimulator.IsPromoteChoiceAllowed(s, p, to.x, to.y, true);
                a = new MatchAction { type = MatchActionType.Move, from = from, to = to, promote = promote };
                break;
            }
            case "drop":
                a = new MatchAction { type = MatchActionType.Drop, piece = ProtoPieces[e.GetProperty("piece").GetString()], to = ProtoSq(e.GetProperty("to")) };
                break;
            case "special":
            case "snipe":
                a = new MatchAction { type = type == "special" ? MatchActionType.Special : MatchActionType.Snipe, from = ProtoSq(e.GetProperty("from")) };
                if (e.TryGetProperty("target", out JsonElement st)) a.targets.Add(ProtoSq(st));
                break;
            case "magic":
                a = new MatchAction { type = MatchActionType.Magic, skill = ProtoSkills[e.GetProperty("skill").GetString()].id };
                if (e.TryGetProperty("target", out JsonElement mt)) a.targets.Add(ProtoSq(mt));
                if (e.TryGetProperty("to", out JsonElement mto) && ProtoSkills[e.GetProperty("skill").GetString()].UsesDestination) a.to = ProtoSq(mto);
                break; // orient：Unity の範囲の魔法は向きが 1 つ（Prototype の orient は範囲が回る魔法だけ）
            case "forge":
                a = new MatchAction { type = MatchActionType.Forge, piece = ProtoPieces[e.GetProperty("piece").GetString()] };
                break;
            case "releaseTimeStop":
                a = MatchAction.ReleaseTimeStop();
                break;
            default:
                throw new Exception($"知らない行動 {type}");
        }
        if (e.TryGetProperty("owner", out JsonElement o)) a.owner = o.GetInt32();
        return a;
    }

    // BoardSnapshot から、Prototype の src/core/hash.js の hashState と同じ JSON の文字列を作る（合戦将棋型・基本対戦）。
    // 盤は Prototype の段（y = 0：後手の奥）→ 筋（x）の順。Unity の (8 − y, x)。巨人は Prototype の駒の id が要るので扱わない
    static string PrototypeHashText(BoardSnapshot s)
    {
        StringBuilder sb = new StringBuilder(4096);
        string B(bool v) => v ? "true" : "false";
        string N(int v) => v >= 0 ? v.ToString() : "null";
        int F(bool v) => v ? 1 : 0;
        string[] legacy = { "HI", "KA", "KI", "GI", "KE", "KY", "FU", "KAYAKU", "SNIPER" };
        string[] later = { "TORI", "KYOJIN" };

        sb.Append('[').Append(s.current).Append(',').Append(s.turnNumber).Append(',').Append(s.moveNumber).Append(',').Append(B(s.gameOver)).Append(',');
        sb.Append('[').Append(s.players[0].energy).Append(',').Append(s.players[1].energy).Append("],[");
        for (int i = 0; i < 2; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{');
            bool first = true;
            foreach (string t in legacy)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append('"').Append(t).Append("\":").Append(s.HandCount((PlayerType)i, ProtoPieces[t]));
            }
            foreach (string t in later)
            {
                int c = s.HandCount((PlayerType)i, ProtoPieces[t]);
                if (c > 0) sb.Append(",\"").Append(t).Append("\":").Append(c);
            }
            sb.Append('}');
        }
        sb.Append("],[");
        for (int py = 0; py < 9; py++)
        {
            if (py > 0) sb.Append(',');
            sb.Append('[');
            for (int px = 0; px < 9; px++)
            {
                if (px > 0) sb.Append(',');
                SnapshotPiece p = s.At(8 - py, px);
                if (p == null) { sb.Append("null"); continue; }
                sb.Append("[\"").Append(ProtoName(p.type)).Append("\",").Append(p.owner).Append(',').Append(F(p.promoted)).Append(',').Append(p.moveCount)
                  .Append(',').Append(F(p.shield)).Append(',').Append(F(p.phalanx)).Append(',').Append(F(p.undead));
                if (p.wing || p.counter) sb.Append(',').Append(F(p.wing));
                if (p.counter) sb.Append(",1");
                sb.Append(']');
            }
            sb.Append(']');
        }
        sb.Append("],[");
        for (int py = 0; py < 9; py++)
        {
            if (py > 0) sb.Append(',');
            sb.Append('[');
            for (int px = 0; px < 9; px++)
            {
                if (px > 0) sb.Append(',');
                SnapshotCell c = s.Cell(8 - py, px);
                if (c.IsWall) { sb.Append("[\"wall\",").Append(c.wall).Append(']'); continue; }
                string state = c.IsBroken ? "broken" : c.IsBurning ? "burning" : "normal";
                sb.Append("[\"").Append(state).Append("\",").Append(N(c.IsBurning ? c.burnUntil : -1)).Append(',').Append(N(c.trap)).Append(']');
            }
            sb.Append(']');
        }
        sb.Append("],");
        sb.Append('[').Append(B(s.players[0].transcendentUsed)).Append(',').Append(B(s.players[1].transcendentUsed)).Append("],");
        sb.Append('[').Append(B(s.players[0].transcendentUnlocked)).Append(',').Append(B(s.players[1].transcendentUnlocked)).Append("],");
        sb.Append('[').Append(N(s.players[0].worldEndSince)).Append(',').Append(N(s.players[1].worldEndSince)).Append("],[");
        for (int i = 0; i < 2; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[').Append(string.Join(",", s.players[i].fallenInitial.Select(t => $"\"{ProtoName(t)}\""))).Append(']');
        }
        sb.Append("],");
        if (s.timeStopped) sb.Append('[').Append(s.timeStopOwner).Append(',').Append(s.timeStopRemaining).Append(",[]]");
        else sb.Append("null");
        if (s.IsRealtime)
        {
            // 基本対戦 × 本将棋型 以外で足す項目（hash.js の extraParts）：設定・時間・連打の間隔・ボス・討伐任務・手下の動き
            string Q(string v) => string.IsNullOrEmpty(v) ? "null" : $"\"{v}\"";
            sb.Append(",[\"").Append(s.mode).Append("\",\"").Append(s.ruleset).Append("\",").Append(s.HasBoss ? Q(s.boss.id) : "null")
              .Append("],[").Append(s.clockTick).Append(',').Append(s.clockTime).Append("],[");
            List<int[]> cds = s.pieces.Where(p => p.cooldownUntil > 0).Select(p => new[] { p.y, 8 - p.x, p.cooldownUntil })
                .OrderBy(c => c[1]).ThenBy(c => c[0]).ToList();
            sb.Append(string.Join(",", cds.Select(c => $"[{c[0]},{c[1]},{c[2]}]")));
            sb.Append("],");
            if (s.HasBoss)
            {
                // ボスの位置は Prototype の左上のマス（x = 筋のいちばん小さいマス・y = 9 − 段 − 奥行き）。向きは dx = 筋の向き・dy = −段の向き
                BossState b = s.boss;
                sb.Append('[').Append(Q(b.id)).Append(',').Append(b.y).Append(',').Append(9 - b.x - b.h).Append(',').Append(b.hp).Append(',')
                  .Append(b.facingY).Append(',').Append(-b.facingX).Append(',').Append(Q(b.ai.phase)).Append(',').Append(b.ai.until).Append(',')
                  .Append(Q(b.ai.attack)).Append(',').Append(Q(b.ai.last)).Append(']');
            }
            else sb.Append("null");
            sb.Append(',').Append(s.IsRaid ? $"[{s.raid.kingHits}]" : "null");
            List<SnapshotPiece> mins = s.pieces.Where(p => p.minion).OrderBy(p => 8 - p.x).ThenBy(p => p.y).ToList();
            if (mins.Count > 0)
            {
                sb.Append(",[").Append(string.Join(",", mins.Select(p =>
                    $"[{p.y},{8 - p.x},{p.minionNext},{(p.minionHasTo ? $"[{p.minionToY},{8 - p.minionToX}]" : "null")}]"))).Append(']');
            }
        }
        sb.Append(']');
        return sb.ToString();
    }

    static void TestPrototypeKifus()
    {
        foreach (string name in new[] { "kassen-1.json", "kassen-2.json" })
        {
            string path = FindKifu(name);
            Check(path != null, $"Prototype の棋譜 {name} がある（Tests/SnapshotTests/PrototypeKifus）");
            if (path == null) continue;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement k = doc.RootElement;
            Check(k.GetProperty("format").GetString() == "ultimate-shogi-kifu" && k.GetProperty("version").GetInt32() == 2, $"{name}: Prototype の棋譜の版 2");
            Check(k.GetProperty("config").GetProperty("ruleset").GetString() == "kassen", $"{name}: 合戦将棋型");

            // 編成（Prototype の normal・ultimate・transcendent の順。Unity の枠の順と同じ）
            List<SnapshotSkill>[] decks = { new List<SnapshotSkill>(), new List<SnapshotSkill>() };
            JsonElement los = k.GetProperty("loadouts");
            for (int i = 0; i < 2; i++)
            {
                JsonElement lo = los[i];
                foreach (JsonElement id in lo.GetProperty("normal").EnumerateArray()) decks[i].Add(ProtoSkills[id.GetString()]);
                foreach (string rank in new[] { "ultimate", "transcendent" })
                {
                    if (lo.TryGetProperty(rank, out JsonElement v) && v.ValueKind == JsonValueKind.String) decks[i].Add(ProtoSkills[v.GetString()]);
                }
            }
            uint seed = k.GetProperty("seed").GetUInt32();
            BoardSnapshot s = Kassen(seed);
            s.players[0].skills = decks[0];
            s.players[1].skills = decks[1];

            List<JsonElement> actions = k.GetProperty("actions").EnumerateArray().ToList();
            int n = actions.Count, played = 0, matched = 0;
            string error = null;
            while (played < n && !s.gameOver && error == null)
            {
                while (played < n && actions[played].GetProperty("t").GetInt32() == s.clockTick)
                {
                    JsonElement e = actions[played];
                    MatchAction a = FromProto(s, e);
                    if (!BoardSimulator.Apply(s, a, out string reason))
                    {
                        error = $"{played + 1} 手目（t={s.clockTick}）{a} を当てはめられない：{reason}";
                        break;
                    }
                    string text = PrototypeHashText(s);
                    string h = MatchHash.Fnv1a(text);
                    string want = e.GetProperty("hash").GetString();
                    played++;
                    if (h != want)
                    {
                        error = $"{played} 手目（t={s.clockTick}）{a} のあとのハッシュが違う：Prototype {want} ／ Unity {h}";
                        string dump = Environment.GetEnvironmentVariable("PROTO_HASH_DUMP");
                        if (!string.IsNullOrEmpty(dump)) File.WriteAllText(dump, text);
                        break;
                    }
                    matched++;
                }
                if (error != null) break;
                if (played < n && actions[played].GetProperty("t").GetInt32() < s.clockTick)
                {
                    error = $"{played + 1} 手目の時刻（t={actions[played].GetProperty("t").GetInt32()}）を過ぎた";
                    break;
                }
                // 途中で JSON を往復させても同じ（BoardSnapshot の合戦将棋型の項目）
                if (s.clockTick % 101 == 0) s = BoardSnapshot.FromJson(s.ToJson());
                BoardSimulator.Tick(s);
            }
            Check(error == null, $"{name}: {error}");
            Check(matched == n, $"{name}: 全 {n} 手のハッシュが Prototype と一致（{matched} 手）");
            Console.WriteLine($"prototype kifu {name}: {matched} / {n} 手のハッシュが一致（seed {seed}・最後の tick {s.clockTick}）");
        }
    }
}
