// 通常魔法「カウンター」のシミュレーション（BoardSimulator.Capture・ResolveCounters・CpuEvaluate）と、局面のハッシュ（MatchHash）のテスト。
// Prototype の tests/core/counter.test.js の場面を、Unity の座標（x = 段・y = 筋。先手は +x が前）に移したもの。
// 期待値は Prototype の README・src/defs/magic/counter.js・src/core/effects.js の tryCapture / counterGuard・play.js の resolveCounters に合わせた
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class SnapshotSuite
{
    // 先手のカウンター状態の金（(4, 4)）と、後手の手番の盤（Prototype の setup。両者とも カウンター・盾・剣 を編成）
    static (BoardSnapshot s, SnapshotPiece ki) CounterSetup()
    {
        BoardSnapshot s = Known(Basic(P2), new[] { "Counter", "Shield", "Sword" }, new[] { "Counter", "Shield", "Sword" });
        s.players[0].energy = 20;
        s.players[1].energy = 20;
        SnapshotPiece ki = Put(s, PieceType.GoldGeneral, P1, 4, 4);
        ki.counter = true;
        return (s, ki);
    }

    static void TestCounter()
    {
        // 通常魔法・コスト 6・手番を使う。エナジーが足りないと使えない
        {
            BoardSnapshot s = Known(Basic(), new[] { "Counter" });
            Put(s, PieceType.GoldGeneral, P1, 4, 4);
            s.players[0].energy = 5;
            Check(!BoardSimulator.LegalActions(s, P1).Any(a => a.type == MatchActionType.Magic), "カウンター: エナジー 5 では使えない");
            s.players[0].energy = 6;
            Check(BoardSimulator.LegalActions(s, P1).Any(a => a.type == MatchActionType.Magic), "カウンター: エナジー 6 で使える");
        }
        // カウンター状態ではない自分の駒（王将も可）を選び、カウンター状態にする。手番を使う
        {
            BoardSnapshot s = Known(Basic(), new[] { "Counter" });
            s.players[0].energy = 20;
            SnapshotPiece ki = Put(s, PieceType.GoldGeneral, P1, 4, 4);
            Put(s, PieceType.GoldGeneral, P2, 6, 4);
            Check(!BoardSimulator.CanApply(s, Mg("Counter", (6, 4))), "カウンター: 相手の駒は選べない");
            Check(BoardSimulator.CanApply(s, Mg("Counter", (0, 0))), "カウンター: 王将も選べる");
            Check(BoardSimulator.Apply(s, Mg("Counter", (4, 4))) && ki.counter, "カウンター: カウンター状態になる");
            Check(s.players[0].energy == 14 && s.Current == P2, "カウンター: エナジー -6・手番を使う");
            s.current = (int)P1;
            Check(!BoardSimulator.CanApply(s, Mg("Counter", (4, 4))), "カウンター: すでにカウンター状態の駒は選べない");
            SnapshotPiece undead = Put(s, PieceType.Pawn, P1, 3, 2); undead.undead = true;
            Check(!BoardSimulator.CanApply(s, Mg("Counter", (3, 2))), "カウンター: アンデッドは選べない");
        }
        // 前方 1×3 から取りに来た駒：取りを無効化して元のマスへ戻し、そのマスへ動いて取る
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 5, 4);   // 先手の金の正面
            Check(BoardSimulator.Apply(s, Mv(5, 4, 4, 4)), "カウンター（正面）: apply");
            Check(!s.Contains(gi) && s.At(5, 4) == ki && s.At(4, 4) == null, "カウンター（正面）: 攻撃した銀を取り返して、そのマスへ動く");
            Check(!ki.counter && ki.moveCount == 0, "カウンター（正面）: カウンターは 1 回だけ・反撃は移動回数に数えない");
            Check(Hand(s, P1, PieceType.SilverGeneral) == 1, "カウンター（正面）: 取った銀は持ち駒");
            Check(s.players[0].energy == 20 + 1 + 1 && s.players[1].energy == 20 + 3, "カウンター（正面）: 反撃も取りなのでエナジー配分あり（手番の +1 も）");
            Check(s.Current == P1 && s.counters == null, "カウンター（正面）: 攻撃した側の手番は使われる・予定は行動の中で処理して消す");
        }
        // 斜め前（左前・右前）から来た駒にも反撃する
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 5, 5);
            BoardSimulator.Apply(s, Mv(5, 5, 4, 4));
            Check(!s.Contains(gi) && s.At(5, 5) == ki, "カウンター（斜め前）: 反撃する");
        }
        // 前方 1×3 の外から来た駒：取りは無効化して元のマスへ戻すが、反撃はしない
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece side = Put(s, PieceType.GoldGeneral, P2, 4, 3);   // 横から
            BoardSimulator.Apply(s, Mv(4, 3, 4, 4));
            Check(s.At(4, 3) == side && s.At(4, 4) == ki && !ki.counter, "カウンター（横）: 元のマスへ戻り、反撃はしない");
            Check(side.moveCount == 0, "カウンター（横）: 阻まれた移動は数えない（盾と同じ）");

            var t = CounterSetup();
            SnapshotPiece hi = Put(t.s, PieceType.Rook, P2, 7, 4);       // 遠くから
            BoardSimulator.Apply(t.s, Mv(7, 4, 4, 4));
            Check(t.s.At(7, 4) == hi && t.s.At(4, 4) == t.ki && !t.ki.counter, "カウンター（遠く）: 元のマスへ戻り、反撃はしない");
        }
        // 剣（魔法の取り）も無効化し、起点の駒が前方 1×3 にいれば反撃する
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 5, 5);   // 後手の銀の前方 1×3 に先手の金
            Check(BoardSimulator.Apply(s, Mg("Sword", (5, 5))), "カウンター（剣）: apply");
            Check(!s.Contains(gi) && s.At(5, 5) == ki && Hand(s, P1, PieceType.SilverGeneral) == 1, "カウンター（剣）: 剣の起点の銀へ反撃する");
        }
        // 盾とカウンターの両方なら、先にカウンターが効く（盾は残る）
        {
            var (s, ki) = CounterSetup();
            ki.shield = true;
            Put(s, PieceType.SilverGeneral, P2, 5, 4);
            BoardSimulator.Apply(s, Mv(5, 4, 4, 4));
            Check(s.At(5, 4) == ki && !ki.counter && ki.shield, "カウンター（盾）: カウンターが先に効いて盾は残る");
        }
        // 反撃も「取り」：攻撃した駒が盾状態なら盾が割れ、反撃した駒は元のマスへ戻る
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 5, 4);
            gi.shield = true;
            BoardSimulator.Apply(s, Mv(5, 4, 4, 4));
            Check(s.At(5, 4) == gi && !gi.shield && s.At(4, 4) == ki, "カウンター（反撃を盾で）: 盾が割れ、反撃した駒は元のマス");
        }
        // 反撃がカウンター状態の駒に阻まれたら、その駒がさらに反撃する
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 5, 4);
            gi.counter = true;
            BoardSimulator.Apply(s, Mv(5, 4, 4, 4));
            Check(!s.Contains(ki) && s.At(4, 4) == gi && !gi.counter && Hand(s, P2, PieceType.GoldGeneral) == 1, "カウンター（返し）: 銀の前方に戻った金を取り返す");
            Check(s.counters == null, "カウンター（返し）: 予定は行動の中で処理して消す");
        }
        // 爆発・隕石などの消失は防げない（無効化も反撃もしない）
        {
            var (s, ki) = CounterSetup();
            Put(s, PieceType.Gunpowder, P1, 5, 4);
            SnapshotPiece gi = Put(s, PieceType.SilverGeneral, P2, 6, 4);
            BoardSimulator.Apply(s, Mv(6, 4, 5, 4));   // 先手の火薬のマスに入る → 3×3 が爆発
            Check(!s.Contains(ki) && !s.Contains(gi), "カウンター（爆発）: 消失は防げない");
        }
        // 停止世界の中で攻撃されたら、無効化も反撃もできない（CPU の先読みの近似：その場で取られる）
        {
            var (s, ki) = CounterSetup();
            Put(s, PieceType.SilverGeneral, P2, 5, 4);
            s.timeStopped = true; s.timeStopOwner = (int)P2; s.timeStopRemaining = 10; s.timeStopReleasable = true;
            Check(BoardSimulator.ApplyForSearch(s, Mv(5, 4, 4, 4)), "カウンター（停止世界）: apply");
            Check(!s.Contains(ki) && s.At(4, 4) != null && s.At(4, 4).type == PieceType.SilverGeneral && Hand(s, P2, PieceType.GoldGeneral) == 1, "カウンター（停止世界）: そのまま取られる");
        }
        // 狙撃も無効化する。狙撃手が前方 1×3 にいなければ反撃しない
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece sn = Put(s, PieceType.Sniper, P2, 8, 0);
            Check(BoardSimulator.Apply(s, MatchAction.Snipe(new Vector2Int(8, 0), new Vector2Int(4, 4))), "カウンター（狙撃）: apply");
            Check(s.At(4, 4) == ki && !ki.counter && s.At(8, 0) == sn, "カウンター（狙撃）: 無効化して、遠くの狙撃手には反撃しない");
        }
        // 突進（歩「突撃」）はカウンターの駒の手前で止まり、前方にいれば反撃される
        {
            var (s, ki) = CounterSetup();
            SnapshotPiece fu = Put(s, PieceType.Pawn, P2, 8, 4); fu.moveCount = 2;
            Check(BoardSimulator.Apply(s, Spc(8, 4)), "カウンター（突撃）: apply");
            Check(!s.Contains(fu) && s.At(5, 4) == ki && Hand(s, P1, PieceType.Pawn) == 1, "カウンター（突撃）: 手前で止まった歩へ反撃する");
        }
        // 金「金剛衝」の場外への押し出しも無効化し、押し出した金が前方 1×3 にいれば反撃する
        {
            BoardSnapshot s = Known(Basic(P2), new[] { "Counter" }, new[] { "Counter" });
            SnapshotPiece ki = Put(s, PieceType.GoldGeneral, P1, 0, 4); ki.counter = true;   // 盤の端（先手の最下段）
            SnapshotPiece g2 = Put(s, PieceType.GoldGeneral, P2, 1, 4); g2.moveCount = 2;
            Check(BoardSimulator.Apply(s, Spc(1, 4)), "カウンター（金剛衝）: apply");
            Check(!s.Contains(g2) && s.At(1, 4) == ki && Hand(s, P1, PieceType.GoldGeneral) == 1, "カウンター（金剛衝）: 場外への押し出しを無効化して反撃する");
        }
        // 反撃で敵陣に入ると成る（移動回数は数えない）
        {
            BoardSnapshot s = Known(Basic(P2), new[] { "Counter" }, new[] { "Counter" });
            SnapshotPiece gi1 = Put(s, PieceType.SilverGeneral, P1, 5, 4); gi1.counter = true;
            SnapshotPiece gi2 = Put(s, PieceType.SilverGeneral, P2, 6, 4);
            BoardSimulator.Apply(s, Mv(6, 4, 5, 4));
            Check(!s.Contains(gi2) && s.At(6, 4) == gi1 && gi1.promoted && gi1.moveCount == 0, "カウンター（成り）: 反撃で敵陣に入ると成る");
        }
        // ファランクスの槍も無効化し、槍の駒が前方 1×3 にいれば反撃する
        {
            BoardSnapshot s = Known(Basic(P2), new[] { "Counter" }, new[] { "Counter" });
            SnapshotPiece ki = Put(s, PieceType.GoldGeneral, P1, 4, 4); ki.counter = true;
            SnapshotPiece ph = Put(s, PieceType.SilverGeneral, P2, 6, 4); ph.shield = true; ph.phalanx = true;
            BoardSimulator.Apply(s, Mv(8, 8, 8, 7));   // 後手の手番の終わりにファランクスが前進（(5, 4)）して槍で突く
            Check(s.At(5, 4) == ph && !ph.shield && s.At(4, 4) == ki && !ki.counter, "カウンター（槍）: 槍を無効化 → 反撃は盾で防がれる");
        }
        // CPU の評価：カウンターの駒は加点し、取られそうな駒には数えない
        {
            var (s, ki) = CounterSetup();
            Put(s, PieceType.SilverGeneral, P2, 5, 4);
            double with = CpuEvaluate.Evaluate(s, P1);
            ki.counter = false;
            double without = CpuEvaluate.Evaluate(s, P1);
            Check(with > without + 0.5, $"カウンター（評価）: 加点と、取られそうな駒から外す（{with:0.00} / {without:0.00}）");
        }
        // JSON の往復でカウンター状態が残る
        {
            var (s, ki) = CounterSetup();
            BoardSnapshot back = BoardSnapshot.FromJson(s.ToJson());
            Check(back != null && back.At(4, 4) != null && back.At(4, 4).counter && back.Diff(s).Count == 0, "カウンター（JSON）: 往復で残る");
        }

        TestCounterHash();
        CounterPlayouts(60, new System.Random(5));
    }

    // カウンター状態は局面のハッシュに入る（カウンターのない局面のハッシュは変わらない。Prototype の hash.js と同じ足し方）
    static void TestCounterHash()
    {
        MatchSnapshot m = new MatchSnapshot();
        m.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.King, owner = 0, x = 0, y = 4 });
        m.pieces.Add(new PieceSnapshot { id = 2, type = PieceType.King, owner = 1, x = 8, y = 4 });
        PieceSnapshot ki = new PieceSnapshot { id = 3, type = PieceType.GoldGeneral, owner = 0, x = 4, y = 4 };
        m.pieces.Add(ki);
        string before = MatchHash.Compute(m);
        Check(MatchHash.BuildText(m).Contains("[\"GoldGeneral\",0,0,0,0,0,0]"), "カウンター（ハッシュ）: カウンターも翼もない駒は今までと同じ形");
        ki.counter = true;
        string counter = MatchHash.Compute(m);
        Check(counter != before && MatchHash.BuildText(m).Contains("[\"GoldGeneral\",0,0,0,0,0,0,0,1]"), "カウンター（ハッシュ）: [.., 翼 0, 1] を足す");
        ki.wing = true;
        string both = MatchHash.Compute(m);
        ki.counter = false;
        string wing = MatchHash.Compute(m);
        Check(both != wing && both != counter && MatchHash.BuildText(m).Contains("[\"GoldGeneral\",0,0,0,0,0,0,1]"), "カウンター（ハッシュ）: 翼だけなら今までどおり");
    }

    // カウンターを入れてランダムに指し続けても壊れない（合法手はすべて当てはめられ、反撃の予定は行動ごとに空になる）
    static void CounterPlayouts(int games, System.Random r)
    {
        string[] lo = { "Counter", "Sword", "Shield", "Phalanx", "TimeStop" };
        int plies = 0, counters = 0, strikes = 0;
        for (int g = 0; g < games; g++)
        {
            BoardSnapshot s = Known(Initial(), lo, lo, (uint)(1000 + g));
            foreach (SnapshotPiece p in s.pieces) p.initial = true;
            for (int ply = 0; ply < 200 && !s.gameOver; ply++)
            {
                if (r.NextDouble() < 0.4) { s.players[0].energy += 4; s.players[1].energy += 4; }
                List<MatchAction> legal = BoardSimulator.LegalActions(s, s.Current);
                if (legal.Count == 0) break;
                MatchAction a = legal[r.Next(legal.Count)];
                List<MatchAction> skills = legal.Where(x => x.type == MatchActionType.Magic || x.type == MatchActionType.Special).ToList();
                if (skills.Count > 0 && r.NextDouble() < 0.35) a = skills[r.Next(skills.Count)];
                if (a.type == MatchActionType.Magic && a.skill == "Counter") counters++;
                int before = s.pieces.Count(p => p.counter);
                int piecesBefore = s.pieces.Count;
                if (!BoardSimulator.ApplyForSearch(s, a)) { Check(false, $"counter playout {g}/{ply}: 合法手が当てはめられない {a}"); break; }
                if (s.pieces.Count(p => p.counter) < before && s.pieces.Count < piecesBefore) strikes++;
                plies++;
                Check(s.counters == null, $"counter playout {g}/{ply}: 反撃の予定が残っている");
                Invariants(s, $"counter playout {g}/{ply}");
            }
        }
        Log($"counter playouts: {games} 局・{plies} 手（カウンター {counters} 回・カウンターが効いて駒が減った手 {strikes}）");
    }
}
