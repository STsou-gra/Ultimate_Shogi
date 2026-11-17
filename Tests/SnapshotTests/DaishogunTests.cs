// 討伐任務の 2 体目のボス「大将軍」と、その陣の駒（軍歩兵・軍銃兵・防壁）のテスト。
//  ・Prototype の tests/core/daishogun.test.js・soldiers.test.js・walls.test.js・boss-mechanics.test.js を移したもの
//    （試験用のボス・攻撃を登録する場面は、大将軍の弓・守備で同じことを確かめる）
//  ・Prototype の棋譜 tests/kifus/raid-daishogun-1〜4.json の再生は BossTests.cs の TestRaidKifus
//
// 座標：BossTests.cs と同じく、場面は Prototype の (px = 筋, py = 段) のまま書く（Unity は (8 − py, px)）
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class Program
{
    static void TestDaishogun()
    {
        TestBossMechanics();
        TestWalls();
        TestWallShield();
        TestSoldiers();
        TestGunners();
        TestDaishogunStart();
        TestDaishogunAttacks();
        TestDaishogunResult();
        TestDaishogunState();
        TestDaishogunMatchHash();
    }

    // ------------------------------------------------------------
    //  MatchHash・MatchSnapshot の大将軍の項目（防壁の HP・軍銃兵の照準・弓の照準・駒の番号。JSON の往復）
    // ------------------------------------------------------------
    static void TestDaishogunMatchHash()
    {
        MatchSnapshot m = new MatchSnapshot();
        m.players.Add(new PlayerSnapshot { owner = 0 });
        m.players.Add(new PlayerSnapshot { owner = 1 });
        m.ruleset = "kassen";
        m.mode = "bossRaid";
        m.clock = new ClockSnapshot { active = true, tick = 230, time = 220 };
        m.boss = BossState.Create(DAI);
        m.raid = new RaidState { active = true, minionSerial = 2, nextPieceId = 4 };
        m.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.King, owner = 0, x = 0, y = 4, serial = 1 });
        string plain = MatchHash.BuildText(m);
        Check(plain.EndsWith(",[\"daishogun\",6,3,30,-1,0,\"cooldown\",200,null,null],[0]]"), $"MatchHash: 防壁・軍銃兵・照準がなければ項目を足さない（{plain.Substring(Math.Max(0, plain.Length - 70))}）");
        m.pieces.Add(new PieceSnapshot { id = 2, type = PieceType.GunJu, owner = 1, x = 8, y = 0, serial = 2, gunner = true, gunNext = 320, aim = new AimData { active = true, target = 1, hasCell = true, x = 0, y = 4 } });
        m.pieces.Add(new PieceSnapshot { id = 3, type = PieceType.Bouheki, owner = 1, x = 7, y = 0, serial = 3, hp = 2, minionSerial = 1 });
        string text = MatchHash.BuildText(m);
        Check(text.EndsWith(",[0],[[[7,0,2]],[[8,0,320,[1,[0,4],0]]],null]]"), $"MatchHash: 防壁の HP・軍銃兵の照準を足す（{text.Substring(Math.Max(0, text.Length - 70))}）");
        m.boss.ai.phase = BossAiState.Telegraph;
        m.boss.ai.attack = "bow";
        m.boss.ai.hasCells = true;
        m.boss.ai.cells = new List<MatchSquare> { new MatchSquare(0, 4) };
        m.boss.ai.aim = new AimData { active = true, target = 1, hasCell = true, x = 0, y = 4, locked = true };
        string bow = MatchHash.BuildText(m);
        Check(bow.EndsWith(",[[[7,0,2]],[[8,0,320,[1,[0,4],0]]],[[1,[0,4],1],[[0,4]]]]]"), $"MatchHash: 弓の照準を足す（{bow.Substring(Math.Max(0, bow.Length - 80))}）");
        string h = MatchHash.Compute(m);
        m.pieces[2].hp = 1;
        Check(MatchHash.Compute(m) != h, "MatchHash: 防壁の HP が違えば違うハッシュ");
        m.pieces[2].hp = 2;
        m.pieces[1].aim.locked = true;
        Check(MatchHash.Compute(m) != h, "MatchHash: 軍銃兵の照準が固まれば違うハッシュ");
        m.pieces[1].aim.locked = false;
        MatchSnapshot back = MatchSnapshot.FromJson(m.ToJson());
        Check(back != null && MatchHash.Compute(back) == h && back.pieces[1].gunner && back.pieces[1].aim.target == 1 && back.pieces[2].hp == 2
              && back.boss.ai.aim.locked && back.raid.nextPieceId == 4 && back.pieces[0].serial == 1, "MatchSnapshot: 大将軍の項目が JSON で往復し、ハッシュも同じ");
        MatchSnapshot old = MatchSnapshot.FromJson("{\"mode\":\"bossRaid\",\"raid\":{\"active\":true,\"kingHits\":1}}");
        Check(old != null && old.raid.kingHits == 1 && old.raid.nextPieceId == 1, "MatchSnapshot: 駒の番号のない JSON は 1 から");
    }

    static BossDef DAI => BossDefs.Daishogun;
    const int Never = 1000000000;

    // 大将軍の討伐任務の局面（王将だけの編成が既定）
    static BoardSnapshot RaidD(List<RaidPlacement> formation = null, uint seed = 1)
    {
        BoardSnapshot s = Kassen(seed, new[] { "Shield", "Sword", "Meteor" }, new string[0]);
        BoardSimulator.SetupRaid(s, DAI, new RaidFormation { pieces = formation ?? KingOnly() });
        return s;
    }

    static IDisposable FixedOn(BossDef def, string id)
    {
        Func<BossState, int, string> keep = def.ChooseAttack;
        def.ChooseAttack = (b, now) => id;
        return new Restore(() => def.ChooseAttack = keep);
    }

    // 兵を止める（歩兵は進まない・銃兵は撃たない）
    static void FreezeSoldiers(BoardSnapshot s)
    {
        foreach (SnapshotPiece p in s.pieces)
        {
            if (p.minion) p.minionNext = Never;
            if (p.gunner) p.gunNext = Never;
        }
    }

    // ボスの席の駒を取り除く（種類を指定。なければ全部）
    static void ClearEnemies(BoardSnapshot s, params PieceType[] types)
    {
        foreach (SnapshotPiece p in s.pieces.ToList())
        {
            if (p.Owner == P2 && (types.Length == 0 || types.Contains(p.type))) s.Remove(p);
        }
    }

    static SnapshotPiece KingOf(BoardSnapshot s) => s.pieces.First(p => p.type == PieceType.King && p.Owner == P1);
    static void MoveKingP(BoardSnapshot s, int px, int py) => s.MovePiece(KingOf(s), 8 - py, px);
    // ボスの席の駒を置く（通知なし）。stop なら動かないよう止める
    static SnapshotPiece EnemyP(BoardSnapshot s, PieceType t, int px, int py, bool stop = true)
    {
        SnapshotPiece q = BossEngine<SnapshotPiece>.PlaceMinion(Host(s), t, 8 - py, px);
        if (stop)
        {
            if (q.minion) q.minionNext = Never;
            if (q.gunner) q.gunNext = Never;
        }
        return q;
    }
    static List<SnapshotPiece> EnemiesOf(BoardSnapshot s, PieceType t) => s.pieces.Where(p => p.type == t).ToList();
    static string EnemyKey(BoardSnapshot s, PieceType t) => PKey(EnemiesOf(s, t).Select(p => new MatchSquare(p.x, p.y)));
    static MatchAction PSnipe(int fx, int fy, int tx, int ty) => Own(MatchAction.Snipe(new Vector2Int(8 - fy, fx), new Vector2Int(8 - ty, tx)), P1);
    static MatchAction PSpecial(int px, int py) => Own(Spc(8 - py, px), P1);
    static MatchAction PSpecial(int px, int py, int tx, int ty) => Own(Spc(8 - py, px, 8 - ty, tx), P1);
    static bool PHas(IEnumerable<MatchSquare> list, int px, int py) => list.Contains(PS(px, py));
    static bool PCanMove(BoardSnapshot s, int fx, int fy, int tx, int ty) => BoardSimulator.CanMoveTo(s, PAt(s, fx, fy), 8 - ty, tx);

    // (px, py) を中心に爆発させる：後手の火薬を置いて、先手の狙撃手（9九に置く）で撃つ
    static void ExplodeP(BoardSnapshot s, int px, int py)
    {
        if (PAt(s, px, py) == null) Put(s, PieceType.Gunpowder, P2, 8 - py, px);
        SnapshotPiece sn = s.pieces.FirstOrDefault(p => p.type == PieceType.Sniper && p.Owner == P1) ?? PPut(s, PieceType.Sniper, 0, 8);
        sn.cooldownUntil = 0;
        bool ok = BoardSimulator.Apply(s, Own(MatchAction.Snipe(new Vector2Int(sn.x, sn.y), new Vector2Int(8 - py, px)), P1), out string reason);
        Check(ok, $"爆発の準備: 火薬を狙撃できる（{reason}）");
        sn.cooldownUntil = 0;
    }

    // ------------------------------------------------------------
    //  ボスの共通の仕組み（boss-mechanics.test.js）：定義の検査・照準
    // ------------------------------------------------------------
    static void TestBossMechanics()
    {
        BossDef With(params BossEnemy[] enemies) => new BossDef
        {
            Id = "t", Name = "試験", W = 3, H = 3, StartX = 6, StartY = 3, Hp = 5, CooldownSec = 2f, CostLimit = 50, ForgeSlots = 2, Enemies = enemies,
            Attacks = new[] { new BossAttackEntry { Id = "bow", Weight = 1 } },
        };
        Check(BossDefs.Validate(With(new BossEnemy(PieceType.Kooni, 8, 0))).Count == 0, "ボスの定義: ボス側の初期配置の駒があっても検査を通る");
        Check(string.Join("", BossDefs.Validate(With(new BossEnemy(PieceType.Kooni, 7, 4)))).Contains("ボスに重なって"), "ボスの定義: ボス側の駒がボスに重なると弾く");
        Check(string.Join("", BossDefs.Validate(With(new BossEnemy(PieceType.Kooni, 8, 9)))).Contains("盤に収まりません"), "ボスの定義: ボス側の駒が盤の外だと弾く");
        Check(string.Join("", BossDefs.Validate(With(new BossEnemy(PieceType.Pawn, 8, 0)))).Contains("ボス側の駒ではありません"), "ボスの定義: ボス側の駒でないと弾く");
        Check(string.Join("", BossDefs.Validate(With(new BossEnemy(PieceType.Kooni, 8, 0), new BossEnemy(PieceType.Kooni, 8, 0)))).Contains("重なっています"), "ボスの定義: 同じマスに 2 つは弾く");

        // 照準（createAim / followAim）：駒を追い、消えたら残りから選び直す。固まったら動かない
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            IBossHost<SnapshotPiece> h = Host(s);
            SnapshotPiece king = KingOf(s);
            Check(king.serial == 1 && s.raid.nextPieceId == 2, $"駒の番号: 作った順に 1 から（王将 {king.serial}・次 {s.raid.nextPieceId}）");
            AimData aim = BossEngine<SnapshotPiece>.CreateAim(h);
            Check(aim.active && aim.target == king.serial && aim.Cell == PS(4, 8) && !aim.locked, "照準: 王将を選んで、そのマスを狙う");
            MoveKingP(s, 5, 8);
            Check(BossEngine<SnapshotPiece>.FollowAim(h, ref aim) && aim.Cell == PS(5, 8), "照準: 駒を追う");
            Check(!BossEngine<SnapshotPiece>.FollowAim(h, ref aim), "照準: 動いていなければ変わらない");
            s.Remove(king);
            SnapshotPiece fu = PPut(s, PieceType.Pawn, 2, 6);
            Check(BossEngine<SnapshotPiece>.FollowAim(h, ref aim) && aim.target == fu.serial && aim.Cell == PS(2, 6), "照準: 追っていた駒が消えたら選び直す");
            aim.locked = true;
            s.MovePiece(fu, 8 - 6, 3);
            Check(!BossEngine<SnapshotPiece>.FollowAim(h, ref aim) && aim.Cell == PS(2, 6), "照準: 固まったら動かない");
            s.Remove(fu);
            AimData none = BossEngine<SnapshotPiece>.CreateAim(h);
            Check(none.active && none.target == 0 && !none.hasCell, "照準: 選べる駒がなければマスなし");
        }
        // 候補は駒の番号の順（打った駒はあとの番号）
        {
            BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 0, 6), FP(PieceType.Pawn, 8, 6) });
            SnapshotPiece late = PPut(s, PieceType.GoldGeneral, 4, 4);
            List<SnapshotPiece> c = BossEngine<SnapshotPiece>.AimCandidates(Host(s));
            Check(c.Select(p => p.serial).SequenceEqual(new[] { 1, 2, 3, 4 }) && c[3] == late && c[0].type == PieceType.King, "照準: 候補は駒の番号の順");
        }
    }

    // ------------------------------------------------------------
    //  防壁（walls.test.js「防壁」）
    // ------------------------------------------------------------
    static void TestWalls()
    {
        // 置くと HP 3。ボスの席の駒（後手）で動かない
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece w = EnemyP(s, PieceType.Bouheki, 0, 1);
            Check(w.type == PieceType.Bouheki && w.Owner == P2 && w.hp == WallRules.MaxHp && WallRules.MaxHp == 3 && w.IsWallPiece && !w.minion && !w.gunner, "防壁: 置くと HP 3・後手の駒・動きの状態を持たない");
            Check(PieceRules.IsMinion(PieceType.Bouheki) && !PieceRules.GoesToHand(PieceType.Bouheki) && !PieceRules.CanPromote(PieceType.Bouheki), "防壁: ボスの席の駒（持ち駒にならない・成らない）");
            BoardSimulator.Advance(s, 10 * Sec);
            Check(PAt(s, 0, 1) == w, "防壁: 動かない");
        }
        // 駒が入ろうとすると攻撃して跳ね返る（HP -1）。移動回数と +2 エナジーは数える。打てない
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 0, 2));
            FreezeAi(s);
            SnapshotPiece w = EnemyP(s, PieceType.Bouheki, 0, 1), fu = PAt(s, 0, 2);
            Check(BoardSimulator.Apply(s, PMove(s, 0, 2, 0, 1), out string why), $"防壁: 防壁のマスへ動かせる（{why}）");
            Check(w.hp == 2 && PAt(s, 0, 2) == fu && PAt(s, 0, 1) == w, "防壁: 攻撃して跳ね返る（HP -1）");
            Check(fu.moveCount == 1 && s.players[0].energy == 2, $"防壁: 移動回数と +2 エナジーは数える（移動 {fu.moveCount}・エナジー {s.players[0].energy}）");
            Check(fu.promoted, "防壁: 攻撃したマスが敵陣なら成る");
            Check(!BoardSimulator.CanDropTo(s, P1, PieceType.GoldGeneral, 8 - 1, 0), "防壁: 防壁のマスには打てない");
        }
        // 走り駒は防壁で止まる（防壁のマスは攻撃として選べる）
        {
            BoardSnapshot s = RaidWith((PieceType.Rook, 0, 6));
            FreezeAi(s);
            EnemyP(s, PieceType.Bouheki, 0, 2);
            Check(PCanMove(s, 0, 6, 0, 2) && PCanMove(s, 0, 6, 0, 3) && !PCanMove(s, 0, 6, 0, 1), "防壁: 走り駒は防壁で止まる（防壁のマスは選べる）");
        }
        // 3 回攻撃すると崩れて盤から消える（持ち駒にもエナジーにもならない）
        {
            BoardSnapshot s = RaidWith((PieceType.GoldGeneral, 0, 2));
            FreezeAi(s);
            SnapshotPiece w = EnemyP(s, PieceType.Bouheki, 0, 1);
            for (int i = 0; i < 3; i++)
            {
                PAt(s, 0, 2).cooldownUntil = 0;
                BoardSimulator.Apply(s, PMove(s, 0, 2, 0, 1));
            }
            Check(w.hp == 0 && PAt(s, 0, 1) == null && !s.Contains(w), "防壁: 3 回で崩れる");
            Check(s.players[0].hand.All(n => n == 0) && s.players[0].energy == 6 && s.players[1].energy == 0, "防壁: 持ち駒にもエナジーにもならない（移動の +2 × 3 だけ）");
            Check(PAt(s, 0, 2) != null && PAt(s, 0, 2).type == PieceType.GoldGeneral, "防壁: 崩した回も跳ね返る");
        }
        // 狙撃・光線・金剛衝・爆発も 1 回につき 1 減る
        {
            BoardSnapshot s = RaidWith((PieceType.Sniper, 8, 8), (PieceType.Rook, 0, 5), (PieceType.GoldGeneral, 7, 2));
            FreezeAi(s);
            PAt(s, 0, 5).moveCount = 9;
            PAt(s, 7, 2).moveCount = 9;
            SnapshotPiece a = EnemyP(s, PieceType.Bouheki, 0, 1), b = EnemyP(s, PieceType.Bouheki, 8, 1), c = EnemyP(s, PieceType.Bouheki, 6, 2);
            Check(PHas(BoardSimulator.SnipeTargets(s, PAt(s, 8, 8)), 8, 1), "防壁: 狙撃の対象になる");
            Check(BoardSimulator.Apply(s, PSnipe(8, 8, 8, 1)) && b.hp == 2 && s.Contains(b), "防壁: 狙撃で HP -1");
            Check(BoardSimulator.Apply(s, PSpecial(0, 5), out string r1) && a.hp == 2, $"防壁: 光線（飛の必殺技）で HP -1（{r1}・HP {a.hp}）");
            Check(BoardSimulator.Apply(s, PSpecial(7, 2), out string r2) && b.hp == 1 && c.hp == 2 && PAt(s, 8, 1) == b && PAt(s, 6, 2) == c, $"防壁: 金剛衝は押せずに HP -1（{r2}・HP {b.hp}/{c.hp}）");
            ExplodeP(s, 7, 1); // 爆発：3×3 の防壁は 1 回だけ
            Check(b.hp == 0 && c.hp == 1 && PAt(s, 8, 1) == null, $"防壁: 爆発で HP -1（{b.hp}/{c.hp}）");
        }
        // 香の貫通突進は防壁を貫けない／巨人は先頭の 1 列の防壁を攻撃して跳ね返る
        {
            BoardSnapshot s = RaidWith((PieceType.Lance, 0, 6));
            FreezeAi(s);
            PAt(s, 0, 6).moveCount = 9;
            SnapshotPiece w = EnemyP(s, PieceType.Bouheki, 0, 3), back = EnemyP(s, PieceType.Kooni, 0, 1);
            Check(BoardSimulator.Apply(s, PSpecial(0, 6), out string r) && w.hp == 2 && s.Contains(back) && PAt(s, 0, 4) != null && PAt(s, 0, 4).type == PieceType.Lance, $"防壁: 貫通突進は防壁の手前で止まる（{r}）");

            BoardSnapshot g = Raid();
            FreezeAi(g);
            SnapshotPiece giant = Put(g, PieceType.Giant, P1, 8 - 5, 1);
            SnapshotPiece w1 = EnemyP(g, PieceType.Bouheki, 0, 2), w2 = EnemyP(g, PieceType.Bouheki, 2, 2);
            List<MatchSquare> moves = BoardSimulator.GiantMoves(g, giant);
            Check(moves.Count == 2 && moves.Contains(PS(1, 2)), $"防壁: 巨人は防壁の列まで動ける（{moves.Count}）");
            Check(BoardSimulator.Apply(g, Own(Mv(giant.x, giant.y, 8 - 2, 1), P1), out string gr) && w1.hp == 2 && w2.hp == 2 && giant.x == 8 - 5 && giant.moveCount == 1,
                $"防壁: 巨人は先頭の 1 列の防壁をどれも攻撃して跳ね返る（{gr}）");
        }
    }

    // ------------------------------------------------------------
    //  防壁の後ろの守り（walls.test.js「防壁の後ろの守り」）
    // ------------------------------------------------------------
    static void TestWallShield()
    {
        // 1一（x8, y0）・2一（x7, y0）の兵（小赤鬼で代わりにする）の手前に、1二（x8, y1）の防壁
        (BoardSnapshot s, SnapshotPiece back1, SnapshotPiece back2, SnapshotPiece wall) Guarded(params (PieceType t, int px, int py)[] extra)
        {
            BoardSnapshot s = RaidWith(extra);
            FreezeAi(s);
            return (s, EnemyP(s, PieceType.Kooni, 8, 0), EnemyP(s, PieceType.Kooni, 7, 0), EnemyP(s, PieceType.Bouheki, 8, 1));
        }

        {
            var (s, _, _, _) = Guarded((PieceType.Pawn, 6, 0));
            Check(s.IsShieldedByWall(8 - 0, 8) && !s.IsShieldedByWall(8 - 0, 7) && !s.IsShieldedByWall(8 - 1, 8) && !s.IsShieldedByWall(8 - 0, 6),
                "防壁の守り: 同じ筋で防壁より奥のボスの席の駒だけ（防壁そのもの・プレイヤーの駒・防壁のない筋は守られない）");
            Check(BossEngine<SnapshotPiece>.IsShieldedByWall(Host(s), 8, 8) && !BossEngine<SnapshotPiece>.IsShieldedByWall(Host(s), 8, 7), "防壁の守り: BossEngine の判定も同じ");
        }
        // 桂の移動・天馬跳躍で防壁を飛び越して着地できない。壊れたあとはできる
        {
            var (s, back1, _, wall) = Guarded((PieceType.Knight, 7, 2));
            SnapshotPiece ke = PAt(s, 7, 2);
            ke.moveCount = 9;
            Check(!PCanMove(s, 7, 2, 8, 0) && PCanMove(s, 7, 2, 6, 0), "防壁の守り: 桂は守られた駒のマスへ跳べない");
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
            Check(!legal.Any(a => (a.to == PS(8, 0) && a.type == MatchActionType.Move) || (a.type == MatchActionType.Special && a.TargetCount == 1 && a.Target == PS(8, 0))),
                "防壁の守り: 合法手に守られた駒のマスへの移動・跳躍がない");
            wall.hp = 1;
            ExplodeP(s, 8, 2); // 防壁を壊す（爆発に桂も巻き込まれる）
            Check(PAt(s, 8, 1) == null, "防壁の守り: 防壁が壊れた");
            SnapshotPiece ke2 = PPut(s, PieceType.Knight, 7, 2);
            Check(PCanMove(s, 7, 2, 8, 0), "防壁の守り: 壊れたあとは跳べる");
            BoardSimulator.Apply(s, PMove(s, 7, 2, 8, 0));
            Check(PAt(s, 8, 0) == ke2 && !s.Contains(back1), "防壁の守り: 壊れたあとは取れる");
        }
        // 狙撃の対象にならない（防壁は撃てる）。壊れれば撃てる
        {
            var (s, _, _, wall) = Guarded((PieceType.Sniper, 0, 8));
            SnapshotPiece sn = PAt(s, 0, 8);
            List<MatchSquare> t = BoardSimulator.SnipeTargets(s, sn);
            Check(!PHas(t, 8, 0) && PHas(t, 7, 0) && PHas(t, 8, 1), "防壁の守り: 守られた駒は狙撃できない（防壁・守られていない駒は撃てる）");
            Check(!BoardSimulator.Apply(s, PSnipe(0, 8, 8, 0)), "防壁の守り: 守られた駒への狙撃は断る");
            wall.hp = 1;
            BoardSimulator.Apply(s, PSnipe(0, 8, 8, 1));
            Check(PHas(BoardSimulator.SnipeTargets(s, sn), 8, 0), "防壁の守り: 防壁が壊れれば撃てる");
        }
        // 爆発・隕石・破壊砲の範囲に入っても消えない（防壁は 1 減る）。壊れたあとは消える
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "Meteor", "Cannon" });
            FreezeAi(s);
            SnapshotPiece back1 = EnemyP(s, PieceType.Kooni, 8, 0), back2 = EnemyP(s, PieceType.Kooni, 7, 0), wall = EnemyP(s, PieceType.Bouheki, 8, 1);
            ExplodeP(s, 7, 1);
            Check(s.Contains(back1) && !s.Contains(back2) && wall.hp == 2, "防壁の守り: 爆発で守られた駒は消えない（防壁は 1 減る・守られていない駒は消える）");
            s.players[0].energy = 99;
            Check(BoardSimulator.Apply(s, PMagic("Meteor", 7, 1), out string mr) && s.Contains(back1) && wall.hp == 1, $"防壁の守り: 隕石でも消えない（{mr}）");
            Check(BoardSimulator.Apply(s, PMagic("Cannon", 7, 4), out string cr), $"防壁の守り: 破壊砲を撃てる（{cr}）");
            Check(PAt(s, 8, 1) == null && s.Contains(back1), "防壁の守り: 破壊砲で防壁は崩れ、守られていた駒は残る");
            Check(!PCell(s, 8, 0).IsBroken && PCell(s, 7, 0).IsBroken, "防壁の守り: 守られていた駒のマスは壊さない");
            ExplodeP(s, 7, 1);
            Check(!s.Contains(back1), "防壁の守り: 防壁が崩れたあとは消える");
        }
        // 光線は防壁に守られた駒に当たらない（そこで止まる）。防壁が壊れれば当たる
        {
            var (s, back1, _, wall) = Guarded((PieceType.Bishop, 6, 2));
            SnapshotPiece ka = PAt(s, 6, 2);
            ka.moveCount = 9;
            EnemyP(s, PieceType.Kooni, 3, 5); // 光線の当たる駒がないと必殺技を使えないので、別の向きに 1 体
            Check(BoardSimulator.Apply(s, PSpecial(6, 2), out string r) && s.Contains(back1), $"防壁の守り: 光線は守られた駒に当たらない（{r}）");
            wall.hp = 1;
            ExplodeP(s, 8, 2);
            ka.moveCount = 9;
            ka.cooldownUntil = 0;
            Check(BoardSimulator.Apply(s, PSpecial(6, 2), out string r2) && !s.Contains(back1), $"防壁の守り: 防壁が壊れれば光線が当たる（{r2}）");
        }
        // 1二が壊れれば、2二が残っていても 1一 を攻撃できる
        {
            var (s, _, _, wall) = Guarded((PieceType.Sniper, 0, 8));
            EnemyP(s, PieceType.Bouheki, 7, 1);
            Check(!PHas(BoardSimulator.SnipeTargets(s, PAt(s, 0, 8)), 8, 0), "防壁の守り: 1二が残っている間は 1一を撃てない");
            wall.hp = 1;
            BoardSimulator.Apply(s, PSnipe(0, 8, 8, 1));
            Check(PAt(s, 7, 1) != null && PAt(s, 7, 1).IsWallPiece && !s.IsShieldedByWall(8, 8) && s.IsShieldedByWall(8, 7) && PHas(BoardSimulator.SnipeTargets(s, PAt(s, 0, 8)), 8, 0),
                "防壁の守り: 1二が壊れれば 1一を撃てる（2二の後ろの 2一は守られたまま）");
        }
        // 金剛衝は守られた駒に効かない・剣は防壁を斬る（HP -1）
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "Sword" });
            FreezeAi(s);
            SnapshotPiece wall = EnemyP(s, PieceType.Bouheki, 4, 5), back = EnemyP(s, PieceType.Kooni, 4, 4);
            SnapshotPiece gold = PPut(s, PieceType.GoldGeneral, 5, 4);
            gold.moveCount = 9;
            Check(BoardSimulator.Apply(s, PSpecial(5, 4), out string r) && PAt(s, 4, 4) == back && wall.hp == 2, $"防壁の守り: 金剛衝は守られた駒を押せない（防壁は 1 減る。{r}）");
            SnapshotPiece gin = PPut(s, PieceType.SilverGeneral, 4, 6);
            s.players[0].energy = 99;
            Check(BoardSimulator.Apply(s, PMagic("Sword", 4, 6), out string sr) && wall.hp == 1 && s.Contains(back), $"防壁の守り: 剣は防壁を斬る（HP -1。{sr}）");
        }
        // 防壁の HP は JSON の往復とハッシュに入る
        {
            var (s, _, _, wall) = Guarded();
            string h = PrototypeHashText(s);
            wall.hp = 2;
            Check(PrototypeHashText(s) != h && PrototypeHashText(BoardSnapshot.FromJson(s.ToJson())) == PrototypeHashText(s), "防壁: HP は JSON の往復とハッシュに入る");
        }
    }

    // ------------------------------------------------------------
    //  軍歩兵（soldiers.test.js）
    // ------------------------------------------------------------
    static void TestSoldiers()
    {
        // 3 秒ごとに前（プレイヤー側）へ 1 マス進む。次のマスは 1 秒前に決まって表示される
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece m = EnemyP(s, PieceType.GunFu, 0, 3, false);
            Check(PieceRules.IsMinion(m.type) && m.minion && m.minionNext == 3 * Sec && !m.minionPlanned, "軍歩兵: 最初の 1 歩は 3 秒後");
            List<BossEvent> ev = AdvanceEv(s, 2 * Sec);
            Check(m.minionPlanned && m.minionHasTo && new MatchSquare(m.minionToX, m.minionToY) == PS(0, 4) && ev.Any(e => e.Type == BossEventType.MinionPlanned && e.To == PS(0, 4)), "軍歩兵: 1 秒前に次のマス（前）を決める");
            ev = AdvanceEv(s, Sec);
            Check(PAt(s, 0, 4) == m && ev.Any(e => e.Type == BossEventType.MinionStep && e.To == PS(0, 4)), "軍歩兵: 3 秒で前へ 1 マス");
            BoardSimulator.Advance(s, 3 * Sec);
            Check(PAt(s, 0, 5) == m, "軍歩兵: 6 秒でもう 1 マス");
        }
        // 前のマスの駒を攻撃する：消えたら進み、残ったら（盾）進まない
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 2, 4));
            FreezeAi(s);
            SnapshotPiece m = EnemyP(s, PieceType.GunFu, 2, 3, false), fu = PAt(s, 2, 4);
            fu.shield = true;
            BoardSimulator.Advance(s, 3 * Sec);
            Check(!fu.shield && PAt(s, 2, 4) == fu && PAt(s, 2, 3) == m, "軍歩兵: 盾の駒は盾が割れて残り、進まない");
            List<BossEvent> ev = AdvanceEv(s, 3 * Sec);
            Check(!s.Contains(fu) && PAt(s, 2, 4) == m, "軍歩兵: 駒が消えたら進む");
            Check(ev.Any(e => e.Type == BossEventType.HitPiece && e.Attack == "march" && e.Result == BossEvent.Smashed), "軍歩兵: 攻撃の Id は march（撃破）");
        }
        // 王将を攻撃すると被弾。防壁・ボスの席の駒・破壊マスの前では進まない
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            MoveKingP(s, 6, 4);
            SnapshotPiece a = EnemyP(s, PieceType.GunFu, 6, 3, false), b = EnemyP(s, PieceType.GunFu, 0, 3, false), c = EnemyP(s, PieceType.GunFu, 1, 3, false), d = EnemyP(s, PieceType.GunFu, 7, 3, false);
            EnemyP(s, PieceType.Bouheki, 0, 4);
            EnemyP(s, PieceType.GunJu, 1, 4);
            PCell(s, 7, 4).state = CellState.Broken;
            BoardSimulator.Advance(s, 3 * Sec);
            Check(s.raid.kingHits == 1, "軍歩兵: 王将を攻撃すると被弾");
            Check(PAt(s, 6, 3) == a && PAt(s, 0, 3) == b && PAt(s, 1, 3) == c && PAt(s, 7, 3) == d, "軍歩兵: 王将・防壁・兵・破壊マスの前では進まない");
        }
        // いちばん手前の段に着いたら止まる（成らない）
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            MoveKingP(s, 8, 8);
            SnapshotPiece m = EnemyP(s, PieceType.GunFu, 0, 7, false);
            BoardSimulator.Advance(s, 3 * Sec);
            Check(PAt(s, 0, 8) == m && !m.promoted, "軍歩兵: 九段まで進む（成らない）");
            BoardSimulator.Advance(s, 6 * Sec);
            Check(PAt(s, 0, 8) == m, "軍歩兵: 九段で止まる");
        }
        // 取れば倒せる（持ち駒・エナジーにならない）。ボスの攻撃は当たらない
        {
            BoardSnapshot s = RaidWith((PieceType.GoldGeneral, 4, 6));
            FreezeAi(s);
            SnapshotPiece m = EnemyP(s, PieceType.GunFu, 4, 5);
            BossEngine<SnapshotPiece>.HitCells(Host(s), new List<MatchSquare> { PS(4, 5) }, "sweep");
            Check(s.Contains(m), "軍歩兵: ボスの攻撃は当たらない");
            BoardSimulator.Apply(s, PMove(s, 4, 6, 4, 5));
            Check(!s.Contains(m) && s.players[0].hand.All(n => n == 0) && s.players[0].energy == 2, "軍歩兵: 取れば倒せる（持ち駒・エナジーにならない。移動の +2 だけ）");
        }
        // 歩と別の種類なので、二歩にならない（軍歩兵のいる筋にも歩を打てる）
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            EnemyP(s, PieceType.GunFu, 3, 4);
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Pawn)] = 1;
            Check(BoardSimulator.CanDropTo(s, P1, PieceType.Pawn, 8 - 7, 3), "軍歩兵: いる筋にも歩を打てる（二歩にならない）");
        }
    }

    // ------------------------------------------------------------
    //  軍銃兵（soldiers.test.js）
    // ------------------------------------------------------------
    static void TestGunners()
    {
        // 置いたときにプレイヤーの駒を 1 枚選んで照準を合わせ、照準は駒を追う。5 秒目（撃つ 3 秒前）に固まり、8 秒目に撃つ
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece g = EnemyP(s, PieceType.GunJu, 8, 0, false);
            Check(g.gunner && !g.minion && g.gunNext == 8 * Sec && g.aim.active && g.aim.target == KingOf(s).serial && g.aim.Cell == PS(4, 8) && !g.aim.locked, "軍銃兵: 置いたときに照準を合わせる（最初に撃つのは 8 秒後）");
            BoardSimulator.Advance(s, Sec);
            MoveKingP(s, 3, 7);
            BoardSimulator.Advance(s, 1);
            Check(g.aim.Cell == PS(3, 7), "軍銃兵: 照準は駒を追う");
            BoardSimulator.Advance(s, 5 * Sec - s.clockTime);
            Check(g.aim.locked, "軍銃兵: 撃つ 3 秒前に固まる");
            MoveKingP(s, 2, 7); // 固まったあとに逃げれば当たらない
            BoardSimulator.Advance(s, 1);
            Check(g.aim.Cell == PS(3, 7), "軍銃兵: 固まったら追わない");
            List<BossEvent> ev = AdvanceEv(s, 8 * Sec - s.clockTime);
            BossEvent shot = ev.FirstOrDefault(e => e.Type == BossEventType.GunnerShot);
            Check(shot != null && shot.Piece == g && shot.At == PS(3, 7) && shot.Hits == 0 && s.raid.kingHits == 0, "軍銃兵: 8 秒目に照準のマスを撃つ（逃げたので当たらない）");
            Check(g.gunNext == 16 * Sec && !g.aim.locked && g.aim.Cell == PS(2, 7), "軍銃兵: 次の周期（新しい照準）");
        }
        // 照準のマスにいた駒に当たる（防壁越しにも撃てる）
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece g = EnemyP(s, PieceType.GunJu, 8, 0, false);
            EnemyP(s, PieceType.Bouheki, 8, 1);
            List<BossEvent> ev = AdvanceEv(s, 8 * Sec);
            Check(s.raid.kingHits == 1 && g.gunNext == 16 * Sec && ev.Any(e => e.Type == BossEventType.HitPiece && e.Attack == "gunshot" && e.Result == BossEvent.King), "軍銃兵: 照準のマスの駒に当たる（防壁越しにも）");
        }
        // 追っていた駒が消えたら、残っている駒から選び直す
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece fu = PPut(s, PieceType.Pawn, 0, 6);
            SnapshotPiece g = EnemyP(s, PieceType.GunJu, 8, 0, false);
            g.aim = new AimData { active = true, target = fu.serial, hasCell = true, x = fu.x, y = fu.y };
            s.Remove(fu);
            BoardSimulator.Advance(s, 1);
            Check(g.aim.target == KingOf(s).serial && g.aim.Cell == PS(4, 8), "軍銃兵: 追っていた駒が消えたら選び直す");
        }
        // 最初に撃つまでの時間を GunnerOffsetSec でずらせる。1 回当てれば倒れる
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            Check(MinionRules.GunnerOffsetSec == 0f && MinionRules.GunnerIntervalSec == 8f && MinionRules.GunnerLockSecBefore == 3f, "軍銃兵: 8 秒ごと・3 秒前に固まる・ずれ 0");
            MinionRules.GunnerOffsetSec = 2f;
            SnapshotPiece g;
            try { g = EnemyP(s, PieceType.GunJu, 0, 0, false); } finally { MinionRules.GunnerOffsetSec = 0f; }
            Check(g.gunNext == 10 * Sec, "軍銃兵: 最初に撃つ時刻をずらせる");
            BoardSimulator.Advance(s, 8 * Sec);
            Check(s.raid.kingHits == 0, "軍銃兵: ずらした分はまだ撃たない");
            BoardSimulator.Advance(s, 2 * Sec);
            Check(s.raid.kingHits == 1, "軍銃兵: 10 秒目に撃つ");
            SnapshotPiece sn = PPut(s, PieceType.Sniper, 8, 8);
            Check(PHas(BoardSimulator.SnipeTargets(s, sn), 0, 0), "軍銃兵: 狙撃できる");
            BoardSimulator.Apply(s, PSnipe(8, 8, 0, 0));
            Check(!s.Contains(g) && s.players[0].hand.All(n => n == 0), "軍銃兵: 1 回当てれば倒れる（持ち駒にならない）");
        }
        // 照準・次に撃つ時刻は JSON の往復とハッシュに入る
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece g = EnemyP(s, PieceType.GunJu, 8, 0, false);
            BoardSimulator.Advance(s, 2 * Sec);
            BoardSnapshot t = BoardSnapshot.FromJson(s.ToJson());
            Check(PrototypeHashText(t) == PrototypeHashText(s) && t.Diff(s, true).Count == 0, "軍銃兵: JSON の往復で同じ");
            g.aim.locked = true;
            Check(PrototypeHashText(t) != PrototypeHashText(s) && t.Diff(s).Count > 0, "軍銃兵: 照準が固まったかはハッシュ・比較に入る");
            g.aim.locked = false;
            BoardSimulator.Advance(s, 10 * Sec);
            BoardSimulator.Advance(t, 10 * Sec);
            Check(PrototypeHashText(t) == PrototypeHashText(s), "軍銃兵: 10 秒進めても同じ");
        }
    }

    // ------------------------------------------------------------
    //  大将軍の定義と初期配置（daishogun.test.js）
    // ------------------------------------------------------------
    static void TestDaishogunStart()
    {
        Check(BossDefs.Validate(DAI).Count == 0, "大将軍: 定義の検査を通る");
        Check(DAI.Name == "大将軍" && DAI.RulesetId == "kassen" && DAI.Hp == 30 && DAI.W == 3 && DAI.H == 3 && DAI.StartX == 6 && DAI.StartY == 3 && DAI.CooldownSec == 10f
              && DAI.HasFixedFacing && DAI.FixedFacingX == -1 && DAI.FixedFacingY == 0 && DAI.KingMaxHits == 3 && DAI.CostLimit == 50 && DAI.ForgeSlots == 2 && DAI.Implemented,
            "大将軍: 合戦将棋型・HP 30・3×3・行動ペース 10 秒・向きはプレイヤー側で固定・コスト上限 50・錬成駒 2 種");
        Check(DAI.ClearRequireNoMinions.SequenceEqual(new[] { PieceType.GunFu, PieceType.GunJu }) && DAI.MinionsOutliveBoss && !AKAONI.MinionsOutliveBoss, "大将軍: 勝ちの条件に軍歩兵・軍銃兵の全滅");
        Check(string.Join(" ", DAI.Attacks.Select(a => $"{a.Id}:{a.Weight}:{(a.NoRepeat ? 1 : 0)}")) == "bow:3:0 greatsword:3:0 guard:2:1 deploy:2:1", "大将軍: 攻撃 4 種（弓 3・大剣斬り 3・守備 2・出陣命令 2。守備・出陣命令は続けない）");
        Check(BossAttacks.Bow.TelegraphSec == 5f && BossAttacks.Bow.LockSecBefore == 2f && BossAttacks.Bow.Retarget && BossAttacks.Greatsword.TelegraphSec == 3f
              && BossAttacks.Guard.TelegraphSec == 0f && BossAttacks.Guard.DurationSec == 5f && BossAttacks.Guard.Invulnerable && BossAttacks.Guard.RestSec == 0f
              && BossAttacks.Deploy.TelegraphSec == 2f && BossAttacks.Deploy.Harmless, "大将軍の攻撃: 予告の秒数（弓 5・大剣斬り 3・守備なし・出陣命令 2）");

        BoardSnapshot s = RaidD();
        Check(s.boss.id == "daishogun" && s.boss.y == 3 && 9 - s.boss.x - s.boss.h == 0 && s.boss.hp == 30 && FacingP(s.boss) == "0,1", "大将軍: 4一〜6三・HP 30・下向き");
        Check(EnemyKey(s, PieceType.GunJu) == PKey((6, 0), (7, 0), (8, 0), (0, 0), (1, 0), (2, 0)), "大将軍の陣: 軍銃兵は 1一〜3一・7一〜9一");
        Check(EnemyKey(s, PieceType.Bouheki) == PKey((6, 1), (7, 1), (8, 1), (0, 1), (1, 1), (2, 1)), "大将軍の陣: 防壁は 1二〜3二・7二〜9二");
        Check(EnemyKey(s, PieceType.GunFu) == PKey(Enumerable.Range(0, 9).Select(x => (x, 3)).ToArray()), "大将軍の陣: 軍歩兵は 1四〜9四");
        Check(EnemiesOf(s, PieceType.Bouheki).All(p => p.hp == 3) && EnemiesOf(s, PieceType.GunJu).All(p => s.IsShieldedByWall(p.x, p.y)), "大将軍の陣: 防壁は HP 3・軍銃兵はみな防壁に守られている");
        Check(EnemiesOf(s, PieceType.GunJu).All(p => p.gunner && p.gunNext == 8 * Sec && p.aim.active) && EnemiesOf(s, PieceType.GunFu).All(p => p.minion && p.minionNext == 3 * Sec), "大将軍の陣: 軍銃兵は 8 秒後に撃つ・軍歩兵は 3 秒後に進む");
        Check(s.pieces.Where(p => p.Owner == P2).Select(p => p.minionSerial).OrderBy(v => v).SequenceEqual(Enumerable.Range(0, 21)) && s.raid.minionSerial == 21 && s.raid.nextPieceId == 23,
            "大将軍の陣: 21 枚に置いた順の番号（駒の番号は王将の次から）");
        Check(BoardSimulator.LegalActions(s, P2).Count == 0, "大将軍: ボスの席は行動できない");
        Check(PrototypeHashText(RaidD(null, 5)) == PrototypeHashText(RaidD(null, 5)), "大将軍: 同じシードなら同じ初期状態（照準の抽選は対局の乱数）");
        BoardSnapshot js = BoardSnapshot.FromJson(s.ToJson());
        Check(js != null && js.Diff(s, true).Count == 0 && PrototypeHashText(js) == PrototypeHashText(s), "大将軍: 初期状態が JSON で往復する");
    }

    // ------------------------------------------------------------
    //  大将軍の攻撃
    // ------------------------------------------------------------
    static void TestDaishogunAttacks()
    {
        // 行動ペース 10 秒で攻撃を選ぶ。王将が横にいても向きは変えない
        using (FixedOn(DAI, "greatsword"))
        {
            BoardSnapshot s = RaidD();
            FreezeSoldiers(s);
            MoveKingP(s, 8, 4);
            BoardSimulator.Advance(s, 10 * Sec - 1);
            Check(s.boss.ai.phase == BossAiState.Cooldown, "大将軍: 10 秒たつまで待ち");
            BoardSimulator.Advance(s, 1);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.attack == "greatsword" && FacingP(s.boss) == "0,1", "大将軍: 10 秒で攻撃を選ぶ（王将が横にいても下向きのまま）");
        }
        // 大剣斬り：予告 3 秒、正面の x2〜6・y3〜4。ボスの席の駒には当たらない。盤の端は切る
        using (FixedOn(DAI, "greatsword"))
        {
            BoardSnapshot s = RaidD();
            FreezeSoldiers(s);
            SnapshotPiece a = PPut(s, PieceType.SilverGeneral, 2, 4), b = PPut(s, PieceType.SilverGeneral, 6, 4), c = PPut(s, PieceType.SilverGeneral, 7, 4);
            BoardSimulator.Advance(s, 10 * Sec);
            Check(PKey(s.boss.ai.cells) == PKey(new[] { 2, 3, 4, 5, 6 }.SelectMany(x => new[] { (x, 3), (x, 4) }).ToArray()) && s.boss.ai.until == 13 * Sec, "大剣斬り: 予告 3 秒・正面 5×2");
            BoardSimulator.Advance(s, 3 * Sec);
            Check(!s.Contains(a) && !s.Contains(b) && s.Contains(c) && EnemiesOf(s, PieceType.GunFu).Count == 9, "大剣斬り: 範囲の駒を撃破（ボスの席の駒には当たらない）");
            SetBossP(s, 0, 5);
            Check(Cells(s, BossAttacks.Greatsword).Count == 4, "大剣斬り: 左端・下端は盤の外を切る（4 マス）");
            SetBossP(s, 6, 6);
            Check(Cells(s, BossAttacks.Greatsword).Count == 0, "大剣斬り: 下端は範囲なし");
        }
        // 弓：予告 5 秒。照準は選んだ駒を追い、着弾の 2 秒前に固まる。そこで逃げれば当たらない
        using (FixedOn(DAI, "bow"))
        {
            BoardSnapshot s = RaidD();
            FreezeSoldiers(s);
            List<BossEvent> ev = AdvanceEv(s, 10 * Sec);
            BossAiState ai = s.boss.ai;
            Check(ai.phase == BossAiState.Telegraph && ai.attack == "bow" && ai.hasCells && ai.cells.SequenceEqual(new[] { PS(4, 8) }) && ai.until == 15 * Sec, "弓: 予告 5 秒・照準は王将のマス");
            Check(ai.aim.active && ai.aim.target == KingOf(s).serial && !ai.aim.locked && ev.Any(e => e.Type == BossEventType.Telegraph && e.Attack == "bow" && e.Sec == 5f), "弓: 照準の状態（追う駒・固まっていない）");
            MoveKingP(s, 5, 7);
            ev = AdvanceEv(s, 1);
            Check(ai.cells.SequenceEqual(new[] { PS(5, 7) }) && ev.Any(e => e.Type == BossEventType.TelegraphUpdate && !e.Locked && e.Cells[0] == PS(5, 7)), "弓: 照準が駒を追う");
            ev = AdvanceEv(s, 13 * Sec - s.clockTime);
            Check(ai.aim.locked && ev.Any(e => e.Type == BossEventType.TelegraphUpdate && e.Locked), "弓: 着弾の 2 秒前に固まる");
            MoveKingP(s, 6, 7);
            BoardSimulator.Advance(s, 1);
            Check(ai.cells.SequenceEqual(new[] { PS(5, 7) }), "弓: 固まったら追わない");
            BoardSimulator.Advance(s, 15 * Sec - s.clockTime);
            Check(s.boss.ai.phase == BossAiState.Cooldown && s.raid.kingHits == 0 && !s.boss.ai.aim.active && s.boss.ai.until == 25 * Sec, "弓: 逃げれば当たらない（終わったら待ち 10 秒・照準は消える）");
        }
        // 弓：逃げなければ当たる。追っていた駒が消えたら、残りから選び直す
        using (FixedOn(DAI, "bow"))
        {
            BoardSnapshot s = RaidD(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 0, 6) });
            FreezeSoldiers(s);
            SnapshotPiece fu = PAt(s, 0, 6);
            BoardSimulator.Advance(s, 10 * Sec);
            s.boss.ai.aim = new AimData { active = true, target = fu.serial, hasCell = true, x = fu.x, y = fu.y };
            s.boss.ai.cells = new List<MatchSquare> { PS(0, 6) };
            s.Remove(fu);
            BoardSimulator.Advance(s, 1);
            Check(s.boss.ai.aim.target == KingOf(s).serial && s.boss.ai.cells.SequenceEqual(new[] { PS(4, 8) }), "弓: 追っていた駒が消えたら選び直す");
            BoardSimulator.Advance(s, 15 * Sec - s.clockTime);
            Check(s.raid.kingHits == 1, "弓: 逃げなければ当たる");
        }
        // 守備：予告なしですぐ 5 秒。その間はボスへの攻撃が 0 ダメージ（駒は跳ね返る）。5 秒経つとまた当たる
        {
            BoardSnapshot s = RaidD();
            SnapshotPiece hi = PPut(s, PieceType.Rook, 4, 4);
            FreezeSoldiers(s);
            ClearEnemies(s, PieceType.GunFu);
            List<BossEvent> ev;
            using (FixedOn(DAI, "guard")) ev = AdvanceEv(s, 10 * Sec);
            Check(!ev.Any(e => e.Type == BossEventType.Telegraph) && ev.Any(e => e.Type == BossEventType.GuardStart) && s.boss.ai.phase == BossAiState.Acting && s.boss.ai.attack == "guard" && s.boss.ai.until == 15 * Sec,
                "守備: 予告なしですぐ 5 秒構える");
            Check(BossEngine<SnapshotPiece>.IsInvulnerable(s.boss), "守備: 構えている間はダメージを受けない");
            s.bossEvents = new List<BossEvent>();
            Check(BoardSimulator.Apply(s, PMove(s, 4, 4, 4, 2), out string why), $"守備: 守備中もボスのマスへ動かせる（{why}）");
            Check(s.boss.hp == 30 && PAt(s, 4, 4) == hi && s.bossEvents.Any(e => e.Type == BossEventType.Guarded), "守備: 0 ダメージで駒は跳ね返る（防いだ通知）");
            s.bossEvents = null;
            Check(BossEngine<SnapshotPiece>.Damage(Host(s), BossHit.OfMagic("Cannon"), PS(4, 1)) == 0 && s.boss.hp == 30, "守備: 破壊砲も 0 ダメージ");
            using (FixedOn(DAI, "greatsword")) ev = AdvanceEv(s, 5 * Sec);
            Check(s.boss.ai.phase == BossAiState.Cooldown && s.boss.ai.until == 25 * Sec && s.boss.ai.last == "guard" && ev.Any(e => e.Type == BossEventType.GuardEnd), "守備: 5 秒で終わり、休憩なしで行動ペース 10 秒");
            hi.cooldownUntil = 0;
            BoardSimulator.Apply(s, PMove(s, 4, 4, 4, 2));
            Check(s.boss.hp == 26 && hi.promoted, $"守備: 終わったらまた当たる（守備中の攻撃で成った竜王の 4 ダメージ。HP {s.boss.hp}）");
        }
        // 守備は防壁・兵を守らない
        using (FixedOn(DAI, "guard"))
        {
            BoardSnapshot s = RaidD();
            PPut(s, PieceType.Pawn, 0, 4);
            FreezeSoldiers(s);
            BoardSimulator.Advance(s, 10 * Sec);
            BoardSimulator.Apply(s, PMove(s, 0, 4, 0, 3));
            Check(PAt(s, 0, 3) != null && PAt(s, 0, 3).type == PieceType.Pawn && PAt(s, 0, 3).Owner == P1, "守備: 兵は守らない（軍歩兵を取れる）");
        }
        // 出陣命令：予告 2 秒で四段の 9 マスを光らせ、実行のときに空いたマスだけに軍歩兵を置く
        using (FixedOn(DAI, "deploy"))
        {
            BoardSnapshot s = RaidD();
            FreezeSoldiers(s);
            ClearEnemies(s, PieceType.GunFu);
            BoardSimulator.Advance(s, 10 * Sec);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.attack == "deploy" && s.boss.ai.until == 12 * Sec && PKey(s.boss.ai.cells) == PKey(Enumerable.Range(0, 9).Select(x => (x, 3)).ToArray()),
                "出陣命令: 予告 2 秒・四段の 9 マス");
            PPut(s, PieceType.GoldGeneral, 1, 3);                 // 予告のあとに入った駒のマスも飛ばす
            EnemyP(s, PieceType.GunJu, 2, 3);
            PCell(s, 7, 3).state = CellState.Broken;
            List<BossEvent> ev = AdvanceEv(s, 2 * Sec);
            Check(EnemyKey(s, PieceType.GunFu) == PKey((0, 3), (3, 3), (4, 3), (5, 3), (6, 3), (8, 3)), $"出陣命令: 空いたマスだけに軍歩兵（{EnemyKey(s, PieceType.GunFu)}）");
            Check(PAt(s, 1, 3).type == PieceType.GoldGeneral && ev.Count(e => e.Type == BossEventType.MinionSpawned) == 6 && !ev.Any(e => e.Type == BossEventType.HitPiece)
                  && ev.Any(e => e.Type == BossEventType.Deployed && e.Cells.Count == 6), "出陣命令: 駒には当てない（6 体出す）");
            BoardSimulator.Advance(s, 3 * Sec);
            Check(PAt(s, 0, 4) != null && PAt(s, 0, 4).type == PieceType.GunFu, "出陣命令: 出した軍歩兵は 3 秒後に進む");
        }
        // 守備・出陣命令は 2 回続けない
        {
            BoardSnapshot s = RaidD();
            IBossHost<SnapshotPiece> h = Host(s);
            bool repeated = false;
            foreach (string id in new[] { "guard", "deploy" })
            {
                s.boss.ai.last = id;
                for (int i = 0; i < 60; i++) repeated |= BossEngine<SnapshotPiece>.ChooseAttack(h) == id;
            }
            Check(!repeated, "大将軍: 守備・出陣命令は 2 回続けない");
            s.boss.ai.last = "bow";
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < 200; i++) seen.Add(BossEngine<SnapshotPiece>.ChooseAttack(h));
            Check(string.Join(" ", seen.OrderBy(v => v, StringComparer.Ordinal)) == "bow deploy greatsword guard", "大将軍: 4 種とも選ばれる");
        }
    }

    // ------------------------------------------------------------
    //  大将軍の勝敗
    // ------------------------------------------------------------
    static void TestDaishogunResult()
    {
        // HP 0 でも軍歩兵・軍銃兵が残れば続く。大将軍の攻撃は止まり、兵は動き続ける。大将軍のマスは空く
        {
            BoardSnapshot s = RaidD();
            s.boss.hp = 0;
            foreach (SnapshotPiece p in EnemiesOf(s, PieceType.GunJu).Skip(1)) s.Remove(p); // 王将しかいないので、銃兵は 1 体だけにする
            Check(BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.None && !PBossAt(s, 4, 1), "大将軍: HP 0 でも兵が残れば続く（ボスのマスは空く）");
            List<BossEvent> ev = AdvanceEv(s, 12 * Sec);
            Check(!s.gameOver && !ev.Any(e => e.Type == BossEventType.Telegraph) && s.boss.ai.phase == BossAiState.Cooldown, "大将軍: 倒れたあとは攻撃しない");
            Check(ev.Any(e => e.Type == BossEventType.GunnerShot), "大将軍: 倒れたあとも軍銃兵は撃つ");
            Check(!EnemiesOf(s, PieceType.GunFu).Any(p => 8 - p.x == 3) && EnemiesOf(s, PieceType.GunFu).Any(p => p.y == 4), "大将軍: 倒れたあとも軍歩兵は進む（大将軍の下の筋も）");
        }
        // 防壁だけ残れば勝ち（討伐成功）。軍銃兵が 1 体でも残れば続く
        {
            BoardSnapshot s = RaidD();
            s.boss.hp = 0;
            ClearEnemies(s, PieceType.GunFu);
            List<SnapshotPiece> guns = EnemiesOf(s, PieceType.GunJu);
            foreach (SnapshotPiece p in guns.Skip(1)) s.Remove(p);
            Check(BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.None && BossEngine<SnapshotPiece>.RequiredMinionsLeft(Host(s)) == 1, "大将軍: 軍銃兵が 1 体でも残れば続く");
            s.Remove(guns[0]);
            Check(EnemiesOf(s, PieceType.Bouheki).Count == 6 && BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.Cleared, "大将軍: 防壁だけ残れば討伐成功");
        }
        // 兵を全部倒しても、大将軍の HP が残っていれば続く
        {
            BoardSnapshot s = RaidD();
            ClearEnemies(s, PieceType.GunFu, PieceType.GunJu);
            Check(BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.None, "大将軍: 兵を全部倒しても HP が残れば続く");
        }
        // 最後の兵を取った行動で討伐成功になる
        {
            BoardSnapshot s = RaidD();
            PPut(s, PieceType.GoldGeneral, 0, 4);
            s.boss.hp = 0;
            ClearEnemies(s, PieceType.GunJu, PieceType.GunFu);
            EnemyP(s, PieceType.GunFu, 0, 3);
            Check(BoardSimulator.Apply(s, PMove(s, 0, 4, 0, 3)) && s.gameOver && s.winner == 0, "大将軍: 最後の兵を取った行動で討伐成功");
        }
        // 赤鬼王は今までどおり（HP 0 で、小赤鬼が残っていても討伐成功。倒れたら小赤鬼も止まる）
        {
            BoardSnapshot s = Raid();
            SnapshotPiece m = SpawnP(s, 4, 4);
            s.boss.hp = 0;
            Check(BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.Cleared, "赤鬼王: 小赤鬼が残っていても HP 0 で討伐成功");
            BossEngine<SnapshotPiece>.Tick(Host(s));
            Check(PAt(s, 4, 4) == m, "赤鬼王: 倒れたら小赤鬼も止まる");
        }
    }

    // ------------------------------------------------------------
    //  大将軍の状態の往復・決定的か
    // ------------------------------------------------------------
    static void TestDaishogunState()
    {
        // 弓の照準の予告中・兵の照準中に JSON にしても、同じに進む（ハッシュが一致する）
        foreach (uint seed in new uint[] { 1, 2, 3 })
        {
            BoardSnapshot s = RaidD(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 3, 6), FP(PieceType.Pawn, 5, 6), FP(PieceType.GoldGeneral, 2, 7) }, seed);
            using (FixedOn(DAI, "bow")) BoardSimulator.Advance(s, 11 * Sec);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.aim.active, $"大将軍の往復（seed {seed}）: 弓の予告中");
            BoardSnapshot t = BoardSnapshot.FromJson(s.ToJson());
            bool same = PrototypeHashText(t) == PrototypeHashText(s) && t.Diff(s, true).Count == 0;
            GameRng rng = new GameRng(seed);
            for (int i = 0; i < 40 && same; i++)
            {
                int dx = rng.Next() < 0.5 ? -1 : 1;
                foreach (BoardSnapshot st in new[] { s, t })
                {
                    SnapshotPiece k = st.pieces.FirstOrDefault(p => p.type == PieceType.King && p.Owner == P1);
                    if (k == null || i % 7 != 0) continue;
                    int ny = Math.Max(0, Math.Min(8, k.y + dx));
                    if (st.At(k.x, ny) == null && !st.BossAt(k.x, ny)) st.MovePiece(k, k.x, ny);
                }
                BoardSimulator.Advance(s, Sec / 2);
                BoardSimulator.Advance(t, Sec / 2);
                same = PrototypeHashText(t) == PrototypeHashText(s);
            }
            Check(same, $"大将軍の往復（seed {seed}）: JSON にしても同じに進む");
        }
        // ランダムな操作で 200 秒 × 4 局：エラーなく進み、決定的。途中で JSON を往復させても同じ
        int ended = 0, cleared = 0;
        foreach (uint seed in new uint[] { 11, 12, 13, 14 })
        {
            string Run(bool roundTrip, out BoardSnapshot end)
            {
                List<RaidPlacement> f = new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Rook, 7, 7), FP(PieceType.Bishop, 1, 7) };
                for (int x = 0; x < 9; x++) f.Add(FP(PieceType.Pawn, x, 6));
                BoardSnapshot st = RaidD(f, seed);
                GameRng rng = new GameRng(seed * 31);
                for (int t = 0; t < 200 * Sec && !st.gameOver; t++)
                {
                    if (rng.Next() < 0.1)
                    {
                        List<MatchAction> legal = BoardSimulator.LegalActions(st, P1);
                        if (legal.Count > 0) BoardSimulator.Apply(st, rng.Pick(legal));
                    }
                    if (roundTrip && t % 97 == 0) st = BoardSnapshot.FromJson(st.ToJson());
                    BoardSimulator.Tick(st);
                }
                end = st;
                return PrototypeHashText(st);
            }
            string a = Run(false, out BoardSnapshot e1), b = Run(true, out _);
            Check(a == b, $"大将軍のランダムな対局（seed {seed}）: 決定的で、JSON の往復をはさんでも同じ");
            if (e1.gameOver) ended++;
            if (e1.gameOver && e1.winner == 0) cleared++;
        }
        Console.WriteLine($"daishogun random: 4 局のうち {ended} 局が終局（討伐成功 {cleared}）");
    }
}
