// 必殺技・魔法・自動行動・詰みのシミュレーション（BoardSimulator.Skills.cs）と CPU（Cpu/CpuEvaluate.cs・CpuSearch.cs）のテスト。
// 期待値は Unity 側のクラス（Special/*.cs・Skill/SkillData/*Effect.cs・PhalanxAdvance・UndeadMarch・GameSystem）を読んで決めた。
// CPU のテストは Prototype の tests/cpu/cpu.test.js を移したもの
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class Program
{
    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------

    static SnapshotSkill Sk(string id, MagicKind kind, int cost, bool endsTurn, bool transcendent = false)
        => new SnapshotSkill { id = id, kind = kind, cost = cost, endsTurn = endsTurn, transcendent = transcendent };

    // Unity の既定の魔法（Assets/SkillData/Definition のコスト・手番）
    static readonly Dictionary<string, SnapshotSkill> Skills = new Dictionary<string, SnapshotSkill>
    {
        ["Shield"] = Sk("Shield", MagicKind.Shield, 3, true),
        ["Sword"] = Sk("Sword", MagicKind.Sword, 4, true),
        ["Pitfall"] = Sk("Pitfall", MagicKind.Pitfall, 2, true),
        ["DoubleTurn"] = Sk("DoubleTurn", MagicKind.DoubleTurn, 2, false),
        ["SwapPiece"] = Sk("SwapPiece", MagicKind.Swap, 4, false),
        ["Explosion"] = Sk("Explosion", MagicKind.Explosion, 5, false),
        ["ReplicateKing"] = Sk("ReplicateKing", MagicKind.ReplicateKing, 8, false),
        ["Phalanx"] = Sk("Phalanx", MagicKind.Phalanx, 7, true),
        ["Meteor"] = Sk("Meteor", MagicKind.Meteor, 10, true),
        ["Cannon"] = Sk("Cannon", MagicKind.Cannon, 18, true),
        ["Cataclysm"] = Sk("Cataclysm", MagicKind.Cataclysm, 30, true, true),
        ["WorldEnd"] = Sk("WorldEnd", MagicKind.WorldEnd, 30, true, true),
        ["UndeadParadise"] = Sk("UndeadParadise", MagicKind.UndeadParadise, 30, true, true),
        ["TimeStop"] = Sk("TimeStop", MagicKind.TimeStop, 30, true, true),
    };

    // 必殺技・魔法をシミュレーションできるスナップショットにする（Unity の既定の必殺技と、両者の編成）
    static BoardSnapshot Known(BoardSnapshot s, string[] p1 = null, string[] p2 = null, uint seed = 12345)
    {
        s.skillsKnown = true;
        void Sp(PieceType t, SpecialKind k, int moves) { s.specialKinds[(int)t] = k; s.specialMoves[(int)t] = moves; }
        Sp(PieceType.Pawn, SpecialKind.PawnCharge, 2);
        Sp(PieceType.Lance, SpecialKind.LancePierce, 2);
        Sp(PieceType.Knight, SpecialKind.KnightLeap, 2);
        Sp(PieceType.GoldGeneral, SpecialKind.GoldShock, 2);
        Sp(PieceType.Bishop, SpecialKind.BishopBeam, 3);
        Sp(PieceType.Rook, SpecialKind.RookBeam, 3);
        s.players[0].skills = (p1 ?? new string[0]).Select(id => Skills[id]).ToList();
        s.players[1].skills = (p2 ?? new string[0]).Select(id => Skills[id]).ToList();
        s.rngSeed = seed;
        s.rngState = seed;
        return s;
    }

    static MatchAction Spc(int x, int y, int tx = -1, int ty = -1)
        => tx < 0 ? MatchAction.Special(new Vector2Int(x, y)) : MatchAction.Special(new Vector2Int(x, y), new Vector2Int(tx, ty));

    static MatchAction Mg(string id, params (int x, int y)[] t)
        => MatchAction.Magic(id, t.Select(p => new Vector2Int(p.x, p.y)).ToArray());

    static bool Has(List<MatchAction> list, MatchAction a) => list.Any(b => b.Key == a.Key);

    static int Hand(BoardSnapshot s, PlayerType o, PieceType t) => s.HandCount(o, t);

    // ------------------------------------------------------------
    //  必殺技・魔法・自動行動・詰み
    // ------------------------------------------------------------
    static void TestSkills()
    {
        // 歩「突撃」：前方へ突き進み、最初の相手の駒を取って止まる。敵陣に入ったので成る。移動回数は 0 に
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Pawn, P1, 2, 4); p.moveCount = 2;
            Put(s, PieceType.SilverGeneral, P2, 6, 4);
            Check(Has(BoardSimulator.LegalActions(s, P1), Spc(2, 4)), "突撃: 合法手にある");
            Check(BoardSimulator.Apply(s, Spc(2, 4)), "突撃: apply");
            SnapshotPiece q = s.At(6, 4);
            Check(q == p && p.promoted && p.moveCount == 0, "突撃: 相手の駒のマスで止まり、成って移動回数 0");
            Check(Hand(s, P1, PieceType.SilverGeneral) == 1 && s.players[0].energy == 5 && s.players[1].energy == 6 && s.Current == P2, "突撃: 取り・エナジー・手番");
        }
        // 盾で防がれると、突撃はその手前で止まる（成らない）
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Pawn, P1, 2, 4); p.moveCount = 2;
            SnapshotPiece g = Put(s, PieceType.SilverGeneral, P2, 6, 4); g.shield = true;
            BoardSimulator.Apply(s, Spc(2, 4));
            Check(s.At(5, 4) == p && !p.promoted && s.At(6, 4) == g && !g.shield, "突撃: 盾で止まる");
        }
        // 移動回数が足りなければ使えない・アンデッドは使えない
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Pawn, P1, 2, 4); p.moveCount = 1;
            Put(s, PieceType.SilverGeneral, P2, 6, 4);
            Check(!BoardSimulator.CanApply(s, Spc(2, 4)), "突撃: 移動回数が足りない");
            p.moveCount = 2; p.undead = true;
            Check(!BoardSimulator.CanApply(s, Spc(2, 4)), "突撃: アンデッドは使えない");
        }
        // 香「貫通槍」：相手の駒をすべて取り、味方の駒の手前で止まる
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Lance, P1, 1, 4); p.moveCount = 2;
            Put(s, PieceType.Pawn, P2, 3, 4); Put(s, PieceType.Pawn, P2, 4, 4); Put(s, PieceType.GoldGeneral, P1, 6, 4);
            Check(BoardSimulator.Apply(s, Spc(1, 4)), "貫通槍: apply");
            Check(s.At(5, 4) == p && Hand(s, P1, PieceType.Pawn) == 2 && s.At(3, 4) == null && s.At(4, 4) == null, "貫通槍: 2 枚取って味方の手前で止まる");
        }
        // 桂「天馬跳躍」：周囲 2 マス以内の空き・相手の駒のマス（味方のマスは選べない）
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Knight, P1, 4, 4); p.moveCount = 2;
            Put(s, PieceType.GoldGeneral, P2, 6, 5); Put(s, PieceType.Pawn, P1, 5, 4);
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1).Where(a => a.type == MatchActionType.Special).ToList();
            Check(legal.Count == 23 && !Has(legal, Spc(4, 4, 5, 4)) && Has(legal, Spc(4, 4, 6, 5)), $"天馬跳躍: 対象 23 マス（{legal.Count}）");
            Check(!BoardSimulator.CanApply(s, Spc(4, 4)), "天馬跳躍: 対象なしは不可");
            BoardSimulator.Apply(s, Spc(4, 4, 6, 5));
            Check(s.At(6, 5) == p && p.promoted && Hand(s, P1, PieceType.GoldGeneral) == 1, "天馬跳躍: 着地点の駒を取って成る");
        }
        // 金「金剛衝」：周りの相手の駒を 1 マス押し出す。盤外なら取り、塞がっていれば止まる
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.GoldGeneral, P1, 7, 2); p.moveCount = 2;
            SnapshotPiece a = Put(s, PieceType.Pawn, P2, 8, 2);   // 盤外へ → 取り
            SnapshotPiece b = Put(s, PieceType.Pawn, P2, 6, 1);   // (5, 0) へ
            SnapshotPiece c = Put(s, PieceType.Pawn, P2, 7, 3);   // (7, 4) は味方の駒で塞がっている
            Put(s, PieceType.Pawn, P1, 7, 4);
            Check(BoardSimulator.Apply(s, Spc(7, 2)), "金剛衝: apply");
            Check(!s.Contains(a) && Hand(s, P1, PieceType.Pawn) == 1, "金剛衝: 盤外に押し出して取る");
            Check(s.At(5, 0) == b && s.At(7, 3) == c, "金剛衝: 空きへ押す・塞がっていれば止まる");
        }
        // 角「光芒四射」：斜めの最初の相手の駒を取る。味方の駒で止まる
        {
            BoardSnapshot s = Known(Basic());
            SnapshotPiece p = Put(s, PieceType.Bishop, P1, 4, 4); p.moveCount = 3;
            SnapshotPiece e = Put(s, PieceType.SilverGeneral, P2, 6, 6);
            SnapshotPiece f = Put(s, PieceType.SilverGeneral, P2, 2, 2);
            Put(s, PieceType.Pawn, P1, 3, 3);
            BoardSimulator.Apply(s, Spc(4, 4));
            Check(!s.Contains(e) && s.Contains(f) && s.At(4, 4) == p && p.moveCount == 0, "光芒四射: 当たった駒だけ取る");
        }

        // 魔法：盾（手番を使う）・編成していない魔法・エナジー不足
        {
            BoardSnapshot s = Known(Basic(), new[] { "Shield" });
            SnapshotPiece g = Put(s, PieceType.GoldGeneral, P1, 3, 3);
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1).Where(a => a.type == MatchActionType.Magic).ToList();
            Check(legal.Count == 2, $"盾: 対象は自分の駒 2 つ（{legal.Count}）");
            Check(!BoardSimulator.CanApply(s, Mg("Sword", (3, 3))), "編成していない魔法");
            Check(BoardSimulator.Apply(s, Mg("Shield", (3, 3))) && g.shield && s.players[0].energy == 1 && s.Current == P2, "盾: 盾状態・エナジー・手番");
            Check(!BoardSimulator.LegalActions(s, P2).Any(a => a.type == MatchActionType.Magic), "相手は盾を編成していない");
        }
        // 剣：前方 1×3 の相手の駒を取り、範囲の火薬は爆発（3×3 で術者も巻き込む）
        {
            BoardSnapshot s = Known(Basic(), new[] { "Sword" });
            s.players[0].energy = 10;
            SnapshotPiece c = Put(s, PieceType.SilverGeneral, P1, 4, 4);
            Put(s, PieceType.Pawn, P2, 5, 3); Put(s, PieceType.Pawn, P2, 5, 4); Put(s, PieceType.Gunpowder, P2, 5, 5);
            Put(s, PieceType.GoldGeneral, P1, 1, 1);
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1).Where(a => a.type == MatchActionType.Magic).ToList();
            Check(legal.Count == 1 && Has(legal, Mg("Sword", (4, 4))), "剣: 前方に相手の駒がいる駒だけ");
            BoardSimulator.Apply(s, Mg("Sword", (4, 4)));
            Check(Hand(s, P1, PieceType.Pawn) == 2 && s.At(5, 5) == null && !s.Contains(c), "剣: 取り 2 枚・火薬の爆発で術者も消える");
        }
        // 落とし穴：空きの通常マスだけ
        {
            BoardSnapshot s = Known(Basic(), new[] { "Pitfall" });
            s.Cell(4, 4).state = CellState.Burning; s.Cell(4, 4).burnUntil = 10;
            int n = BoardSimulator.LegalActions(s, P1).Count(a => a.type == MatchActionType.Magic);
            Check(n == 81 - 2 - 1, $"落とし穴: 空きの通常マス（{n}）");
            BoardSimulator.Apply(s, Mg("Pitfall", (3, 3)));
            Check(s.Cell(3, 3).trap == 0 && s.Current == P2, "落とし穴: 仕掛けて手番交代");
        }
        // 2回行動：手番を使わず、次の手番を使う行動のあとも手番が続く
        {
            BoardSnapshot s = Known(Basic(), new[] { "DoubleTurn" });
            Check(BoardSimulator.Apply(s, Mg("DoubleTurn")) && s.Current == P1 && s.extraTurn, "2回行動: 発動しても手番は続く");
            BoardSimulator.Apply(s, Mv(0, 0, 1, 0));
            Check(s.Current == P1 && !s.extraTurn, "2回行動: 1 手目のあとも続く");
            BoardSimulator.Apply(s, Mv(1, 0, 2, 0));
            Check(s.Current == P2, "2回行動: 2 手目で交代");
        }
        // 駒入替：自分の駒 2 つ（順序つきの組）
        {
            BoardSnapshot s = Known(Basic(), new[] { "SwapPiece" });
            SnapshotPiece g = Put(s, PieceType.GoldGeneral, P1, 3, 3);
            int n = BoardSimulator.LegalActions(s, P1).Count(a => a.type == MatchActionType.Magic);
            Check(n == 2, $"駒入替: 2 つの駒の組（{n}）");
            BoardSimulator.Apply(s, Mg("SwapPiece", (3, 3), (0, 0)));
            Check(s.At(0, 0) == g && s.At(3, 3).type == PieceType.King && s.Current == P1, "駒入替: 入れ替わり、手番は続く");
        }
        // 爆発：3×3 の駒が消失（王将は残る）。手番は続く
        {
            BoardSnapshot s = Known(Basic(), new[] { "Explosion" });
            s.players[0].energy = 6;
            Put(s, PieceType.GoldGeneral, P2, 7, 7); Put(s, PieceType.Pawn, P1, 8, 7);
            BoardSimulator.Apply(s, Mg("Explosion", (7, 7)));
            Check(s.At(7, 7) == null && s.At(8, 7) == null && s.At(8, 8) != null && s.Current == P1 && s.players[0].energy == 1, "爆発: 王将は残る・手番は続く");
        }
        // 王将複製：自陣の (0, 4) に 2 枚目の王将
        {
            BoardSnapshot s = Known(Basic(), new[] { "ReplicateKing" });
            s.players[0].energy = 8;
            BoardSimulator.Apply(s, Mg("ReplicateKing"));
            Check(s.KingCount(P1) == 2 && s.At(0, 4)?.type == PieceType.King, "王将複製: 2 枚目");
        }
        // ファランクス：盾の駒にかけ、手番の終わりに 1 マス前進して前方 1×3 の相手の駒を取る
        {
            BoardSnapshot s = Known(Basic(), new[] { "Phalanx" });
            s.players[0].energy = 7;
            SnapshotPiece g = Put(s, PieceType.SilverGeneral, P1, 4, 4); g.shield = true;
            Put(s, PieceType.SilverGeneral, P1, 3, 3);
            Put(s, PieceType.Pawn, P2, 6, 5);
            Check(BoardSimulator.LegalActions(s, P1).Count(a => a.type == MatchActionType.Magic) == 1, "ファランクス: 盾の駒だけ");
            BoardSimulator.Apply(s, Mg("Phalanx", (4, 4)));
            Check(g.phalanx && s.At(5, 4) == g && s.At(6, 5) == null && Hand(s, P1, PieceType.Pawn) == 1 && s.Current == P2, "ファランクス: 前進して取る");
            BoardSimulator.Apply(s, Mv(8, 8, 8, 7));
            BoardSimulator.Apply(s, Mv(0, 0, 0, 1));
            Check(s.At(6, 4) == g && g.promoted, "ファランクス: 次の手番の終わりも前進し、敵陣に入って成る");
        }
        // 隕石：中心は自陣か中立（王将を巻き込まない）。3×3 が消失し、10 ターン炎上
        {
            BoardSnapshot s = Known(Basic(), new[] { "Meteor" });
            s.players[0].energy = 10;
            Put(s, PieceType.GoldGeneral, P2, 4, 4); Put(s, PieceType.Pawn, P1, 3, 3);
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1).Where(a => a.type == MatchActionType.Magic).ToList();
            Check(legal.Count == 6 * 9 - 4, $"隕石: 中心は x 0〜5 で王将の 3×3 以外（{legal.Count}）");
            BoardSimulator.Apply(s, Mg("Meteor", (4, 4)));
            Check(s.At(4, 4) == null && s.At(3, 3) == null && s.Cell(5, 5).IsBurning && s.Cell(5, 5).burnUntil == 12, "隕石: 消失と炎上");
        }
        // 破壊砲：縦 3×9 を破壊（王将のいる範囲は選べない）
        {
            BoardSnapshot s = Known(Basic(), new[] { "Cannon" });
            s.players[0].energy = 18;
            Put(s, PieceType.Rook, P2, 6, 4);
            int n = BoardSimulator.LegalActions(s, P1).Count(a => a.type == MatchActionType.Magic);
            Check(n == 9 * 5, $"破壊砲: 筋 2〜6 を中心にする 45 マス（{n}）");
            BoardSimulator.Apply(s, Mg("Cannon", (0, 4)));
            Check(s.At(6, 4) == null && s.cells.Count(c => c.IsBroken) == 27, "破壊砲: 27 マス破壊");
        }
        // 超常魔法：解放するまで使えない。天変地異は同じ乱数なら同じ結果
        {
            BoardSnapshot s = Known(Initial(), new[] { "Cataclysm" }, null, 7);
            s.turnNumber = 11; s.players[0].energy = 30;
            Check(!BoardSimulator.CanApply(s, Mg("Cataclysm")), "天変地異: 未解放");
            s.players[0].transcendentUnlocked = true;
            BoardSimulator.Apply(s.Clone(), Mg("Cataclysm"));
            BoardSnapshot a1 = s.Clone(), a2 = s.Clone(), a3 = s.Clone();
            a3.rngState = 99;
            BoardSimulator.Apply(a1, Mg("Cataclysm")); BoardSimulator.Apply(a2, Mg("Cataclysm")); BoardSimulator.Apply(a3, Mg("Cataclysm"));
            Check(a1.Diff(a2).Count == 0 && a1.Diff(a3).Count > 0 && a1.pieces.Count == 40, "天変地異: 同じ乱数なら同じ結果・違う乱数なら違う結果");
            Check(a1.players[0].transcendentUsed && !BoardSimulator.CanApply(a1, Mg("Cataclysm")), "天変地異: 使用済み");
        }
        // 世界の終わり：発動で 3 マス、以後は 1 ターンの終わりごとに 3 マス崩れる（王将のマスは崩れない）
        {
            BoardSnapshot s = Known(Basic(), new[] { "WorldEnd" });
            s.players[0].energy = 30; s.players[0].transcendentUnlocked = true;
            BoardSimulator.Apply(s, Mg("WorldEnd"));
            Check(s.cells.Count(c => c.IsBroken) == 3 && s.players[0].worldEndSince == 3 && s.KingCount(P1) == 1, "世界の終わり: 発動で 3 マス");
            BoardSimulator.Apply(s, BoardSimulator.LegalActions(s, P2).First(a => a.type == MatchActionType.Move));
            Check(s.cells.Count(c => c.IsBroken) == 3, "世界の終わり: 発動したターンの終わりには崩れない");
            MatchAction m = BoardSimulator.LegalActions(s, P1).First(a => a.type == MatchActionType.Move);
            BoardSimulator.Apply(s, m);
            MatchAction m2 = BoardSimulator.LegalActions(s, P2).First(a => a.type == MatchActionType.Move);
            BoardSimulator.Apply(s, m2);
            Check(s.cells.Count(c => c.IsBroken) == 6, "世界の終わり: 次のターンの終わりに 3 マス");
        }
        // アンデッド・パラダイス：倒された初期配置の駒（王将以外）が自陣にアンデッドで復活し、手番の終わりに相手の王将へ進む
        {
            BoardSnapshot s = Known(Basic(), new[] { "UndeadParadise" });
            s.players[0].energy = 30; s.players[0].transcendentUnlocked = true;
            Check(!BoardSimulator.CanApply(s, Mg("UndeadParadise")), "アンデッド: 倒された駒がなければ使えない");
            s.players[0].fallenInitial.AddRange(new[] { PieceType.Pawn, PieceType.King, PieceType.GoldGeneral });
            BoardSimulator.Apply(s, Mg("UndeadParadise"));
            List<SnapshotPiece> undead = s.pieces.Where(p => p.undead).ToList();
            Check(undead.Count == 2 && s.players[0].fallenInitial.Count == 0, "アンデッド: 2 体復活・記録は消える");
            Check(undead.All(p => p.Owner == P1 && p.x <= 3 && p.moveCount == 1), "アンデッド: 発動した手番の終わりに 1 手ずつ進む");
            Check(!BoardSimulator.LegalActions(s.Clone(), P1).Any(), "アンデッド: 手番は相手");
        }
        // 停止世界（CPU の先読みだけの近似）：発動しても手番は続き、10 回まで行動できる。解除で手番交代
        {
            BoardSnapshot s = Known(Basic(), new[] { "TimeStop" });
            s.players[0].energy = 30; s.players[0].transcendentUnlocked = true;
            Check(BoardSimulator.ApplyForSearch(s, Mg("TimeStop")) && s.timeStopped && s.Current == P1 && s.timeStopRemaining == 10, "停止世界: 発動");
            Check(!BoardSimulator.Apply(s.Clone(), Mv(0, 0, 1, 0)), "停止世界: Apply は時間停止中の行動を当てはめない");
            BoardSimulator.ApplyForSearch(s, Mv(0, 0, 1, 0));
            Check(s.Current == P1 && s.timeStopRemaining == 9 && BoardSimulator.LegalActions(s, P1).Any(a => a.type == MatchActionType.ReleaseTimeStop), "停止世界: 1 回行動しても続く");
            BoardSimulator.ApplyForSearch(s, MatchAction.ReleaseTimeStop());
            Check(!s.timeStopped && s.Current == P2, "停止世界: 解除で手番交代");
        }
        // 詰み：手番の始まりに、王手を回避できる手がなければ終局
        {
            BoardSnapshot s = Known(Empty(11));
            Put(s, PieceType.King, P1, 0, 0);
            Put(s, PieceType.King, P2, 8, 4);
            Put(s, PieceType.Pawn, P1, 6, 4);
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.GoldGeneral)] = 1;
            BoardSnapshot t = s.Clone();
            BoardSimulator.Apply(t, MatchAction.Drop(PieceType.GoldGeneral, new Vector2Int(7, 4)));
            Check(t.gameOver && t.winner == 0, "詰み: 頭金で先手の勝ち");
            BoardSnapshot u = s.Clone();
            BoardSimulator.Apply(u, MatchAction.Drop(PieceType.GoldGeneral, new Vector2Int(7, 3)));
            Check(!u.gameOver && u.Current == P2, "詰み: 逃げられるなら続く");
        }
        // 超常魔法の解放：相手の駒が自陣にいて、駒数が相手のほうが多い
        {
            BoardSnapshot s = Known(Basic());
            Put(s, PieceType.Pawn, P2, 2, 4); Put(s, PieceType.Pawn, P2, 5, 4);
            BoardSimulator.Apply(s, Mv(0, 0, 1, 0));
            Check(s.players[0].transcendentUnlocked && !s.players[1].transcendentUnlocked, "解放: 侵入されて駒数で負けている側");
        }

        // 魔法・必殺技を入れて、ランダムに指し続けても壊れない（合法手はすべて当てはめられる）
        SkillPlayouts(150, new System.Random(4));
    }

    static readonly string[][] Loadouts =
    {
        new[] { "Shield", "Sword", "Pitfall", "Phalanx", "TimeStop" },
        new[] { "DoubleTurn", "SwapPiece", "Explosion", "Meteor", "UndeadParadise" },
        new[] { "Shield", "ReplicateKing", "Sword", "Cannon", "Cataclysm" },
        new[] { "Pitfall", "Explosion", "Shield", "Meteor", "WorldEnd" },
    };

    static BoardSnapshot SkillGame(int lo0, int lo1, uint seed)
    {
        BoardSnapshot s = Known(Initial(), Loadouts[lo0], Loadouts[lo1], seed);
        foreach (SnapshotPiece p in s.pieces) p.initial = true;
        return s;
    }

    static void SkillPlayouts(int games, System.Random r)
    {
        int plies = 0, ended = 0, magic = 0, special = 0;
        for (int g = 0; g < games; g++)
        {
            BoardSnapshot s = SkillGame(g % 4, (g / 4) % 4, (uint)g);
            for (int ply = 0; ply < 200 && !s.gameOver; ply++)
            {
                if (r.NextDouble() < 0.3) { s.players[0].energy += 3; s.players[1].energy += 3; } // 魔法を使えるように
                if (r.NextDouble() < 0.05) { s.players[0].transcendentUnlocked = true; s.players[1].transcendentUnlocked = true; }
                List<MatchAction> legal = BoardSimulator.LegalActions(s, s.Current);
                if (legal.Count == 0) break;
                MatchAction a = legal[r.Next(legal.Count)];
                List<MatchAction> skills = legal.Where(x => x.type == MatchActionType.Magic || x.type == MatchActionType.Special).ToList();
                if (skills.Count > 0 && r.NextDouble() < 0.3) a = skills[r.Next(skills.Count)];
                if (a.type == MatchActionType.Magic) magic++;
                if (a.type == MatchActionType.Special) special++;
                if (!BoardSimulator.ApplyForSearch(s, a)) { Check(false, $"skill playout {g}/{ply}: 合法手が当てはめられない {a}"); break; }
                plies++;
                Invariants(s, $"skill playout {g}/{ply}");
            }
            if (s.gameOver) ended++;
        }
        Console.WriteLine($"skill playouts: {games} 局・{plies} 手（魔法 {magic}・必殺技 {special}・終局 {ended} 局）");
    }

    // ------------------------------------------------------------
    //  CPU（Prototype の tests/cpu/cpu.test.js）
    // ------------------------------------------------------------

    // 呼ぶたびに 1 ミリ秒進む時計（時間切れを決定的にする）
    static Func<double> Clock() { double t = 0; return () => t += 1; }

    static CpuSearch.Options Opt(int level, double limit = 150) => new CpuSearch.Options { Level = level, TimeLimitMs = limit, Now = Clock() };

    static BoardSnapshot KingsOnly()
    {
        BoardSnapshot s = Known(Empty(11));
        Put(s, PieceType.King, P1, 0, 4);
        Put(s, PieceType.King, P2, 8, 4);
        return s;
    }

    static void TestCpu()
    {
        // 合法手を返し、局面（乱数の状態を含む）を書き換えない。同じ局面なら同じ手
        {
            System.Random r = new System.Random(5);
            int n = 0;
            for (int g = 0; g < 6; g++)
            {
                BoardSnapshot s = SkillGame(g % 4, (g + 1) % 4, (uint)(100 + g));
                for (int ply = 0; ply < 60 && !s.gameOver; ply++)
                {
                    s.players[0].energy += 1; s.players[1].energy += 1;
                    if (ply == 30) { s.players[0].transcendentUnlocked = true; s.players[1].transcendentUnlocked = true; }
                    List<MatchAction> legal = BoardSimulator.LegalActions(s, s.Current);
                    if (legal.Count == 0) break;
                    if (ply % 13 == 3)
                    {
                        string before = s.ToJson();
                        HashSet<string> keys = new HashSet<string>(legal.Select(a => a.Key));
                        foreach (int level in new[] { 0, 1, 2 })
                        {
                            MatchAction a = CpuSearch.Choose(s, Opt(level));
                            Check(a != null && keys.Contains(a.Key), $"cpu: 合法手を返す g{g} ply{ply} Lv{level} {a}");
                            Check(s.ToJson() == before, $"cpu: 局面を書き換えない g{g} ply{ply} Lv{level}");
                            MatchAction b = CpuSearch.Choose(s, Opt(level));
                            Check(b != null && a.Key == b.Key, $"cpu: 同じ局面なら同じ手 g{g} ply{ply} Lv{level}");
                            n++;
                        }
                    }
                    BoardSimulator.ApplyForSearch(s, legal[r.Next(legal.Count)]);
                }
            }
            Console.WriteLine($"cpu: {n} 回考えた（合法・書き換えない・同じ手）");
        }

        // 王を取れるなら取る（Lv1・Lv2）
        {
            BoardSnapshot s = KingsOnly();
            Put(s, PieceType.Rook, P1, 5, 4);
            foreach (int level in new[] { 1, 2 })
            {
                MatchAction a = CpuSearch.Choose(s, Opt(level, 1500));
                Check(a != null && a.type == MatchActionType.Move && a.from == new MatchSquare(5, 4) && a.to == new MatchSquare(8, 4), $"cpu: 王を取る Lv{level}（{a}）");
            }
        }

        // 評価値：勝ち・負けは ±Win。駒得なら正
        {
            BoardSnapshot s = KingsOnly();
            Check(Math.Abs(CpuEvaluate.Evaluate(s, P1)) < 1e-6, "evaluate: 王だけなら 0");
            Put(s, PieceType.Rook, P1, 2, 0);
            Check(CpuEvaluate.Evaluate(s, P1) > 5 && CpuEvaluate.Evaluate(s, P2) < -5, "evaluate: 駒得なら正");
            BoardSnapshot t = s.Clone();
            t.Remove(t.At(8, 4)); t.gameOver = true; t.winner = 0;
            Check(CpuEvaluate.Evaluate(t, P1) == CpuEvaluate.Win && CpuEvaluate.Evaluate(t, P2) == -CpuEvaluate.Win, "evaluate: 勝敗");
        }

        // 停止世界の中では 1 回ずつ行動を選び、最後は解除して時間を動かす
        {
            BoardSnapshot s = SkillGame(0, 1, 7);
            s.players[0].energy = 40; s.players[0].transcendentUnlocked = true;
            Check(BoardSimulator.ApplyForSearch(s, Mg("TimeStop")) && s.timeStopped && s.Current == P1, "cpu time stop: 発動");
            int n = 0;
            while (s.timeStopped && n < 20)
            {
                MatchAction a = CpuSearch.Choose(s, Opt(1, 300));
                Check(a != null && BoardSimulator.ApplyForSearch(s, a), $"cpu time stop: {a}");
                n++;
            }
            Check(!s.timeStopped && s.Current == P2 && n <= 11, $"cpu time stop: 解除して手番交代（{n} 回）");
        }

        // CPU 同士（Lv1 対 Lv2）で最後まで対局できる
        {
            int[][] pairs = { new[] { 0, 1 }, new[] { 2, 3 }, new[] { 1, 2 }, new[] { 3, 0 } };
            foreach (int[] pr in pairs)
            {
                BoardSnapshot s = SkillGame(pr[0], pr[1], (uint)(11 + pr[0]));
                int n = 0;
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                while (!s.gameOver && n < 300)
                {
                    MatchAction a = CpuSearch.Choose(s, Opt(s.Current == P1 ? 1 : 2, 120));
                    if (a == null) { Check(false, $"cpu vs cpu {pr[0]}-{pr[1]}: 指せる手がない（{n} 手目）"); break; }
                    if (a.type == MatchActionType.Forge) { BoardSimulator.ApplyForSearch(s, a); n++; continue; }
                    Check(BoardSimulator.ApplyForSearch(s, a), $"cpu vs cpu {pr[0]}-{pr[1]}: {n + 1} 手目 {a}");
                    Invariants(s, $"cpu vs cpu {pr[0]}-{pr[1]} {n}");
                    n++;
                }
                Console.WriteLine($"cpu vs cpu（編成 {pr[0]} 対 {pr[1]}）: {n} 手・{(s.gameOver ? (s.winner == 0 ? "先手 Lv1 の勝ち" : s.winner == 1 ? "後手 Lv2 の勝ち" : "引き分け") : "300 手で打ち切り")}・{sw.ElapsedMilliseconds} ms");
            }
        }

        // 実時間で 1 手あたりの思考時間（初期配置・魔法あり）
        {
            BoardSnapshot s = SkillGame(0, 1, 3);
            foreach (int level in new[] { 1, 2 })
            {
                CpuSearch search = new CpuSearch(s, new CpuSearch.Options { Level = level, TimeLimitMs = 1500 });
                System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
                search.Run();
                Console.WriteLine($"cpu Lv{level}: 初期配置で {sw.ElapsedMilliseconds} ms・{search.Simulated} 手を試した → {search.Result}");
            }
        }
    }
}
