// 錬成駒「巨人」のシミュレーション（BoardSnapshot・BoardSimulator・SnapshotRules・CheckRules・MatchHash・CpuEvaluate）のテスト。
// Prototype の tests/core/giant.test.js の場面を、Unity の座標（x = 段・y = 筋。先手は +x が前）に移したもの：
// Prototype の (x, y) は Unity の (8 - y, x)。期待値は Prototype の README・src/core/moves.js・play.js・effects.js に合わせた。
// 討伐任務（ボス）の場面は Unity にそのモードがないので移していない。時間停止の場面は、シミュレーションが時間停止中の行動を
// 当てはめないので「巨人は動けない」だけを確かめる（予定と解除後の HP は本編の GameSystem.TimeStop.cs）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class SnapshotSuite
{
    // 王だけの盤（先手の手番。王将は Prototype の kingsOnly と同じ (4,8)・(4,0) → Unity の (0,4)・(8,4)）。両者とも 盾・剣・落とし穴・隕石 を編成
    static BoardSnapshot GiantBoard(string[] p2 = null)
    {
        BoardSnapshot s = Empty(3, P1);
        Put(s, PieceType.King, P1, 0, 4);
        Put(s, PieceType.King, P2, 8, 4);
        Known(s, new[] { "Shield", "Sword", "Pitfall", "Meteor", "Explosion" }, p2 ?? new[] { "Shield", "Sword", "Pitfall", "Meteor" });
        s.players[0].energy = 50;
        s.players[1].energy = 50;
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Giant, cost = 5 });
        return s;
    }

    static SnapshotPiece PutGiant(BoardSnapshot s, PlayerType o, int x, int y)
    {
        SnapshotPiece g = new SnapshotPiece { type = PieceType.Giant, owner = (int)o, x = x, y = y, hp = GiantRules.MaxHp };
        s.Place(g);
        return g;
    }

    static List<string> CellsOf(BoardSnapshot s, SnapshotPiece p)
    {
        List<string> res = new List<string>();
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++) if (s.At(x, y) == p) res.Add($"{x},{y}");
        return res;
    }

    static List<string> MovesOf(BoardSnapshot s, SnapshotPiece p)
    {
        List<string> res = new List<string>();
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++) if (BoardSimulator.CanMoveTo(s, p, x, y)) res.Add($"{x},{y}");
        return res;
    }

    static void TestGiant()
    {
        // 錬成できる（5 エナジー）。持ち駒になる
        {
            BoardSnapshot s = GiantBoard();
            Check(BoardSimulator.LegalActions(s, P1).Any(a => a.type == MatchActionType.Forge && a.piece == PieceType.Giant), "巨人: 錬成できる");
            Check(BoardSimulator.Apply(s, MatchAction.Forge(PieceType.Giant)), "巨人: 錬成 apply");
            Check(Hand(s, P1, PieceType.Giant) == 1 && s.players[0].energy == 45, "巨人: 持ち駒になり、エナジー -5");
        }

        // 打つと 3×3 マスを占める（中央のマスを選ぶ）。盤上の駒としては 1 枚
        {
            BoardSnapshot s = GiantBoard();
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Giant)] = 1;
            Check(BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 1, 2), "巨人（打つ）: 中央を選べる");
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 1, 0), "巨人（打つ）: 盤からはみ出す位置は不可");
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 1, 4), "巨人（打つ）: 王将に重なる位置は不可");
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 7, 2), "巨人（打つ）: 前へ動けない位置／序盤の敵陣は不可");
            s.turnNumber = 20;
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 7, 2) && BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 6, 2), "巨人（打つ）: 敵陣でも前へ 1 マス動ければ打てる");
            Check(!BoardSimulator.CanDropTo(s, P2, PieceType.Giant, 1, 2) && BoardSimulator.CanDropTo(s, P2, PieceType.Giant, 2, 2), "巨人（打つ）: 後手の前は -x");
            s.turnNumber = 3;
            s.Cell(2, 3).state = CellState.Burning;
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Giant, 1, 2), "巨人（打つ）: 9 マスのどれかが炎上なら不可");
            s.Cell(2, 3).state = CellState.Normal;
            Check(BoardSimulator.Apply(s, MatchAction.Drop(PieceType.Giant, new Vector2Int(2, 2))), "巨人（打つ）: apply");
            SnapshotPiece g = s.At(2, 2);
            Check(g != null && g.type == PieceType.Giant && g.Owner == P1 && g.hp == 3, "巨人（打つ）: HP 3 で置かれる");
            Check(CellsOf(s, g).Count == 9 && s.At(3, 1) == g && s.At(1, 3) == g, "巨人（打つ）: 9 マスを占める");
            Check(s.pieces.Count(p => p == g) == 1 && BoardSimulator.LegalActions(s, P2).Count(a => a.type == MatchActionType.Move && a.from == new MatchSquare(2, 2)) == 0, "巨人（打つ）: 盤上の駒としては 1 枚");
            Invariants(s, "巨人（打つ）");
        }

        // 前へまっすぐ 1〜5 マスだけ動ける（移動先は先頭の 1 列の中央のマス）
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 1, 2);
            Check(string.Join(" ", MovesOf(s, g)) == "3,2 4,2 5,2 6,2 7,2", $"巨人（移動）: 移動先 {string.Join(" ", MovesOf(s, g))}");
            Check(BoardSimulator.LegalActions(s, P1).Count(a => a.type == MatchActionType.Move && a.from == new MatchSquare(1, 2)) == 5, "巨人（移動）: 合法手は中央のマスから 5 つ");
            Check(BoardSimulator.Apply(s, Mv(1, 2, 5, 2)), "巨人（移動）: apply");
            Check(g.x == 4 && g.y == 2 && g.moveCount == 1, "巨人（移動）: 中央は先頭の 1 つ手前・移動回数 +1");
            Check(string.Join(" ", CellsOf(s, g)) == "3,1 3,2 3,3 4,1 4,2 4,3 5,1 5,2 5,3" && s.At(1, 2) == null, "巨人（移動）: 占めるマスが動く");
            Invariants(s, "巨人（移動）");
        }

        // 味方の駒・破壊マスで止まる。先頭の 1 列の相手の駒は全部取る
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 1, 2);
            Put(s, PieceType.Pawn, P2, 4, 1);
            Put(s, PieceType.GoldGeneral, P2, 4, 3);
            Check(string.Join(" ", MovesOf(s, g)) == "3,2 4,2", "巨人（取り）: 相手の駒の列で止まる");
            Check(BoardSimulator.Apply(s, Mv(1, 2, 4, 2)), "巨人（取り）: apply");
            Check(g.x == 3 && g.y == 2, "巨人（取り）: 先頭の列まで進む");
            Check(Hand(s, P1, PieceType.Pawn) == 1 && Hand(s, P1, PieceType.GoldGeneral) == 1, "巨人（取り）: 2 枚とも取って持ち駒");
            Put(s, PieceType.Pawn, P1, 6, 3);
            Check(string.Join(" ", MovesOf(s, g)) == "5,2", "巨人（取り）: 味方の駒の手前で止まる");
            s.Cell(6, 1).state = CellState.Broken;
            Check(string.Join(" ", MovesOf(s, g)) == "5,2", "巨人（取り）: 破壊マスの手前で止まる");
            Invariants(s, "巨人（取り）");
        }

        // 盾の駒が先頭の列にいれば、取れた駒は取って、巨人は元の位置へ跳ね返る（移動回数は数える）
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 1, 2);
            Put(s, PieceType.Pawn, P2, 4, 1);
            SnapshotPiece shield = Put(s, PieceType.GoldGeneral, P2, 4, 3); shield.shield = true;
            BoardSimulator.Apply(s, Mv(1, 2, 4, 2));
            Check(g.x == 1 && g.y == 2 && g.moveCount == 1, "巨人（盾）: 跳ね返る（移動回数は数える）");
            Check(Hand(s, P1, PieceType.Pawn) == 1 && s.At(4, 3) == shield && !shield.shield, "巨人（盾）: 歩は取り、盾は割れる");
        }

        // 3 回攻撃を受けると倒れる。2 回目までは攻撃した駒が元のマスへ跳ね返る。3 回目で取られて持ち駒になる
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            SnapshotPiece hi = Put(s, PieceType.Rook, P2, 7, 4);
            s.current = (int)P2;
            Check(BoardSimulator.CanMoveTo(s, hi, 4, 4), "巨人（HP）: 飛車は巨人のマスへ動ける");
            foreach (int hp in new[] { 2, 1 })
            {
                Check(BoardSimulator.Apply(s, Mv(7, 4, 4, 4)), $"巨人（HP {hp}）: apply");
                Check(g.hp == hp && s.At(7, 4) == hi && s.At(4, 4) == g, $"巨人（HP {hp}）: HP が減り、飛車は跳ね返る");
                s.current = (int)P2;
            }
            Check(hi.moveCount == 2, "巨人（HP）: 跳ね返った攻撃も移動として数える");
            BoardSimulator.Apply(s, Mv(7, 4, 4, 4));
            Check(!s.pieces.Contains(g) && s.At(4, 4) == hi && s.At(3, 4) == null, "巨人（HP 0）: 取られて、飛車がそのマスへ入る");
            Check(Hand(s, P2, PieceType.Giant) == 1, "巨人（HP 0）: 相手の持ち駒になる");
            Invariants(s, "巨人（HP 0）");
        }

        // 盾状態にできない（盾・剣の対象・起点に選べない）
        {
            BoardSnapshot s = GiantBoard();
            PutGiant(s, P1, 3, 4);
            Put(s, PieceType.Pawn, P2, 5, 4);
            Check(!BoardSimulator.CanApply(s, Mg("Shield", (3, 4))), "巨人: 盾にできない");
            Check(!BoardSimulator.CanApply(s, Mg("Sword", (3, 4))), "巨人: 剣の起点にできない");
        }

        // 落とし穴のマスに入ると 1 回ダメージを受ける（消えない。穴は閉じる）
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 1, 2);
            s.Cell(3, 3).trap = 1;
            Check(string.Join(" ", MovesOf(s, g)) == "3,2", "巨人（落とし穴）: 落とし穴の列で止まる");
            BoardSimulator.Apply(s, Mv(1, 2, 3, 2));
            Check(g.x == 2 && g.y == 2 && g.hp == 2 && !s.Cell(3, 3).HasTrap, "巨人（落とし穴）: HP -1・穴は閉じる");
        }

        // 爆発・隕石は何マス重なっても 1 回の攻撃
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            Check(BoardSimulator.Apply(s, Mg("Explosion", (3, 4))), "巨人（爆発）: apply");
            Check(g.hp == 2, "巨人（爆発）: 1 回の攻撃");
            s.current = (int)P2;
            Check(BoardSimulator.Apply(s, Mg("Meteor", (4, 4))), "巨人（隕石）: apply");
            Check(g.hp == 1 && s.At(3, 4) == g, "巨人（隕石）: 1 回の攻撃で残る");
        }

        // 剣・破壊砲・天変地異
        {
            BoardSnapshot s = GiantBoard(new[] { "Shield", "Sword", "Pitfall", "Cannon", "Cataclysm" });
            s.players[1].energy = 100;
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            Put(s, PieceType.GoldGeneral, P2, 5, 4);   // 前方 1×3（後手の前は -x）= (4, 3..5) は全部巨人のマス
            s.current = (int)P2;
            Check(BoardSimulator.Apply(s, Mg("Sword", (5, 4))), "巨人（剣）: apply");
            Check(g.hp == 2, "巨人（剣）: 3 マスに重なっても 1 回");
            s.current = (int)P2;
            Check(BoardSimulator.Apply(s, Mg("Cannon", (4, 6))), "巨人（破壊砲）: apply（筋 5〜7。王将の筋 4 は避ける）");
            Check(g.hp == 1, "巨人（破壊砲）: 1 回の攻撃");
            Check(s.Cell(3, 5).state == CellState.Normal && s.Cell(8, 6).IsBroken, "巨人（破壊砲）: 生き残った巨人のマスは壊れない");
            s.current = (int)P2;
            s.players[1].transcendentUnlocked = true;
            Check(BoardSimulator.Apply(s, Mg("Cataclysm")), "巨人（天変地異）: apply");
            Check(g.x == 3 && g.y == 4 && CellsOf(s, g).Count == 9, "巨人（天変地異）: 巨人のマスは動かない");
            Invariants(s, "巨人（天変地異）");
        }

        // 狙撃でも 1 回の攻撃
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            SnapshotPiece sn = Put(s, PieceType.Sniper, P2, 8, 0);
            s.current = (int)P2;
            Check(BoardSimulator.SnipeTargets(s, sn).Contains(new MatchSquare(3, 4)), "巨人（狙撃）: 撃てる");
            Check(BoardSimulator.Apply(s, MatchAction.Snipe(new Vector2Int(8, 0), new Vector2Int(3, 4))), "巨人（狙撃）: apply");
            Check(g.hp == 2, "巨人（狙撃）: 1 回の攻撃");
        }

        // 巨人の先頭の 1 列に相手の王将が入れば王手（Unity の CheckRules とも同じ）
        {
            BoardSnapshot s = GiantBoard();
            PutGiant(s, P2, 4, 7);   // 先頭の列は筋 6〜8。王将（0, 4）には届かない
            Check(!SnapshotRules.IsInCheck(s, P1) && !CheckRules.IsInCheck(Build(s).Board, P1), "巨人（王手）: 届かない");
            BoardSnapshot s2 = GiantBoard();
            PutGiant(s2, P2, 4, 3);  // 先頭の列は筋 2〜4：前へ進むと段 0 の列に王将が入る
            Check(SnapshotRules.IsInCheck(s2, P1) && CheckRules.IsInCheck(Build(s2).Board, P1), "巨人（王手）: 届く");
            Put(s2, PieceType.Pawn, P1, 2, 2);       // 途中の列に先手の駒：そこで止まるので届かない
            Check(!SnapshotRules.IsInCheck(s2, P1) && !CheckRules.IsInCheck(Build(s2).Board, P1), "巨人（王手）: 途中の駒で止まる");
            CompareRules(Build(s2), "巨人（王手）");
        }

        // 時間停止中は巨人は動けない
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            s.timeStopped = true;
            s.timeStopOwner = 0;
            Check(MovesOf(s, g).Count == 0, "巨人（時間停止）: 動けない");
        }

        // ハッシュ：巨人がいなければ今までと同じ形（持ち駒の巨人 0 は入れない）。持ち駒・HP は入る
        {
            MatchSnapshot m = new MatchSnapshot();
            m.players.Add(new PlayerSnapshot { owner = 0 });
            m.players.Add(new PlayerSnapshot { owner = 1 });
            foreach (PlayerSnapshot ps in m.players)
                foreach (PieceType t in PieceRules.HandOrder) ps.hand.Add(new HandCount { piece = t, count = 0 });
            m.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.King, owner = 0, x = 0, y = 4 });
            m.pieces.Add(new PieceSnapshot { id = 2, type = PieceType.King, owner = 1, x = 8, y = 4 });
            string text = MatchHash.BuildText(m);
            Check(!text.Contains("Giant") && text.EndsWith("null]"), "巨人（ハッシュ）: 巨人がいなければ今までと同じ形");
            m.players[0].hand[BoardSnapshot.HandIndex(PieceType.Giant)] = new HandCount { piece = PieceType.Giant, count = 1 };
            Check(MatchHash.BuildText(m).Contains("\"Bird\":0,\"Giant\":1}"), "巨人（ハッシュ）: 持っているときだけ持ち駒に入る");
            m.players[0].hand[BoardSnapshot.HandIndex(PieceType.Giant)] = new HandCount { piece = PieceType.Giant, count = 0 };
            PieceSnapshot g = new PieceSnapshot { id = 3, type = PieceType.Giant, owner = 0, x = 3, y = 4, hp = 3 };
            m.pieces.Add(g);
            string before = MatchHash.Compute(m);
            Check(MatchHash.BuildText(m).EndsWith(",[[3,4,3]]]") && System.Text.RegularExpressions.Regex.Matches(MatchHash.BuildText(m), "\"Giant\"").Count == 9,
                "巨人（ハッシュ）: 9 マスに入り、最後に HP");
            g.hp = 2;
            Check(MatchHash.Compute(m) != before, "巨人（ハッシュ）: HP が変わればハッシュも変わる");
        }

        // JSON の往復：巨人のマスと HP が戻る
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            g.hp = 2;
            BoardSnapshot t = BoardSnapshot.FromJson(s.ToJson());
            Check(t != null && t.Diff(s).Count == 0 && t.At(4, 5) != null && t.At(4, 5).hp == 2, "巨人（JSON）: 往復で同じ");
        }

        // CPU：巨人のいる局面を評価でき、合法な手を選ぶ（評価は HP で下がる）
        {
            BoardSnapshot s = GiantBoard();
            SnapshotPiece g = PutGiant(s, P1, 3, 4);
            double full = CpuEvaluate.Evaluate(s, P1);
            g.hp = 1;
            Check(CpuEvaluate.Evaluate(s, P1) < full, "巨人（CPU）: HP が減ると評価が下がる");
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Giant)] = 1;
            MatchAction a = CpuSearch.Choose(s, Opt(1, 300));
            Check(a != null && BoardSimulator.CanApply(s, a), $"巨人（CPU）: 合法な手 {a}");
        }

        // ランダムに指し続けても壊れない（巨人を錬成・打つ・動かす。ときどき CheckRules と照合）
        int plies = 0, giantMoves = 0;
        System.Random r = new System.Random(7);
        for (int game = 0; game < 60; game++)
        {
            BoardSnapshot s = Known(Initial(), new[] { "Shield", "Sword", "Meteor", "Explosion", "Cannon" }, new[] { "Shield", "Sword", "Meteor", "Bomb", "Cannon" }, (uint)(100 + game));
            s.recipes.Add(new SnapshotRecipe { piece = PieceType.Giant, cost = 5 });
            s.players[0].energy = 30;
            s.players[1].energy = 30;
            for (int ply = 0; ply < 150 && !s.gameOver; ply++)
            {
                List<MatchAction> legal = BoardSimulator.LegalActions(s, s.Current);
                if (legal.Count == 0) break;
                MatchAction a = legal[r.Next(legal.Count)];
                var giantActs = legal.Where(x => x.piece == PieceType.Giant || (x.type == MatchActionType.Move && s.At(x.from.x, x.from.y).IsGiant)).ToList();
                if (giantActs.Count > 0 && r.NextDouble() < 0.5) a = giantActs[r.Next(giantActs.Count)];
                if (a.type == MatchActionType.Move && s.At(a.from.x, a.from.y).IsGiant) giantMoves++;
                if (!BoardSimulator.Apply(s, a, out string reason)) { Check(false, $"巨人の playout {game}/{ply}: 当てはめられない {a} {reason}"); break; }
                plies++;
                Invariants(s, $"巨人の playout {game}/{ply}");
                if (ply % 15 == 0) { CompareRules(Build(s), $"巨人の playout {game}/{ply}"); TestBoards.ReleaseAll(); }
            }
        }
        Log($"giant playouts: 60 局・{plies} 手（巨人の移動 {giantMoves}）");
    }
}
