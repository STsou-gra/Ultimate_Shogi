// 討伐任務（ボス）のシミュレーション（Match/BossEngine.cs・BoardSimulator.Boss.cs・RaidRules.cs）のテスト。
//  ・Prototype の tests/core/boss.test.js・raid-transcendent.test.js・raid-meteor.test.js を移したもの
//  ・Prototype の棋譜 tests/kifus/raid-akaoni-1〜4.json（PrototypeKifus/ に写した）を再生し、全行動のあとの盤面のハッシュが
//    Prototype の hash.js の値と一致するか（KassenTests.cs の PrototypeHashText に、ボス・討伐任務・手下の項目を足した）
//
// 座標：Prototype の (x = 筋, y = 段。先手は下 y = 6〜8) は Unity の (x = 8 − y, y = x)。テストの場面は Prototype の数値のまま書けるよう、
// PX(px, py)（Unity の段）・PY(px, py)（Unity の筋）・FP（編成の駒）・PPut（駒を置く）で直す
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

static partial class Program
{
    static void TestBoss()
    {
        TestBossDefs();
        TestRaidStart();
        TestBossSquares();
        TestBossAttackCells();
        TestBossAi();
        TestBossRampage();
        TestRaidFormation();
        TestRaidDeterminism();
        TestMinions();
        TestRaidTranscendent();
        TestRaidHashAndState();
        TestRaidKifus();
    }

    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------

    static BossDef AKAONI => BossDefs.Akaoni;

    // Prototype のマス → Unity のマス
    static MatchSquare PS(int px, int py) => new MatchSquare(8 - py, px);
    static RaidPlacement FP(PieceType t, int px, int py) => new RaidPlacement(t, 8 - py, px);
    static List<RaidPlacement> KingOnly() => new List<RaidPlacement> { FP(PieceType.King, 4, 8) };

    // 討伐任務の局面（Prototype の setupGame：エナジー 0・時間 0。編成を並べ、ボスは奥の中央）。loadout は Unity の魔法の Id
    static BoardSnapshot Raid(List<RaidPlacement> formation = null, uint seed = 1, PieceType[] picks = null, string[] loadout = null, List<SnapshotSkill> skills = null)
    {
        BoardSnapshot s = Kassen(seed, loadout ?? new[] { "Shield", "Sword", "Meteor" }, new string[0]);
        if (skills != null) s.players[0].skills = skills;
        RaidFormation f = new RaidFormation { pieces = formation ?? KingOnly() };
        if (picks != null) f.forgePicks.AddRange(picks);
        BoardSimulator.SetupRaid(s, AKAONI, f);
        return s;
    }

    // ---- Prototype の座標で場面を書くための道具（px = 筋・py = 段。Unity は (8 − py, px)）
    static SnapshotPiece PPut(BoardSnapshot s, PieceType t, int px, int py) => Put(s, t, P1, 8 - py, px);
    static SnapshotPiece PAt(BoardSnapshot s, int px, int py) => s.At(8 - py, px);
    static SnapshotCell PCell(BoardSnapshot s, int px, int py) => s.Cell(8 - py, px);
    static bool PBossAt(BoardSnapshot s, int px, int py) => s.BossAt(8 - py, px);
    // 移動（Prototype と同じく「成れるなら成る」）
    static MatchAction PMove(BoardSnapshot s, int fx, int fy, int tx, int ty)
    {
        SnapshotPiece p = PAt(s, fx, fy);
        bool promote = p != null && BoardSimulator.IsPromoteChoiceAllowed(s, p, 8 - ty, tx, true);
        return KMv(P1, 8 - fy, fx, 8 - ty, tx).With(a => a.promote = promote);
    }
    static MatchAction With(this MatchAction a, Action<MatchAction> f) { f(a); return a; }
    static MatchAction PMagic(string id, int px, int py) => Own(Mg(id, (8 - py, px)), P1);
    // マスの一覧を Prototype の "x,y" の並び（文字列の順）にする
    static string PKey(IEnumerable<MatchSquare> cells) => string.Join(" ", cells.Select(c => $"{c.y},{8 - c.x}").OrderBy(v => v, StringComparer.Ordinal));
    static string PKey(params (int x, int y)[] cells) => string.Join(" ", cells.Select(c => $"{c.x},{c.y}").OrderBy(v => v, StringComparer.Ordinal));
    static void SetBossP(BoardSnapshot s, int px, int py) { s.boss.y = px; s.boss.x = 9 - py - s.boss.h; }
    static void FaceP(BoardSnapshot s, int dx, int dy) { s.boss.facingY = dx; s.boss.facingX = -dy; }
    static string FacingP(BossState b) => $"{b.facingY},{-b.facingX}";
    static IBossHost<SnapshotPiece> Host(BoardSnapshot s) => BoardSimulator.BossHost(s);
    static List<MatchSquare> Cells(BoardSnapshot s, BossAttackDef atk) => BossEngine<SnapshotPiece>.AttackCells(Host(s), atk);
    // ボスを Prototype の (dx, dy) へ動かす
    static bool MoveBossP(BoardSnapshot s, int dx, int dy, string attack = null) => BossEngine<SnapshotPiece>.MoveBoss(Host(s), -dy, dx, attack);
    static SnapshotPiece SpawnP(BoardSnapshot s, int px, int py) => BossEngine<SnapshotPiece>.Spawn(Host(s), PieceType.Kooni, 8 - py, px);
    static void FreezeAi(BoardSnapshot s) { s.boss.ai.until = 1000000000; }
    // 通知を集めながら進める
    static List<BossEvent> AdvanceEv(BoardSnapshot s, int ticks)
    {
        s.bossEvents = new List<BossEvent>();
        BoardSimulator.Advance(s, ticks);
        List<BossEvent> ev = s.bossEvents;
        s.bossEvents = null;
        return ev;
    }
    // 攻撃を固定する（戻すには Dispose）
    sealed class Restore : IDisposable { readonly Action a; public Restore(Action a) { this.a = a; } public void Dispose() => a(); }
    static IDisposable Fixed(string id)
    {
        Func<BossState, int, string> keep = AKAONI.ChooseAttack;
        AKAONI.ChooseAttack = (b, now) => id;
        return new Restore(() => AKAONI.ChooseAttack = keep);
    }
    static IDisposable SweepSides(params int[] w)
    {
        int[] keep = BossAttacks.SweepSideWeights;
        BossAttacks.SweepSideWeights = w;
        return new Restore(() => BossAttacks.SweepSideWeights = keep);
    }
    static readonly int[] FrontOnly = { 1, 0, 0, 0 };

    // 王将だけの編成で始めて、extra の駒（自陣の外でも）を置く。extra に王将があれば、王将をそこへ移す
    static BoardSnapshot RaidWith(params (PieceType t, int px, int py)[] extra)
    {
        bool king = extra.Any(e => e.t == PieceType.King);
        BoardSnapshot s = Raid(king ? new List<RaidPlacement> { FP(PieceType.King, 8, 8) } : KingOnly());
        foreach ((PieceType t, int px, int py) in extra)
        {
            if (t == PieceType.King) s.MovePiece(s.pieces.First(p => p.type == PieceType.King), 8 - py, px);
            else PPut(s, t, px, py);
        }
        return s;
    }

    // ------------------------------------------------------------
    //  ボスの定義・討伐任務の開始（boss.test.js「ボスの定義」「討伐任務の開始」）
    // ------------------------------------------------------------
    static void TestBossDefs()
    {
        Check(BossDefs.All.Select(b => b.Id).SequenceEqual(new[] { "akaoni", "daishogun" }), "ボス: 赤鬼王・大将軍の順");
        Check(AKAONI.Name == "赤鬼王" && AKAONI.RulesetId == "kassen" && AKAONI.W == 3 && AKAONI.H == 3 && AKAONI.Hp == 30 && AKAONI.CooldownSec == 5f, "赤鬼王: 合戦将棋型・3×3・HP 30・行動ペース 5 秒");
        Check(AKAONI.Attacks.Select(a => a.Id).SequenceEqual(new[] { "sweep", "smash", "spin", "summon", "rampage" }), "赤鬼王: 攻撃 5 種");
        Check(BossDefs.Validate(AKAONI).Count == 0 && AKAONI.Implemented, "赤鬼王: 定義の検査に通る");
        Check(AKAONI.KingMaxHits == 3 && AKAONI.CostLimit == 50 && AKAONI.ForgeSlots == 2, "赤鬼王: 王将 3 回目で負け・コスト上限 50・錬成駒 2 種");
        Check(BossDefs.Daishogun.Implemented && BossDefs.Validate(BossDefs.Daishogun).Count == 0, "大将軍: 選べる・定義が正しい");
        Check(BossDefs.Validate(new BossDef { Id = "x", W = 3, H = 3, StartX = 8, StartY = 3, Hp = 30 }).Count > 0, "ボス: 盤に収まらない開始位置は弾く");
        Check(BossAttacks.Sweep.TelegraphSec == 5f && BossAttacks.Smash.TelegraphSec == 5f && BossAttacks.Spin.TelegraphSec == 5f && BossAttacks.Summon.TelegraphSec == 4f, "攻撃: 予告 5 秒（小赤鬼号令は 4 秒）");
        Check(BossAttacks.Rampage.TelegraphSec == 5f && BossAttacks.Rampage.DurationSec == 10f && BossAttacks.Rampage.StepSec == 1f && BossAttacks.Rampage.RestSec == 5f, "暴れ回り: 予告 5 秒・10 秒間・1 秒ごと・休憩 5 秒");
        Check(GameModes.BossRaid.MoveEnergy == 2 && !GameModes.BossRaid.IsPlayerSeat(P2) && GameModes.BossRaid.IsPlayerSeat(P1) && GameModes.Versus.IsPlayerSeat(P2), "モード: 討伐任務は移動 +2・後手の席はボス");
    }

    static void TestRaidStart()
    {
        BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 4, 6) });
        BossState b = s.boss;
        // Prototype の (3,0) 3×3 ＝ Unity の段 6〜8・筋 3〜5。向きは下（プレイヤー側）
        Check(b.active && b.id == "akaoni" && b.x == 6 && b.y == 3 && b.w == 3 && b.h == 3 && b.hp == 30 && b.maxHp == 30 && FacingP(b) == "0,1", "開始: ボスは奥の中央 3×3・HP 30・下向き");
        Check(b.ai.phase == BossAiState.Cooldown && b.ai.until == 5 * Sec, "開始: 待ち 5 秒");
        Check(PBossAt(s, 3, 0) && PBossAt(s, 5, 2) && !PBossAt(s, 2, 0) && !PBossAt(s, 4, 3), "開始: ボスのマス");
        Check(s.pieces.Count == 2 && s.pieces.All(p => p.initial && p.Owner == P1), "開始: 編成の 2 枚（初期配置の駒）");
        Check(s.raid.active && s.raid.kingHits == 0 && s.IsRaid && s.mode == "bossRaid" && s.IsRealtime, "開始: 討伐任務・王将の被弾 0");
        Check(BoardSimulator.LegalActions(s, P2).Count == 0 && !BoardSimulator.CanApply(s, KMv(P2, 2, 4, 3, 4)), "開始: 後手の席は行動できない");
        Check(s.players[1].skills.Count == 0 && s.players[0].transcendentUnlocked && !s.players[1].transcendentUnlocked, "開始: 後手は魔法なし・先手の超常魔法は解放済み");

        // 時間でエナジーが増えるのはプレイヤーの席だけ
        BoardSnapshot t = Raid();
        FreezeAi(t);
        BoardSimulator.Advance(t, Sec * 3);
        Check(t.players[0].energy == 3 && t.players[1].energy == 0, "開始: 時間のエナジーは先手だけ");

        // 不正な編成では始められない
        bool threw = false;
        try { Raid(new List<RaidPlacement>()); } catch (ArgumentException e) { threw = e.Message.Contains("王将"); }
        Check(threw, "開始: 王将のない編成では始められない");
    }

    // ------------------------------------------------------------
    //  ボスのマスの扱い（boss.test.js「ボスのマスの扱い」・raid-meteor.test.js）
    // ------------------------------------------------------------
    static void TestBossSquares()
    {
        // 移動先にボスのマスを選べ、走り駒はボスで止まる。持ち駒はボスのマスに打てない
        {
            BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Rook, 4, 7), FP(PieceType.Lance, 3, 8) });
            SnapshotPiece hi = PAt(s, 4, 7);
            List<int> ups = Enumerable.Range(0, 7).Where(py => BoardSimulator.CanMoveTo(s, hi, 8 - py, 4)).ToList();
            Check(ups.Min() == 2, $"ボスのマス: 飛はボスのマス (4,2) で止まる（{string.Join(",", ups)}）");
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Pawn)] = 1;
            s.clockTime = MatchPolicy.SecToTicks(K.Durations.DropLockSec);
            bool onBoss = false;
            for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++) if (s.BossAt(x, y) && BoardSimulator.CanDropTo(s, P1, PieceType.Pawn, x, y)) onBoss = true;
            Check(!onBoss, "ボスのマス: 持ち駒は打てない");
        }
        // ボスのマスへの移動は攻撃：ダメージを与えて元のマスへ跳ね返る。移動回数と +2 エナジーは数える
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 4, 3));
            FreezeAi(s);
            SnapshotPiece fu = PAt(s, 4, 3);
            s.bossEvents = new List<BossEvent>();
            Check(BoardSimulator.Apply(s, PMove(s, 4, 3, 4, 2)), "攻撃: ボスのマスへ動かせる");
            Check(s.boss.hp == 29 && PAt(s, 4, 3) == fu && fu.moveCount == 1 && s.players[0].energy == 2, $"攻撃: HP 29・元のマス・移動回数 1・+2 エナジー（HP {s.boss.hp}・エナジー {s.players[0].energy}）");
            Check(fu.promoted, "攻撃: 攻撃したマスが敵陣なら成る");
            Check(s.bossEvents.Any(e => e.Type == BossEventType.Damaged && e.Amount == 1 && e.Hp == 29), "攻撃: ダメージの通知");
        }
        // 駒の種類・成り・技・魔法でダメージが変わる
        {
            PieceType[] types = { PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook, PieceType.King };
            Check(types.Select(t => RaidRules.BossDamage(AKAONI, BossHit.OfPiece(t, false))).SequenceEqual(new[] { 1, 1, 2, 2, 2, 3, 3, 1 }), "ダメージ: 歩 1・香 1・桂 2・銀 2・金 2・角 3・飛 3・王 1");
            Check(RaidRules.BossDamage(AKAONI, BossHit.OfPiece(PieceType.Pawn, true)) == 2 && RaidRules.BossDamage(AKAONI, BossHit.OfPiece(PieceType.Rook, true)) == 4, "ダメージ: 成った駒は +1");
            Check(RaidRules.BossDamage(AKAONI, BossHit.OfSpecial(PieceType.Pawn)) == 3, "ダメージ: 必殺技 3");
            Check(RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Sword")) == 2 && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Bomb")) == 3
                  && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Meteor")) == 5 && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Cannon")) == 8
                  && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Shield")) == 0, "ダメージ: 剣 2・ボム 3・隕石 5・破壊砲 8・盾 0");
            Check(RaidRules.BossDamage(AKAONI, BossHit.Explosion) == 3 && RaidRules.BossDamage(AKAONI, BossHit.Snipe) == 2 && RaidRules.BossDamage(AKAONI, BossHit.Phalanx) == 1, "ダメージ: 爆発 3・狙撃 2・ファランクスの突き 1");
            Check(RaidRules.BossDamage(AKAONI, BossHit.OfPiece(PieceType.Giant, false)) == 3 && RaidRules.BossDamage(AKAONI, BossHit.OfPiece(PieceType.Kooni, false)) == 1, "ダメージ: 巨人 3・値のない駒は 1");
            // 倍率（Prototype の damageTaken）
            AKAONI.DamageRate = h => h.kind == BossHitKind.Piece && h.piece == PieceType.Rook ? 2f : h.kind == BossHitKind.Magic ? 0.5f : h.kind == BossHitKind.Explosion ? 0f : 1f;
            try
            {
                Check(RaidRules.BossDamage(AKAONI, BossHit.OfPiece(PieceType.Rook, false)) == 6 && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Cannon")) == 4
                      && RaidRules.BossDamage(AKAONI, BossHit.OfMagic("Sword")) == 1 && RaidRules.BossDamage(AKAONI, BossHit.Explosion) == 0, "ダメージ: 倍率（切り捨て・1 以上・0 なら効かない）");
            }
            finally { AKAONI.DamageRate = null; }
        }
        // 必殺技（突撃）でボスに突っ込むと、必殺技のダメージ 1 回だけ
        {
            BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 4, 6) });
            FreezeAi(s);
            PAt(s, 4, 6).moveCount = 2;
            Check(BoardSimulator.Apply(s, Own(Spc(2, 4), P1)), "突撃: 使える");
            Check(s.boss.hp == 27 && PAt(s, 4, 3) != null && PAt(s, 4, 3).type == PieceType.Pawn, $"突撃: HP 27・ボスの手前で止まる（HP {s.boss.hp}）");
            Check(s.players[0].energy == 2, "突撃: 必殺技でも +2 エナジー");
        }
        // 光線（飛「龍王旋風」）はボスに当たる
        {
            BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Rook, 4, 6) });
            FreezeAi(s);
            PAt(s, 4, 6).moveCount = 3;
            Check(BoardSimulator.CanApply(s, Own(Spc(2, 4), P1)), "龍王旋風: ボスに当たるなら使える");
            BoardSimulator.Apply(s, Own(Spc(2, 4), P1));
            Check(s.boss.hp == 27, $"龍王旋風: HP 27（{s.boss.hp}）");
        }
        // 金剛衝：周囲にボスがいれば使えて、1 回だけ当たる。桂の天馬跳躍はボスのマスへ跳べる
        {
            BoardSnapshot s = RaidWith((PieceType.GoldGeneral, 4, 3), (PieceType.Knight, 2, 4));
            FreezeAi(s);
            PAt(s, 4, 3).moveCount = 2;
            PAt(s, 2, 4).moveCount = 2;
            Check(BoardSimulator.Apply(s, Own(Spc(5, 4), P1)) && s.boss.hp == 27, $"金剛衝: 周囲のボスに 3（{s.boss.hp}）");
            Check(BoardSimulator.Apply(s, Own(Spc(4, 2, 6, 3), P1)) && s.boss.hp == 24 && PAt(s, 2, 4) != null, $"天馬跳躍: ボスのマスへ跳ぶと攻撃して跳ね返る（{s.boss.hp}）");
        }
        // 狙撃はボスのマスを撃てる（狙撃のダメージ）
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece sn = PPut(s, PieceType.Sniper, 0, 8);
            Check(BoardSimulator.SnipeTargets(s, sn).Count(c => s.BossAt(c.x, c.y)) == 9, "狙撃: ボスの 9 マスを撃てる");
            MatchAction a = Own(MatchAction.Snipe(new Vector2Int(0, 0), new Vector2Int(7, 4)), P1);
            Check(BoardSimulator.Apply(s, a) && s.boss.hp == 28 && s.players[0].energy == 0, $"狙撃: HP 28・+2 エナジーはない（{s.boss.hp}）");
        }
        // 火薬の爆発の 3×3 がボスに重なれば爆発のダメージ（1 回の爆発で 1 回）
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 4, 4));
            FreezeAi(s);
            PPut(s, PieceType.Gunpowder, 4, 3);
            BoardSimulator.Apply(s, PMove(s, 4, 4, 4, 3));
            Check(s.boss.hp == 27, $"爆発: HP 27（{s.boss.hp}）");
        }
        // 剣の前方 1×3・隕石の 3×3・破壊砲の帯がボスに重なれば、それぞれ 1 回だけ
        {
            BoardSnapshot s = RaidWith((PieceType.SilverGeneral, 4, 3));
            FreezeAi(s);
            s.players[0].energy = 100;
            Check(BoardSimulator.Apply(s, PMagic("Sword", 4, 3)) && s.boss.hp == 28, $"剣: HP 28（{s.boss.hp}）");
            Check(BoardSimulator.Apply(s, PMagic("Meteor", 4, 3)) && s.boss.hp == 23, $"隕石: 敵陣寄りの中心でも当たる・HP 23（{s.boss.hp}）");
            Check(PCell(s, 4, 2).IsBurning && s.boss.hp == 23, "隕石: ボスのマスも炎上するが、ボスには効かない");

            BoardSnapshot s2 = Raid(null, 1, null, new[] { "Shield", "Cannon" });
            FreezeAi(s2);
            s2.players[0].energy = 100;
            Check(BoardSimulator.Apply(s2, PMagic("Cannon", 1, 4)) && s2.boss.hp == 30, "破壊砲: 0〜2 筋はボスに重ならない");
            Check(BoardSimulator.Apply(s2, PMagic("Cannon", 2, 4)) && s2.boss.hp == 22, $"破壊砲: 1〜3 筋はボスに重なる・HP 22（{s2.boss.hp}）");
            Check(!PCell(s2, 3, 0).IsBroken && PCell(s2, 3, 5).IsBroken, "破壊砲: ボスのいるマスは壊さない");
            Check(!BoardSimulator.CanApply(s2, PMagic("Cannon", 4, 4)), "破壊砲: 王将のいる範囲は選べない");
        }
        // ボム：3×3 がボスに重なれば 3
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Bomb" });
            FreezeAi(s);
            PPut(s, PieceType.SilverGeneral, 4, 4);
            s.players[0].energy = 10;
            // 銀 (4,4) の 2 マス先の前 (4,2) はボスのマス
            Check(BoardSimulator.Apply(s, Own(MgTo("Bomb", 4, 4, 6, 4), P1)) && s.boss.hp == 27, $"ボム: HP 27（{s.boss.hp}）");
        }
        // ファランクスはボスの前で前進せず、槍でボスを突く
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece gi = PPut(s, PieceType.SilverGeneral, 4, 3);
            gi.shield = true; gi.phalanx = true;
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(3f));
            Check(PAt(s, 4, 3) == gi && s.boss.hp == 29, $"ファランクス: 前進せずに突く・HP 29（{s.boss.hp}）");
        }
        // 討伐任務で使える魔法（落とし穴・カウンター・天変地異・世界の終わりと、Unity だけの魔法は使えない。停止世界は使える）
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Pitfall", "Shield", "Explosion", "DoubleTurn", "WorldEnd" });
            s.players[0].energy = 50;
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
            Check(legal.Any(a => a.skill == "Shield") && !legal.Any(a => a.skill == "Pitfall" || a.skill == "WorldEnd" || a.skill == "Explosion" || a.skill == "DoubleTurn"), "魔法: 落とし穴・世界の終わり・爆発・2回行動は使えない");
            BoardSnapshot t = Raid(null, 1, null, new[] { "Cataclysm" });
            t.players[0].energy = 50;
            Check(!BoardSimulator.LegalActions(t, P1).Any(a => a.type == MatchActionType.Magic), "魔法: 天変地異は使えない");
            BoardSnapshot u = Raid(null, 1, null, new[] { "Shield", "TimeStop" });
            u.players[0].energy = 50;
            Check(BoardSimulator.LegalActions(u, P1).Any(a => a.skill == "TimeStop"), "魔法: 停止世界は使える");
            foreach (string id in new[] { "Shield", "Wall", "Sword", "Warp", "Bomb", "Wing", "Phalanx", "Meteor", "Cannon", "TimeStop", "UndeadParadise" })
                Check(RaidRules.IsMagicUsable(id), $"魔法: {id} は討伐任務で使える");
            foreach (string id in new[] { "Pitfall", "Counter", "Cataclysm", "WorldEnd", "DoubleTurn", "SwapPiece", "Explosion", "ReplicateKing" })
                Check(!RaidRules.IsMagicUsable(id), $"魔法: {id} は討伐任務で使えない");
        }
        // 隕石の中心マス（raid-meteor.test.js）
        {
            BoardSnapshot v = Kassen(1, new[] { "Shield", "Meteor" }, new[] { "Shield", "Meteor" });
            v.players[0].energy = 30;
            Check(!BoardSimulator.CanApply(v, PMagic("Meteor", 1, 1)), "隕石: 基本対戦は敵陣を中心に選べない");
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "Meteor" });
            FreezeAi(s);
            s.players[0].energy = 30;
            Check(BoardSimulator.CanApply(s, PMagic("Meteor", 4, 1)), "隕石: 討伐任務は敵陣のマスも中心に選べる");
            PCell(s, 1, 1).state = CellState.Broken;
            Check(!BoardSimulator.CanApply(s, PMagic("Meteor", 1, 1)) && !BoardSimulator.CanApply(s, PMagic("Meteor", 4, 7)), "隕石: 破壊マス・王将を巻き込む範囲は選べない");
            Check(BoardSimulator.Apply(s, PMagic("Meteor", 4, 1)) && s.boss.hp == 25, $"隕石: ボスに 5（{s.boss.hp}）");
        }
        // 王将がそれ以外の理由で倒れたら（落とし穴）その場でゲームオーバー
        {
            BoardSnapshot s = Raid(new List<RaidPlacement> { FP(PieceType.King, 4, 8), FP(PieceType.Pawn, 0, 6) });
            PCell(s, 4, 7).trap = 0;
            BoardSimulator.Apply(s, PMove(s, 4, 8, 4, 7));
            Check(s.gameOver && s.winner == 1 && BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.KingLost, "勝敗: 王将が倒れたら討伐失敗");
        }
        // HP 0 で討伐成功（クリアタイム）。倒れたボスはマスを占めない
        {
            BoardSnapshot s = RaidWith((PieceType.Rook, 4, 3));
            FreezeAi(s);
            BoardSimulator.Advance(s, 3 * Sec);
            s.boss.hp = 3;
            s.bossEvents = new List<BossEvent>();
            int energy = s.players[0].energy;
            BoardSimulator.Apply(s, PMove(s, 4, 3, 4, 2));
            Check(s.boss.hp == 0 && s.gameOver && s.winner == 0 && BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.Cleared, "勝敗: HP 0 で討伐成功");
            Check(s.clockTime == 3 * Sec && RaidResults.TimeLabel(s.clockTime) == "0:03.0", $"勝敗: クリアタイム 3 秒（{RaidResults.TimeLabel(s.clockTime)}）");
            Check(s.bossEvents.Any(e => e.Type == BossEventType.Defeated) && !PBossAt(s, 4, 1), "勝敗: 討伐の通知・倒れたボスはマスを占めない");
            Check(s.players[0].energy == energy, "勝敗: 勝敗がついた行動では +2 エナジーはない（Prototype と同じ順）");
            Check(BoardSimulator.LegalActions(s, P1).Count == 0, "勝敗: 終局後は行動できない");
        }
    }

    // ------------------------------------------------------------
    //  ボスの攻撃の範囲（boss.test.js「ボスの攻撃の範囲」）
    // ------------------------------------------------------------
    static void TestBossAttackCells()
    {
        using (SweepSides(FrontOnly))
        {
            BoardSnapshot s = Raid();
            Check(PKey(Cells(s, BossAttacks.Sweep)) == PKey((2, 3), (3, 3), (4, 3), (5, 3), (6, 3)), $"薙ぎ払い: 前方に接する横 5 マス（{PKey(Cells(s, BossAttacks.Sweep))}）");
            // 盤の端で切れる・向き（左右・上）
            SetBossP(s, 0, 3);
            FaceP(s, 0, 1);
            Check(PKey(Cells(s, BossAttacks.Sweep)) == PKey((0, 6), (1, 6), (2, 6), (3, 6)), "薙ぎ払い: 盤の端で切れる");
            FaceP(s, -1, 0);
            Check(Cells(s, BossAttacks.Sweep).Count == 0, "薙ぎ払い: 範囲のない側は空");
            FaceP(s, 1, 0);
            Check(PKey(Cells(s, BossAttacks.Sweep)) == PKey((3, 2), (3, 3), (3, 4), (3, 5), (3, 6)), "薙ぎ払い: 右向き");
            Check(PKey(Cells(s, BossAttacks.Smash)) == PKey((3, 4), (4, 4), (5, 4)), $"ぶっ叩き: 右向き（{PKey(Cells(s, BossAttacks.Smash))}）");
            FaceP(s, 0, -1);
            Check(PKey(Cells(s, BossAttacks.Smash)) == PKey((1, 2), (1, 1), (1, 0)), "ぶっ叩き: 上向き");
        }
        {
            // 薙ぎ払い：ときどき右側・左側（1×5）・裏側（5×1）。範囲のない側は抽選から外す
            BoardSnapshot s = Raid();
            SetBossP(s, 3, 3);
            Dictionary<string, string> sides = new Dictionary<string, string>
            {
                ["front"] = PKey(Enumerable.Range(2, 5).Select(x => (x, 6)).ToArray()),
                ["back"] = PKey(Enumerable.Range(2, 5).Select(x => (x, 2)).ToArray()),
                ["right"] = PKey(Enumerable.Range(2, 5).Select(y => (2, y)).ToArray()), // 下向きのボスの右手は盤の左
                ["left"] = PKey(Enumerable.Range(2, 5).Select(y => (6, y)).ToArray()),
            };
            Dictionary<string, int> n = sides.Keys.ToDictionary(k => k, k => 0);
            bool known = true;
            for (int i = 0; i < 700; i++)
            {
                string key = PKey(Cells(s, BossAttacks.Sweep));
                string side = sides.Keys.FirstOrDefault(d => sides[d] == key);
                if (side == null) known = false; else n[side]++;
            }
            Check(known && n["front"] > n["back"] + n["right"] && n["back"] > 40 && n["right"] > 40 && n["left"] > 40, $"薙ぎ払い: 正面が基本・ときどき右／左／裏（{string.Join(" ", n.Select(kv => kv.Key + ":" + kv.Value))}）");
            SetBossP(s, 0, 0);
            bool ok = true;
            for (int i = 0; i < 200; i++)
            {
                List<MatchSquare> cells = Cells(s, BossAttacks.Sweep);
                if (cells.Count == 0 || !cells.All(c => 8 - c.x <= 3 && c.y <= 3) || !(cells.Any(c => 8 - c.x == 3) || cells.Any(c => c.y == 3))) ok = false;
            }
            Check(ok, "薙ぎ払い: 端にいて範囲のない側は選ばれない");

            // 回転薙ぎ払い：周り 16 マス。角は重ねず、盤の外は切る
            SetBossP(s, 3, 3);
            List<(int, int)> ring = new List<(int, int)>();
            for (int y = 2; y <= 6; y++) for (int x = 2; x <= 6; x++) if (x == 2 || x == 6 || y == 2 || y == 6) ring.Add((x, y));
            List<MatchSquare> spin = Cells(s, BossAttacks.Spin);
            Check(spin.Count == 16 && PKey(spin) == PKey(ring.ToArray()), "回転薙ぎ払い: 周りの 16 マス");
            SetBossP(s, 0, 0);
            Check(PKey(Cells(s, BossAttacks.Spin)) == PKey((0, 3), (1, 3), (2, 3), (3, 3), (3, 0), (3, 1), (3, 2)), "回転薙ぎ払い: 盤の外は切る");
        }
        {
            BoardSnapshot s = Raid();
            Check(PKey(Cells(s, BossAttacks.Smash)) == PKey((4, 3), (4, 4), (4, 5)), "ぶっ叩き: 正面の中央の筋に奥行き 3 マス");
            Check(Cells(s, BossAttacks.Rampage) == null, "暴れ回り: 範囲を出さない");
        }
        // 向き：ボスの中心から王将へ、差の大きい軸（同じなら縦）
        {
            BossState b = BossState.Create(AKAONI);
            string Face(int kx, int ky) { BossEngine<SnapshotPiece>.FaceToward(b, 8 - ky, kx); return FacingP(b); }
            Check(Face(4, 8) == "0,1" && Face(8, 2) == "1,0" && Face(0, 1) == "-1,0" && Face(7, 4) == "0,1", "向き: 下・右・左・差が同じなら縦");
            b.x = 9 - 5 - 3; // Prototype の y = 5
            Check(Face(4, 0) == "0,-1", "向き: 上");
        }
    }

    // ------------------------------------------------------------
    //  ボスの AI（boss.test.js「ボスの AI（状態機械）」）
    // ------------------------------------------------------------
    static void TestBossAi()
    {
        // 待ち 5 秒 → 予告 5 秒（範囲は予告の開始時に確定）→ 攻撃 → 待ち 5 秒
        using (SweepSides(FrontOnly))
        using (Fixed("sweep"))
        {
            BoardSnapshot s = RaidWith((PieceType.SilverGeneral, 2, 3));
            AdvanceEv(s, 5 * Sec - 1);
            Check(s.boss.ai.phase == BossAiState.Cooldown, "AI: 5 秒たつまで待ち");
            List<BossEvent> ev = AdvanceEv(s, 1);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.attack == "sweep" && s.boss.ai.until == 10 * Sec, "AI: 5 秒で予告（10 秒まで）");
            BossEvent tel = ev.FirstOrDefault(e => e.Type == BossEventType.Telegraph);
            Check(tel != null && tel.Attack == "sweep" && tel.Sec == 5f && PKey(tel.Cells) == PKey((2, 3), (3, 3), (4, 3), (5, 3), (6, 3)), "AI: 予告の通知（攻撃・秒数・範囲）");
            // 予告中に駒を範囲の外へ逃がす
            SnapshotPiece gi = PAt(s, 2, 3);
            BoardSimulator.Advance(s, 2 * Sec);
            Check(BoardSimulator.Apply(s, PMove(s, 2, 3, 1, 4)), "AI: 予告中に駒を逃がせる");
            ev = AdvanceEv(s, 3 * Sec);
            Check(ev.Any(e => e.Type == BossEventType.Attack) && s.Contains(gi) && 8 - gi.x == 4, "AI: 範囲は予告の時点で決まるので、逃がせば避けられる");
            Check(s.boss.ai.phase == BossAiState.Cooldown && s.boss.ai.until == 15 * Sec && s.boss.ai.last == "sweep" && s.boss.ai.attack == "", "AI: 攻撃のあと待ち 5 秒");
        }
        // 攻撃を受けた駒：盾は割れて残る／なければ撃破（持ち主が駒の価値ぶんのエナジー・持ち駒にならない）
        using (Fixed("smash"))
        {
            BoardSnapshot s = RaidWith((PieceType.SilverGeneral, 4, 3), (PieceType.GoldGeneral, 4, 4));
            PAt(s, 4, 4).shield = true;
            BoardSimulator.Advance(s, 10 * Sec);
            Check(PAt(s, 4, 3) == null && PAt(s, 4, 4) != null && PAt(s, 4, 4).type == PieceType.GoldGeneral && !PAt(s, 4, 4).shield, "被弾: 銀は撃破・盾の金は盾が割れて残る");
            Check(s.players[0].energy == 10 + 3, $"被弾: 毎秒 +1 × 10 ＋ 銀の価値 3（{s.players[0].energy}）");
            Check(Hand(s, P1, PieceType.SilverGeneral) + Hand(s, P2, PieceType.SilverGeneral) == 0, "被弾: 誰の持ち駒にもならない");
        }
        // 王将はボスの攻撃に 2 回まで耐え、3 回目でゲームオーバー（盾が先に割れる）
        using (Fixed("smash"))
        {
            BoardSnapshot s = RaidWith((PieceType.King, 4, 3));
            SnapshotPiece king = PAt(s, 4, 3);
            king.shield = true;
            BoardSimulator.Advance(s, 10 * Sec);
            Check(!king.shield && s.raid.kingHits == 0, "王将: 盾が先に割れる");
            BoardSimulator.Advance(s, 10 * Sec);
            Check(s.raid.kingHits == 1, "王将: 被弾 1");
            BoardSimulator.Advance(s, 10 * Sec);
            Check(s.raid.kingHits == 2 && !s.gameOver, "王将: 被弾 2 まで耐える");
            List<BossEvent> ev = AdvanceEv(s, 10 * Sec);
            Check(s.raid.kingHits == 3 && s.gameOver && s.winner == 1 && BoardSimulator.RaidOutcomeOf(s) == RaidOutcome.KingHits, "王将: 3 回目でゲームオーバー");
            Check(ev.Any(e => e.Type == BossEventType.KingHit && e.Hits == 3 && e.Max == 3), "王将: 被弾の通知");
            Check(s.Contains(king), "王将: 駒は盤に残る");
        }
        // 攻撃の選び方：重み付きの抽選で、暴れ回り・小赤鬼号令は 2 回続けない
        {
            BoardSnapshot s = Raid();
            Dictionary<string, int> n = new[] { "sweep", "smash", "spin", "summon", "rampage" }.ToDictionary(k => k, k => 0);
            for (int i = 0; i < 700; i++) n[BossEngine<SnapshotPiece>.ChooseAttack(Host(s))]++;
            Check(n["spin"] > 40 && n["summon"] > 40 && n["rampage"] > 40 && n["sweep"] > n["rampage"], $"抽選: 重み付き（{string.Join(" ", n.Select(kv => kv.Key + ":" + kv.Value))}）");
            bool repeat = false;
            foreach (string id in new[] { "rampage", "summon" })
            {
                s.boss.ai.last = id;
                for (int i = 0; i < 100; i++) if (BossEngine<SnapshotPiece>.ChooseAttack(Host(s)) == id) repeat = true;
            }
            Check(!repeat, "抽選: 暴れ回り・小赤鬼号令は 2 回続けない");
        }
        // 回転薙ぎ払い：予告 5 秒のあと、周りの駒すべてに当たる
        using (Fixed("spin"))
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 2, 0), (PieceType.Pawn, 6, 2), (PieceType.Pawn, 4, 3), (PieceType.Pawn, 2, 3), (PieceType.Pawn, 4, 4));
            BoardSimulator.Advance(s, 5 * Sec);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.attack == "spin" && s.boss.ai.cells.Count == 11, $"回転薙ぎ払い: 上の端なので 11 マス（{s.boss.ai.cells.Count}）");
            List<BossEvent> ev = AdvanceEv(s, 5 * Sec);
            BossEvent atk = ev.FirstOrDefault(e => e.Type == BossEventType.Attack && e.Attack == "spin");
            Check(atk != null && atk.Hits == 4 && PAt(s, 4, 4) != null, "回転薙ぎ払い: 周りの 4 枚に当たり、範囲の外は残る");
            Check(PAt(s, 2, 0) == null && PAt(s, 6, 2) == null && PAt(s, 4, 3) == null && PAt(s, 2, 3) == null, "回転薙ぎ払い: 当たった駒は撃破");
        }
    }

    // ------------------------------------------------------------
    //  暴れ回り（boss.test.js の暴れ回りの分）
    // ------------------------------------------------------------
    static void TestBossRampage()
    {
        // 予告 5 秒 → 10 秒間 1 秒ごとに 1 マス（次の行き先は 1 秒前に表示）→ 休憩 5 秒 → 待ち 5 秒
        using (Fixed("rampage"))
        {
            BoardSnapshot s = Raid();
            BoardSimulator.Advance(s, 5 * Sec);
            Check(s.boss.ai.phase == BossAiState.Telegraph && s.boss.ai.attack == "rampage" && !s.boss.ai.hasCells, "暴れ回り: 範囲を出さない予告");
            List<BossEvent> ev = AdvanceEv(s, 5 * Sec);
            Check(s.boss.ai.phase == BossAiState.Acting && s.boss.ai.until == 20 * Sec, "暴れ回り: 10 秒間");
            BossEvent warn = ev.FirstOrDefault(e => e.Type == BossEventType.StepWarn);
            Check(warn != null && warn.HasTo && warn.Cells.Count == 9, "暴れ回り: 次の行き先の表示（9 マス）");
            ev = AdvanceEv(s, 10 * Sec);
            List<BossEvent> steps = ev.Where(e => e.Type == BossEventType.Step).ToList();
            Check(steps.Count == 10 && steps[0].To == warn.To, $"暴れ回り: 10 歩・最初の 1 歩は表示した行き先（{steps.Count}）");
            Check(steps.All(st => Math.Max(Math.Abs(st.To.x - st.From.x), Math.Abs(st.To.y - st.From.y)) == 1), "暴れ回り: 1 歩は 8 方向へ 1 マス");
            Check(s.boss.ai.phase == BossAiState.Resting && s.boss.ai.until == 25 * Sec && s.boss.ai.last == "rampage", "暴れ回り: 休憩 5 秒");
            BoardSimulator.Advance(s, 5 * Sec);
            Check(s.boss.ai.phase == BossAiState.Cooldown && s.boss.ai.until == 30 * Sec && s.boss.ai.attack == "", "暴れ回り: 休憩のあと待ち 5 秒");
        }
        // 盤外・破壊マスには行かない
        {
            BoardSnapshot s = Raid();
            SetBossP(s, 0, 0);
            PCell(s, 1, 3).state = CellState.Broken;
            Check(!MoveBossP(s, -1, 0) && !MoveBossP(s, 0, 1), "暴れ回りの移動: 盤外・破壊マスには行かない");
            Check(MoveBossP(s, 1, 0) && s.boss.y == 1, "暴れ回りの移動: 行ける方向には動く");
            GameRng rng = new GameRng(5);
            int[][] dirs = { new[] { -1, -1 }, new[] { 0, -1 }, new[] { 1, -1 }, new[] { -1, 0 }, new[] { 1, 0 }, new[] { -1, 1 }, new[] { 0, 1 }, new[] { 1, 1 } };
            bool ok = true;
            for (int i = 0; i < 200; i++)
            {
                int[] d = rng.Pick(dirs);
                MoveBossP(s, d[0], d[1]);
                foreach (MatchSquare c in BossEngine<SnapshotPiece>.BossCells(s.boss))
                {
                    if (!c.IsOnBoard || s.Cell(c.x, c.y).IsBroken) ok = false;
                }
            }
            Check(ok, "暴れ回りの移動: 200 回動かしても盤内・破壊マスの外");
        }
        // 踏まれた駒：撃破・盾と王将は移動方向へ押し出す・火薬は爆発してボスにも当たる
        {
            BoardSnapshot s = RaidWith((PieceType.King, 4, 3), (PieceType.Pawn, 3, 3), (PieceType.SilverGeneral, 5, 3));
            FreezeAi(s);
            PAt(s, 5, 3).shield = true;
            MoveBossP(s, 0, 1, "rampage");
            Check(9 - s.boss.x - s.boss.h == 1, "踏む: ボスが 1 マス下へ");
            Check(PAt(s, 3, 3) == null && s.players[0].energy == 1, "踏む: 歩は撃破（歩の価値のエナジー）");
            Check(PAt(s, 4, 4) != null && PAt(s, 4, 4).type == PieceType.King && s.raid.kingHits == 1, "踏む: 王将は押し出し（被弾 1）");
            Check(PAt(s, 5, 4) != null && PAt(s, 5, 4).type == PieceType.SilverGeneral && !PAt(s, 5, 4).shield, "踏む: 盾の銀は盾が割れて押し出し");
            BoardSnapshot s2 = Raid();
            FreezeAi(s2);
            PPut(s2, PieceType.Gunpowder, 4, 3);
            MoveBossP(s2, 0, 1, "rampage");
            Check(s2.boss.hp == 27 && PAt(s2, 4, 3) == null, $"踏む: 火薬は爆発してボスにも当たる（{s2.boss.hp}）");
        }
        // 押し出し先が塞がっていれば、いちばん近い空きマス
        {
            BoardSnapshot s = RaidWith((PieceType.King, 4, 3), (PieceType.Pawn, 4, 4), (PieceType.Pawn, 3, 4), (PieceType.Pawn, 5, 4));
            FreezeAi(s);
            MoveBossP(s, 0, 1, "rampage");
            SnapshotPiece k = s.pieces.First(p => p.type == PieceType.King);
            Check(!s.BossAt(k.x, k.y) && Math.Max(Math.Abs(k.y - 4), Math.Abs((8 - k.x) - 3)) <= 2, "押し出し: 塞がっていれば近くの空きマス");
            Invariants(s, "押し出しのあと");
        }
    }

    // ------------------------------------------------------------
    //  編成（boss.test.js「討伐任務の編成」）
    // ------------------------------------------------------------
    static void TestRaidFormation()
    {
        List<string> Errors(List<RaidPlacement> f, params PieceType[] picks)
        {
            RaidFormation setup = new RaidFormation { pieces = f };
            setup.forgePicks.AddRange(picks);
            return RaidRules.Validate(setup, AKAONI, out _);
        }
        int Cost(List<RaidPlacement> f) { RaidRules.Validate(new RaidFormation { pieces = f }, AKAONI, out int c); return c; }
        List<RaidPlacement> Plus(params RaidPlacement[] more) { List<RaidPlacement> l = KingOnly(); l.AddRange(more); return l; }

        Check(Errors(KingOnly()).Count == 0, "編成: 王将だけは通る");
        Check(Errors(new List<RaidPlacement>()).Contains("王将を 1 枚入れてください"), "編成: 王将なしは弾く");
        Check(Errors(Plus(FP(PieceType.King, 3, 8))).Contains("王将は 1 枚だけです"), "編成: 王将 2 枚は弾く");
        List<RaidPlacement> big = Plus(Enumerable.Range(0, 9).Select(x => FP(x < 5 ? PieceType.Rook : PieceType.Bishop, x, 7)).ToArray());
        Check(Errors(big).Any(e => e.StartsWith("コストが上限を超えています")), "編成: コスト超過は弾く");
        Check(Errors(Plus(FP(PieceType.Pawn, 0, 5))).Contains("駒は自陣（手前 3 段）にだけ置けます"), "編成: 自陣の外は弾く");
        Check(Errors(Plus(FP(PieceType.Pawn, 4, 8))).Contains("同じマスに 2 つの駒は置けません"), "編成: 同じマスは弾く");
        Check(Errors(Plus(FP(PieceType.Gunpowder, 0, 8))).Count == 0 && Cost(Plus(FP(PieceType.Gunpowder, 0, 8))) == 3, "編成: 火薬も置ける（コスト 3）");
        Check(Errors(Plus(FP(PieceType.Sniper, 0, 8))).Count == 0 && Cost(Plus(FP(PieceType.Sniper, 0, 8))) == 8, "編成: 狙撃手も置ける（コスト 8）");
        Check(Errors(Plus(FP(PieceType.Kooni, 0, 8))).Count > 0 && Errors(Plus(FP(PieceType.Giant, 1, 7))).Count > 0, "編成: 小赤鬼・巨人は置けない");
        Check(Errors(KingOnly(), PieceType.Gunpowder, PieceType.Sniper).Count == 0, "錬成駒: 2 種まで選べる");
        Check(Errors(KingOnly(), PieceType.Gunpowder, PieceType.Gunpowder).Count > 0, "錬成駒: 同じ駒は 2 回選べない");
        Check(Errors(KingOnly(), PieceType.Gunpowder, PieceType.Sniper, PieceType.Pawn).Count > 0, "錬成駒: 枠（2 種）を超えると弾く");
        Check(Cost(Plus(FP(PieceType.Pawn, 0, 6))) == 1, "編成: 歩のコスト 1");
        PieceType[] costTypes = { PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook, PieceType.King };
        Check(costTypes.Select(RaidRules.TeamCost).SequenceEqual(new[] { 1, 3, 3, 4, 4, 6, 6, 0 }), "編成: コストは 歩1・香3・桂3・銀4・金4・角6・飛6・王0");

        // おまかせ配置はコストの上限以内で、検査に通る（赤鬼王は本将棋の初期配置 20 枚・コスト 49）
        List<RaidPlacement> auto = RaidRules.AutoFormation(AKAONI);
        Check(Errors(auto).Count == 0 && Cost(auto) == 49 && auto.Count == 20, $"おまかせ配置: 20 枚・コスト 49（{auto.Count} 枚・{Cost(auto)}）");
        BoardSnapshot init = Initial();
        Check(auto.All(f => init.At(f.x, f.y) != null && init.At(f.x, f.y).type == f.type && init.At(f.x, f.y).Owner == P1), "おまかせ配置: 本将棋の先手の初期配置と同じ");
        Check(RaidRules.AutoFormation(new BossDef { CostLimit = 10 }).Count == 10, "おまかせ配置: 上限 10 なら王将 + 歩 9 枚（入らない駒は飛ばす）");

        // 錬成できる駒 ＝ 初期配置の駒の種類（王将を除く）＋ 錬成駒の編成
        {
            RaidFormation setup = new RaidFormation { pieces = Plus(FP(PieceType.Pawn, 0, 6), FP(PieceType.Pawn, 1, 6), FP(PieceType.Rook, 7, 7)) };
            setup.forgePicks.Add(PieceType.Gunpowder);
            List<RaidForgeOption> list = RaidRules.ForgeRecipes(setup);
            Check(list.Select(r => $"{r.Type}:{r.Label}:{r.Cost}").SequenceEqual(new[] { "Rook:飛:10", "Pawn:歩:2", "Gunpowder:火薬:3" }), $"錬成: 飛 10・歩 2・火薬 3（{string.Join(" ", list.Select(r => r.Type + ":" + r.Cost))}）");
            BoardSnapshot s = Raid(Plus(FP(PieceType.Pawn, 0, 6)), 1, new[] { PieceType.Sniper });
            Check(s.recipes.Select(r => r.piece).SequenceEqual(new[] { PieceType.Pawn, PieceType.Sniper }), "錬成: 局面のレシピ（歩・狙撃手）");
            s.players[0].energy = 2;
            Check(BoardSimulator.CanApply(s, Own(MatchAction.Forge(PieceType.Pawn), P1)) && !BoardSimulator.CanApply(s, Own(MatchAction.Forge(PieceType.Gunpowder), P1)), "錬成: 歩は錬成できる・レシピにない火薬はできない");
            BoardSimulator.Apply(s, Own(MatchAction.Forge(PieceType.Pawn), P1));
            Check(Hand(s, P1, PieceType.Pawn) == 1 && s.players[0].energy == 0, "錬成: 歩が持ち駒に・エナジー 0（+2 はない）");
        }
        Check(Errors(KingOnly(), PieceType.Rook, PieceType.Gunpowder).Count == 0 && Errors(KingOnly(), PieceType.Pawn, PieceType.Lance).Count == 0, "錬成駒: 通常の駒も選べる");
        Check(Errors(KingOnly(), PieceType.King).Contains("王将 は錬成駒に選べません") && Errors(KingOnly(), PieceType.Kooni).Count > 0, "錬成駒: 王将・小赤鬼は選べない");
        Check(Errors(Plus(FP(PieceType.Pawn, 0, 6)), PieceType.Pawn).Contains("歩 は初期配置にあるので、錬成駒に選ばなくても錬成できます"), "錬成駒: 初期配置にある種類は選べない");
        {
            List<RaidForgeOption> opts = RaidRules.ForgePickOptions(Plus(FP(PieceType.Pawn, 0, 6), FP(PieceType.Sniper, 1, 8)));
            Check(opts.Select(o => o.Type).SequenceEqual(new[] { PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird, PieceType.Giant, PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook }), "錬成駒の候補: 火薬・狙撃手・鳥・巨人・歩〜飛");
            Check(opts.Where(o => o.Auto).Select(o => o.Type).SequenceEqual(new[] { PieceType.Sniper, PieceType.Pawn }), "錬成駒の候補: 初期配置にある種類は自動");
            Check(opts.First(o => o.Type == PieceType.Rook).Cost == 10 && opts.First(o => o.Type == PieceType.Sniper).Cost == 5, "錬成駒の候補: 飛 10・狙撃手 5");
            Check(RaidRules.PrunedForgePicks(Plus(FP(PieceType.Rook, 7, 7)), new[] { PieceType.Rook, PieceType.Gunpowder }).SequenceEqual(new[] { PieceType.Gunpowder }), "錬成駒: 初期配置に入った種類は外す");
        }
        {
            RaidFormation setup = new RaidFormation { pieces = Plus(FP(PieceType.Gunpowder, 0, 8), FP(PieceType.Pawn, 0, 6)) };
            setup.forgePicks.AddRange(new[] { PieceType.Rook, PieceType.Sniper });
            Check(RaidRules.ForgeRecipes(setup).Select(r => $"{r.Type}:{r.Cost}").SequenceEqual(new[] { "Pawn:2", "Gunpowder:3", "Rook:10", "Sniper:5" }), "錬成: 初期配置の火薬も自動・選んだ飛と狙撃手");
            BoardSnapshot s = Raid(Plus(FP(PieceType.Sniper, 1, 8)), 1, new[] { PieceType.SilverGeneral });
            Check(PAt(s, 1, 8).type == PieceType.Sniper && PAt(s, 1, 8).initial, "錬成: 初期配置の狙撃手は初期配置の駒");
            s.players[0].energy = 11;
            BoardSimulator.Apply(s, Own(MatchAction.Forge(PieceType.Sniper), P1));
            BoardSimulator.Apply(s, Own(MatchAction.Forge(PieceType.SilverGeneral), P1));
            Check(Hand(s, P1, PieceType.Sniper) == 1 && Hand(s, P1, PieceType.SilverGeneral) == 1 && s.players[0].energy == 0, "錬成: 狙撃手 5 ＋ 銀 6");
        }
        // 打つ・魔法・錬成・狙撃では +2 エナジーはない
        {
            BoardSnapshot s = Raid(null, 1, new[] { PieceType.Sniper });
            FreezeAi(s);
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Pawn)] = 1;
            Check(BoardSimulator.Apply(s, Own(MatchAction.Drop(PieceType.Pawn, new Vector2Int(1, 0)), P1)) && s.players[0].energy == 0, "打つ: +2 エナジーはない");
        }
    }

    // ------------------------------------------------------------
    //  状態の往復と決定性（boss.test.js「討伐任務：状態の往復と決定性」）
    // ------------------------------------------------------------
    static List<MatchAction> RandomRaid(uint seed, int ticks, out BoardSnapshot end)
    {
        BoardSnapshot s = Raid(RaidRules.AutoFormation(AKAONI), seed, new[] { PieceType.Gunpowder, PieceType.Sniper });
        GameRng r = new GameRng(seed * 13 + 5);
        List<MatchAction> log = new List<MatchAction>();
        for (int i = 0; i < ticks && !s.gameOver; i++)
        {
            if (r.Next() < 0.3)
            {
                List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
                if (legal.Count > 0)
                {
                    List<MatchAction> moves = legal.Where(a => a.type == MatchActionType.Move).ToList();
                    MatchAction a = (moves.Count > 0 && r.Next() < 0.75 ? r.Pick(moves) : r.Pick(legal)).Clone();
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

    static void TestRaidDeterminism()
    {
        // 予告中・暴れ回りの途中で JSON にして読み込み、続きが同じハッシュになる
        Func<BossState, int, string> keep = AKAONI.ChooseAttack;
        AKAONI.ChooseAttack = (b, now) => new[] { "rampage", "sweep", "rampage", "smash" }[now / 100 % 4];
        try
        {
            foreach (int at in new[] { 7 * Sec, 13 * Sec, 18 * Sec })
            {
                BoardSnapshot a = Raid(RaidRules.AutoFormation(AKAONI), 3);
                BoardSimulator.Advance(a, at);
                BoardSnapshot b = BoardSnapshot.FromJson(a.ToJson());
                Check(b != null && b.Diff(a, true).Count == 0 && PrototypeHashText(b) == PrototypeHashText(a), $"往復: {at} tick で JSON にして読み込んでも同じ（{(b == null ? "読めない" : string.Join(" / ", b.Diff(a, true).Take(3)))}）");
                bool same = true;
                for (int i = 0; i < 30 * Sec; i++)
                {
                    BoardSimulator.Tick(a);
                    BoardSimulator.Tick(b);
                    if (i % Sec == 0 && PrototypeHashText(b) != PrototypeHashText(a)) same = false;
                }
                Check(same && b.Diff(a, true).Count == 0, $"往復: {at} tick から 30 秒進めても同じ");
            }
        }
        finally { AKAONI.ChooseAttack = keep; }

        // 同じシードと同じ tick つきの行動の列なら、同じハッシュになる
        {
            List<MatchAction> log = RandomRaid(7, 2000, out _);
            Check(log.Count > 30, $"決定性: 行動が多い（{log.Count}）");
            BoardSnapshot s = Raid(RaidRules.AutoFormation(AKAONI), 7, new[] { PieceType.Gunpowder, PieceType.Sniper });
            int k = 0;
            bool ok = true;
            while (k < log.Count && ok)
            {
                while (k < log.Count && log[k].t == s.clockTick)
                {
                    MatchAction e = log[k].Clone();
                    e.hash = "";
                    e.t = -1;
                    if (!BoardSimulator.Apply(s, e) || MatchHash.Fnv1a(PrototypeHashText(s)) != log[k].hash) { ok = false; break; }
                    k++;
                }
                if (s.clockTick % 97 == 0) s = BoardSnapshot.FromJson(s.ToJson()).Clone();
                BoardSimulator.Tick(s);
            }
            Check(ok && k == log.Count, $"決定性: {log.Count} 手が同じハッシュ（{k} 手）");
        }

        // ランダムな操作で最後まで（または上限まで）：状態の整合性が崩れない
        int over = 0, cleared = 0;
        foreach (uint seed in new uint[] { 1, 2, 3, 4, 5, 6 })
        {
            RandomRaid(seed, MatchPolicy.SecToTicks(400f), out BoardSnapshot s);
            if (s.gameOver) over++;
            if (s.gameOver && s.winner == 0) cleared++;
            Invariants(s, $"ランダムな討伐任務 {seed}");
            Check(!s.pieces.Any(p => p.Footprint().Any(c => s.BossAt(c.x, c.y))), $"ランダムな討伐任務 {seed}: ボスのマスに駒はいない");
            Check(s.boss.hp >= 0 && s.boss.x >= 0 && s.boss.x + 3 <= 9 && s.boss.y >= 0 && s.boss.y + 3 <= 9, $"ランダムな討伐任務 {seed}: ボスは盤内・HP 0 以上");
            if (s.gameOver) Check(BoardSimulator.RaidOutcomeOf(s) != RaidOutcome.None, $"ランダムな討伐任務 {seed}: 終局なら勝敗が決まっている");
            Check(s.players[1].energy == 0 && s.players[1].hand.All(h => h == 0), $"ランダムな討伐任務 {seed}: ボスの席はエナジー・持ち駒を持たない");
        }
        Check(over > 0, $"ランダムな討伐任務: 終局する対局がある（{over} / 6・討伐成功 {cleared}）");
        Console.WriteLine($"raid random: 6 局のうち {over} 局が終局（討伐成功 {cleared}）");
    }

    // ------------------------------------------------------------
    //  小赤鬼号令と小赤鬼（boss.test.js「小赤鬼号令と小赤鬼（ボスの手下）」）
    // ------------------------------------------------------------
    static void TestMinions()
    {
        string SpawnKey = PKey((8, 0), (8, 1), (8, 2), (0, 0), (0, 1), (0, 2)); // 1一・1二・1三・9一・9二・9三
        // 予告 4 秒のあと小赤鬼が現れる。いた駒は攻撃を受ける
        using (Fixed("summon"))
        {
            BoardSnapshot s = RaidWith((PieceType.SilverGeneral, 8, 1), (PieceType.GoldGeneral, 0, 2));
            PAt(s, 0, 2).shield = true;
            List<BossEvent> ev = AdvanceEv(s, 5 * Sec);
            BossEvent tel = ev.FirstOrDefault(e => e.Type == BossEventType.Telegraph);
            Check(tel != null && tel.Attack == "summon" && tel.Sec == 4f && PKey(tel.Cells) == SpawnKey, "小赤鬼号令: 予告 4 秒・呼び出す 6 マス");
            ev = AdvanceEv(s, 4 * Sec);
            Check(ev.First(e => e.Type == BossEventType.Attack && e.Attack == "summon").Hits == 2, "小赤鬼号令: いた駒 2 枚は攻撃を受ける");
            Check(ev.Count(e => e.Type == BossEventType.MinionSpawned) == 5 && PAt(s, 0, 2).type == PieceType.GoldGeneral, "小赤鬼号令: 盾で残った金のマスには出ない（5 体）");
            List<SnapshotPiece> ms = BossEngine<SnapshotPiece>.Minions(Host(s));
            Check(PKey(ms.Select(m => new MatchSquare(m.x, m.y))) == PKey((0, 0), (0, 1), (8, 0), (8, 1), (8, 2)) && ms.All(m => m.type == PieceType.Kooni && m.Owner == P2 && m.minion), "小赤鬼号令: ボスの席の小赤鬼");
            Check(ms.Select(m => m.minionSerial).SequenceEqual(new[] { 0, 1, 2, 3, 4 }) && s.raid.minionSerial == 5, "小赤鬼号令: 現れた順の番号");
            Check(s.boss.ai.phase == BossAiState.Cooldown && s.boss.ai.last == "summon", "小赤鬼号令: 終わったら待ち");
        }
        // ボスのマス・破壊マス・小赤鬼がいるマスには呼び出さない。盤上は max 体まで
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            s.boss.y = 6; // ボスが 1 筋側にいる（Prototype の x = 6）
            PCell(s, 0, 0).state = CellState.Broken;
            SpawnP(s, 0, 1);
            Check(PKey(Cells(s, BossAttacks.Summon)) == "0,2", $"小赤鬼号令: 呼び出せるのは 9三 だけ（{PKey(Cells(s, BossAttacks.Summon))}）");
            int keep = BossAttacks.SummonMax;
            BossAttacks.SummonMax = 1;
            try
            {
                using (Fixed("summon"))
                {
                    BoardSnapshot s2 = Raid();
                    BoardSimulator.Advance(s2, 9 * Sec);
                    Check(BossEngine<SnapshotPiece>.Minions(Host(s2)).Count == 1, "小赤鬼号令: 盤上は max 体まで");
                }
            }
            finally { BossAttacks.SummonMax = keep; }
        }
        // 3 秒ごとに 8 方向へ 1 マス動く（行き先は 1 秒前に決めて表示）。王将に近づく
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece m = SpawnP(s, 4, 4);
            BoardSimulator.Advance(s, 2 * Sec - 1);
            Check(!m.minionPlanned, "小赤鬼: 2 秒たつまで行き先を決めない");
            BoardSimulator.Advance(s, 1);
            Check(m.minionPlanned && m.minionHasTo && m.minionToX == 8 - 5 && m.minionToY == 4, "小赤鬼: 1 秒前に行き先（王将へまっすぐ）");
            BoardSimulator.Advance(s, Sec - 1);
            Check(PAt(s, 4, 4) == m, "小赤鬼: 3 秒たつまで動かない");
            List<BossEvent> ev = AdvanceEv(s, 1);
            Check(PAt(s, 4, 5) == m && ev.Any(e => e.Type == BossEventType.MinionStep && e.From == PS(4, 4) && e.To == PS(4, 5)), "小赤鬼: 3 秒で 1 マス");
            Check(!m.minionPlanned && m.minionNext == 6 * Sec, "小赤鬼: 次は 6 秒");
            BoardSimulator.Advance(s, 3 * Sec);
            Check(PAt(s, 4, 6) == m, "小赤鬼: 6 秒でもう 1 マス");
        }
        // 隣にプレイヤーの駒がいれば襲う：盾は割れる／なければ撃破してそのマスへ進む／王将は被弾
        {
            BoardSnapshot s = RaidWith((PieceType.Pawn, 1, 4));
            FreezeAi(s);
            SnapshotPiece m = SpawnP(s, 0, 4);
            SnapshotPiece fu = PAt(s, 1, 4);
            fu.shield = true;
            List<BossEvent> ev = AdvanceEv(s, 3 * Sec);
            Check(ev.Any(e => e.Type == BossEventType.MinionStrike) && !fu.shield && PAt(s, 0, 4) == m, "小赤鬼: 襲う（盾は割れ、駒が残ったので進まない）");
            BoardSimulator.Advance(s, 3 * Sec);
            Check(PAt(s, 1, 4) == m && !s.Contains(fu) && Hand(s, P1, PieceType.Pawn) + Hand(s, P2, PieceType.Pawn) == 0, "小赤鬼: 撃破してそのマスへ進む（持ち駒にならない）");
            BoardSnapshot s2 = Raid();
            FreezeAi(s2);
            SpawnP(s2, 4, 7);
            BoardSimulator.Advance(s2, 3 * Sec);
            Check(s2.raid.kingHits == 1 && PAt(s2, 4, 8) != null && PAt(s2, 4, 8).type == PieceType.King, "小赤鬼: 王将は被弾（その場に残る）");
        }
        // 行き先が塞がったら、その回は動かない。炎上・落とし穴のマスは選ばない
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece m = SpawnP(s, 4, 4);
            foreach ((int x, int y) in new[] { (3, 5), (4, 5), (5, 5) }) { PCell(s, x, y).state = CellState.Burning; PCell(s, x, y).burnUntil = 1000000000; }
            BoardSimulator.Advance(s, 2 * Sec);
            Check(m.minionHasTo && 8 - m.minionToX < 5, "小赤鬼: 炎上のマスは選ばない");
            BoardSnapshot s2 = Raid();
            FreezeAi(s2);
            SnapshotPiece m2 = SpawnP(s2, 4, 4);
            BoardSimulator.Advance(s2, 2 * Sec);
            SpawnP(s2, 4, 5); // 先に別の小赤鬼が入った
            BoardSimulator.Advance(s2, Sec);
            Check(PAt(s2, 4, 4) == m2 && !m2.minionPlanned, "小赤鬼: 行き先が塞がったら動かない");
            BoardSnapshot s3 = Raid();
            FreezeAi(s3);
            SnapshotPiece m3 = SpawnP(s3, 4, 4);
            BoardSimulator.Advance(s3, 2 * Sec);
            s3.MovePiece(m3, 8 - 1, 1); // 行き先を決めたあとに押し出されて、行き先が隣でなくなった
            BoardSimulator.Advance(s3, Sec);
            Check(PAt(s3, 1, 1) == m3, "小赤鬼: 行き先が隣でなくなったら動かない");
        }
        // プレイヤーの駒で取れる（持ち駒にならない）・狙撃できる。ボスの攻撃は小赤鬼に当たらず、暴れ回りでは押し出される
        {
            BoardSnapshot s = RaidWith((PieceType.Rook, 4, 6), (PieceType.Sniper, 0, 8));
            FreezeAi(s);
            SnapshotPiece a = SpawnP(s, 4, 4), b = SpawnP(s, 7, 4);
            Check(PieceRules.IsMinion(a.type) && BoardSimulator.CanMoveTo(s, PAt(s, 4, 6), 8 - 4, 4), "小赤鬼: 飛で取りに行ける");
            Check(BoardSimulator.Apply(s, PMove(s, 4, 6, 4, 4)) && !s.Contains(a) && s.players[0].hand.All(h => h == 0), "小赤鬼: 取れる（持ち駒にならない）");
            Check(s.players[0].energy == 2 && s.players[1].energy == 0, "小赤鬼: 取ってもエナジーは移動の +2 だけ");
            Check(BoardSimulator.SnipeTargets(s, PAt(s, 0, 8)).Contains(PS(7, 4)), "小赤鬼: 狙撃できる");
            BoardSnapshot s2 = Raid();
            FreezeAi(s2);
            SnapshotPiece c = SpawnP(s2, 4, 3);
            BossEngine<SnapshotPiece>.HitCells(Host(s2), new List<MatchSquare> { PS(4, 3) }, "sweep");
            Check(s2.Contains(c), "小赤鬼: ボスの攻撃は当たらない");
            MoveBossP(s2, 0, 1, "rampage");
            Check(s2.Contains(c) && !s2.BossAt(c.x, c.y), "小赤鬼: 暴れ回りでは押し出される");
        }
        // 行き先の選び方は状態だけで決まる（JSON にして読み込んでも同じ）
        {
            BoardSnapshot s = Raid(RaidRules.AutoFormation(AKAONI), 9);
            FreezeAi(s);
            foreach ((int x, int y) in new[] { (0, 3), (8, 3), (4, 4) }) SpawnP(s, x, y);
            BoardSimulator.Advance(s, 4 * Sec);
            BoardSnapshot c = BoardSnapshot.FromJson(s.ToJson());
            Check(PrototypeHashText(c) == PrototypeHashText(s), "小赤鬼: JSON の往復で同じハッシュ");
            for (int i = 0; i < 20 * Sec; i++) { BoardSimulator.Tick(s); BoardSimulator.Tick(c); }
            Check(PrototypeHashText(c) == PrototypeHashText(s) && c.Diff(s, true).Count == 0, "小赤鬼: 20 秒進めても同じ");
        }
    }

    // ------------------------------------------------------------
    //  討伐任務の超常魔法（raid-transcendent.test.js のうち、シミュレーションで確かめられる分）
    // ------------------------------------------------------------
    static void TestRaidTranscendent()
    {
        // 対局の開始時点から解放済み。エナジーが足りればすぐ使える。1 対局に 1 回だけ
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "TimeStop" });
            Check(s.players[0].transcendentUnlocked, "超常魔法: 最初から解放済み");
            Check(!BoardSimulator.CanApply(s, Own(Mg("TimeStop"), P1)), "超常魔法: エナジー不足なら使えない");
            s.players[0].energy = 100;
            Check(BoardSimulator.Apply(s, Own(Mg("TimeStop"), P1)) && s.timeStopped && s.timeStopRemaining == -1, "停止世界: 使える（回数の上限なし）");
            BoardSimulator.Tick(s);
            Check(BoardSimulator.ApplyForSearch(s, Own(MatchAction.ReleaseTimeStop(), P1)) && !s.timeStopped, "停止世界: 解除できる");
            Check(!BoardSimulator.CanApply(s, Own(Mg("TimeStop"), P1)), "超常魔法: 1 対局に 1 回だけ");
        }
        // 基本対戦（合戦将棋型）の解放条件は今までどおり（最初は未解放）
        {
            BoardSnapshot v = Kassen();
            Check(!v.players[0].transcendentUnlocked && !v.players[1].transcendentUnlocked, "超常魔法: 基本対戦は最初は未解放");
        }
        // 行動回数の上限がない：TimeStopMoves 回を超えて動ける。TimeStopSec 秒で時間が動き出す
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "TimeStop" });
            FreezeAi(s);
            s.players[0].energy = 30;
            BoardSimulator.Apply(s, Own(Mg("TimeStop"), P1));
            int castTick = s.clockTick, time = s.clockTime;
            bool all = true;
            for (int i = 0; i < MatchConfig.TimeStopMoves + 5; i++)
            {
                BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.PieceCooldownSec));
                int x = i % 2 == 1 ? 3 : 4;
                if (!BoardSimulator.ApplyForSearch(s, PMove(s, x, 8, 7 - x, 8))) all = false;
            }
            Check(all && s.timeStopped, "停止世界: 10 回を超えて動ける");
            while (s.timeStopped) BoardSimulator.Tick(s);
            Check(s.clockTick - castTick == MatchPolicy.SecToTicks(K.Durations.TimeStopSec) && s.clockTime == time, "停止世界: 発動から 10 秒で解除・対局の時間は進まない");
        }
        // 時間停止中：ボスの AI・手下・時間ごとのエナジーが止まる
        {
            BoardSnapshot s = Raid(null, 1, null, new[] { "Shield", "TimeStop" });
            s.players[0].energy = 30;
            BoardSimulator.Advance(s, Sec * 5);
            Check(s.boss.ai.phase == BossAiState.Telegraph, "停止世界: 5 秒で予告が始まっている");
            SnapshotPiece oni = SpawnP(s, 0, 3);
            oni.minionNext = s.clockTime + Sec;
            BoardSimulator.Apply(s, Own(Mg("TimeStop"), P1));
            int e0 = s.players[0].energy;
            string ai = BoardSnapshot.BossKey(s.boss);
            List<BossEvent> ev = AdvanceEv(s, MatchPolicy.SecToTicks(K.Durations.TimeStopSec) - 1);
            Check(s.timeStopped && s.players[0].energy == e0 && BoardSnapshot.BossKey(s.boss) == ai && PAt(s, 0, 3) == oni && ev.Count == 0, "停止世界: ボス・手下・エナジーが止まる");
            BoardSimulator.Advance(s, 2);
            Check(!s.timeStopped, "停止世界: 10 秒で時間が動き出す");
            BoardSimulator.Advance(s, Sec);
            Check(s.players[0].energy == e0 + 1, "停止世界: 動き出すとエナジーも進む");
        }
        // アンデッド・パラダイス：アンデッドは相手の王将の代わりにボスへ進み、届けばボスを攻撃する
        {
            BoardSnapshot s = Raid();
            FreezeAi(s);
            SnapshotPiece u = PPut(s, PieceType.Rook, 4, 6);
            u.undead = true;
            BoardSimulator.Advance(s, MatchPolicy.SecToTicks(K.Durations.AutoActSec));
            Check(s.boss.hp == 27 && PAt(s, 4, 6) == u && u.moveCount == 1, $"アンデッド: ボスのマスへ入る手が最優先（飛のダメージ 3・跳ね返る）（HP {s.boss.hp}）");
            BoardSnapshot t = Raid();
            FreezeAi(t);
            SnapshotPiece g = PPut(t, PieceType.GoldGeneral, 0, 7);
            g.undead = true;
            BoardSimulator.Advance(t, MatchPolicy.SecToTicks(K.Durations.AutoActSec));
            Check(!t.Contains(g) || (8 - g.x) < 7, "アンデッド: ボスへ近づく");
        }
    }

    // ------------------------------------------------------------
    //  MatchHash・MatchSnapshot の討伐任務の項目（JSON の往復）
    // ------------------------------------------------------------
    static void TestRaidHashAndState()
    {
        BoardSnapshot b = Raid(RaidRules.AutoFormation(AKAONI), 5);
        using (Fixed("summon"))
        {
            BoardSimulator.Advance(b, 11 * Sec);
        }
        BoardSnapshot bb = BoardSnapshot.FromJson(b.ToJson());
        Check(bb != null && bb.HasBoss && bb.IsRaid && bb.Diff(b, true).Count == 0 && bb.pieces.Count(p => p.minion) == 6, "BoardSnapshot: ボス・討伐任務・手下が JSON で往復する");
        BoardSnapshot v = Kassen();
        BoardSnapshot vv = BoardSnapshot.FromJson(v.ToJson());
        Check(vv != null && !vv.HasBoss && !vv.IsRaid && vv.mode == "versus", "BoardSnapshot: 基本対戦にはボスがいない");
        Check(BoardSnapshot.FromJson("{\"version\":1}") is BoardSnapshot old && !old.HasBoss && !old.IsRaid && old.mode == "versus", "BoardSnapshot: 項目のない JSON は基本対戦");

        // 局面（MatchSnapshot）のハッシュ：討伐任務はモード・ボス・王将の被弾・手下を足す。基本対戦は今までと同じ
        MatchSnapshot m = new MatchSnapshot();
        m.players.Add(new PlayerSnapshot { owner = 0 });
        m.players.Add(new PlayerSnapshot { owner = 1 });
        m.ruleset = "kassen";
        m.clock = new ClockSnapshot { active = true, tick = 260, time = 240 };
        m.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.King, owner = 0, x = 0, y = 4 });
        string versus = MatchHash.BuildText(m);
        Check(versus.EndsWith(",[\"versus\",\"kassen\",null],[260,240],[]]"), $"MatchHash: 基本対戦は討伐任務の項目を足さない（{versus.Substring(Math.Max(0, versus.Length - 60))}）");
        m.mode = "bossRaid";
        m.boss = BossState.Create(AKAONI);
        m.boss.x = 5; m.boss.y = 2; m.boss.hp = 21; m.boss.facingX = 0; m.boss.facingY = 1;
        m.boss.ai.phase = BossAiState.Telegraph; m.boss.ai.until = 300; m.boss.ai.attack = "sweep"; m.boss.ai.last = "smash";
        m.raid = new RaidState { active = true, kingHits = 1 };
        string raid = MatchHash.BuildText(m);
        Check(raid.EndsWith(",[\"bossRaid\",\"kassen\",\"akaoni\"],[260,240],[],[\"akaoni\",5,2,21,0,1,\"telegraph\",300,\"sweep\",\"smash\"],[1]]"),
            $"MatchHash: 討伐任務はモード・ボス・王将の被弾を足す（{raid.Substring(Math.Max(0, raid.Length - 110))}）");
        m.pieces.Add(new PieceSnapshot { id = 2, type = PieceType.Kooni, owner = 1, x = 7, y = 8, minion = true, minionSerial = 2, minionNext = 280, minionPlanned = true, minionHasTo = true, minionToX = 6, minionToY = 7 });
        m.pieces.Add(new PieceSnapshot { id = 3, type = PieceType.Kooni, owner = 1, x = 6, y = 0, minion = true, minionSerial = 1, minionNext = 290 });
        string mins = MatchHash.BuildText(m);
        Check(mins.EndsWith(",[1],[[6,0,290,null],[7,8,280,[6,7]]]]"), $"MatchHash: 手下がいるときだけ [x, y, 次に動く時刻, 行き先] を足す（{mins.Substring(Math.Max(0, mins.Length - 60))}）");
        m.raid.kingHits = 2;
        Check(MatchHash.Compute(m) != MatchHash.Fnv1a(mins), "MatchHash: 王将の被弾が違えば違うハッシュ");
        MatchSnapshot back = MatchSnapshot.FromJson(m.ToJson());
        Check(back != null && back.mode == "bossRaid" && back.boss.active && back.boss.hp == 21 && back.raid.kingHits == 2
              && back.pieces.Count(p => p.minion) == 2 && MatchHash.Compute(back) == MatchHash.Compute(m), "MatchSnapshot: ボス・王将の被弾・手下が JSON で往復し、ハッシュも同じ");
    }

    // ------------------------------------------------------------
    //  Prototype の棋譜（tests/kifus/raid-akaoni-*.json）の再生
    // ------------------------------------------------------------
    static void TestRaidKifus()
    {
        foreach (string name in new[]
                 {
                     "raid-akaoni-1.json", "raid-akaoni-2.json", "raid-akaoni-3.json", "raid-akaoni-4.json",
                     "raid-daishogun-1.json", "raid-daishogun-2.json", "raid-daishogun-3.json", "raid-daishogun-4.json",
                 })
        {
            string path = FindKifu(name);
            Check(path != null, $"Prototype の棋譜 {name} がある（Tests/SnapshotTests/PrototypeKifus）");
            if (path == null) continue;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement k = doc.RootElement;
            JsonElement cfg = k.GetProperty("config");
            BossDef kifuBoss = BossDefs.ById(cfg.GetProperty("bossId").GetString());
            Check(cfg.GetProperty("mode").GetString() == "bossRaid" && cfg.GetProperty("ruleset").GetString() == "kassen" && kifuBoss != null
                  && kifuBoss.Id == (name.Contains("daishogun") ? "daishogun" : "akaoni"), $"{name}: 討伐任務・合戦将棋型・{(kifuBoss != null ? kifuBoss.Name : "?")}");
            if (kifuBoss == null) continue;

            // 編成（Prototype の setup.formation・forgePicks）と魔法（先手だけ。normal・ultimate・transcendent の順）
            RaidFormation f = new RaidFormation();
            JsonElement setup = k.GetProperty("setup");
            foreach (JsonElement e in setup.GetProperty("formation").EnumerateArray())
            {
                f.pieces.Add(FP(ProtoPieces[e.GetProperty("type").GetString()], e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32()));
            }
            foreach (JsonElement e in setup.GetProperty("forgePicks").EnumerateArray()) f.forgePicks.Add(ProtoPieces[e.GetString()]);
            List<SnapshotSkill> deck = new List<SnapshotSkill>();
            JsonElement lo = k.GetProperty("loadouts")[0];
            foreach (JsonElement id in lo.GetProperty("normal").EnumerateArray()) deck.Add(ProtoSkills[id.GetString()]);
            foreach (string rank in new[] { "ultimate", "transcendent" })
            {
                if (lo.TryGetProperty(rank, out JsonElement v) && v.ValueKind == JsonValueKind.String) deck.Add(ProtoSkills[v.GetString()]);
            }
            uint seed = k.GetProperty("seed").GetUInt32();
            BoardSnapshot s = Kassen(seed);
            s.players[0].skills = deck;
            BoardSimulator.SetupRaid(s, kifuBoss, f);

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
                // 途中で JSON を往復させても同じ（BoardSnapshot のボス・討伐任務・手下の項目）
                if (s.clockTick % 101 == 0) s = BoardSnapshot.FromJson(s.ToJson());
                BoardSimulator.Tick(s);
            }
            Check(error == null, $"{name}: {error}");
            Check(matched == n, $"{name}: 全 {n} 手のハッシュが Prototype と一致（{matched} 手）");
            int endTick = k.GetProperty("endTick").GetInt32();
            Console.WriteLine($"prototype kifu {name}: {matched} / {n} 手のハッシュが一致（seed {seed}・最後の tick {s.clockTick} / {endTick}・ボス HP {s.boss.hp}・王将の被弾 {s.raid.kingHits}・{(s.gameOver ? (s.winner == 0 ? "討伐成功" : "討伐失敗") : "続行中")}）");
        }
    }
}
