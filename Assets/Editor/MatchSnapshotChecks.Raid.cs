using System.Collections.Generic;
using UnityEngine;

// 討伐任務（ボスバトル）の確認：局面の書き出し・読み込み（ボス・王将の被弾・手下・編成）、盤面のハッシュ、棋譜の版 2、ルール説明。
// MatchSnapshotChecks の続き（EditMode テストとメニュー「UltimateShogi/局面の書き出しを確かめる」から呼ばれる）。
// ボスの AI・攻撃・ダメージ・Prototype の棋譜の再生は、Unity の外のテスト（Tests/SnapshotTests の BossTests.cs）で確かめる
public static partial class MatchSnapshotChecks
{
    static RaidFormation SampleFormation()
    {
        RaidFormation f = new RaidFormation { pieces = RaidRules.AutoFormation(BossDefs.Akaoni) };
        f.forgePicks.Add(PieceType.Gunpowder);
        f.forgePicks.Add(PieceType.Giant);
        return f;
    }

    // 討伐任務の局面：ボス（位置・HP・向き・AI の予告の範囲）・王将の被弾・手下の状態・編成が、書き出し → JSON → 読み込み → 書き出しで戻り、
    // ハッシュも同じ。ハッシュにモード・ボス・王将の被弾・手下が入る。基本対戦の対局・別のモードには読み込めない
    public static void CheckRaidStateRoundTrip()
    {
        using (Fixture f = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
        {
            f.match.Turn.BeginRealtime();
            f.match.SetupRaid(BossDefs.Akaoni, SampleFormation());
            True(f.match.HasBoss && f.match.Policy.IsRaid, "討伐任務の MatchState にボスがいない");
            True(f.match.IsTranscendentUnlocked(PlayerType.Player1) && !f.match.IsTranscendentUnlocked(PlayerType.Player2), "超常魔法は、プレイヤーの席だけ最初から解放済み");
            f.match.Clock.Tick = 260;
            f.match.Clock.Time = 240;

            BossState b = f.match.Boss;
            b.x = 5;
            b.y = 2;
            b.hp = 21;
            b.facingX = 0;
            b.facingY = 1;
            b.ai.phase = BossAiState.Telegraph;
            b.ai.until = 300;
            b.ai.attack = "sweep";
            b.ai.last = "smash";
            b.ai.hasCells = true;
            b.ai.cells = new List<MatchSquare> { new MatchSquare(4, 1), new MatchSquare(4, 2), new MatchSquare(4, 3) };
            f.match.Raid.kingHits = 1;
            f.match.Raid.minionSerial = 3;

            // 駒の番号（serial）は作った順（対局では GameBoard が振る。ここでは手で振る）
            GamePiece king = f.NewPiece(PieceType.King, PlayerType.Player1, 0, 4);
            king.IsInitial = true;
            king.serial = 1;
            GamePiece pawn = f.NewPiece(PieceType.Pawn, PlayerType.Player1, 2, 4);
            pawn.IsInitial = true;
            pawn.serial = 2;
            GamePiece minion = f.NewPiece(PieceType.Kooni, PlayerType.Player2, 7, 8);
            minion.serial = 3;
            minion.minion = new MinionData { Active = true, Serial = 2, Next = 280, Planned = true, HasTo = true, ToX = 6, ToY = 7 };
            f.match.Raid.nextPieceId = 4;

            MatchSnapshot s = f.Export();
            Equal("bossRaid", s.mode, "局面のモード");
            True(s.boss != null && s.boss.active && s.boss.id == "akaoni" && s.boss.hp == 21 && s.boss.x == 5 && s.boss.y == 2, "局面のボス");
            True(s.raid != null && s.raid.active && s.raid.kingHits == 1 && s.raid.minionSerial == 3, "局面の王将の被弾");
            True(s.formation != null && s.formation.pieces.Count == 20 && s.formation.forgePicks.Count == 2, "局面の編成");
            PieceSnapshot ms = s.pieces.Find(p => p.type == PieceType.Kooni);
            True(ms != null && ms.minion && ms.minionSerial == 2 && ms.minionNext == 280 && ms.minionHasTo && ms.minionToX == 6 && ms.minionToY == 7, "局面の手下の状態");
            True(MatchSnapshotIO.Validate(s) == null, "討伐任務の局面が Validate を通らない：" + MatchSnapshotIO.Validate(s));

            string text = MatchHash.BuildText(s);
            True(text.Contains("[\"bossRaid\",\"kassen\",\"akaoni\"]"), "ハッシュにモード・戦闘型・ボスが入る");
            True(text.Contains("[\"akaoni\",5,2,21,0,1,\"telegraph\",300,\"sweep\",\"smash\"]"), "ハッシュにボスの位置・HP・向き・AI が入る");
            True(text.Contains("[[7,8,280,[6,7]]]"), "ハッシュに手下の次の行き先が入る");
            True(text.EndsWith("[1],[[7,8,280,[6,7]]]]"), "赤鬼王のハッシュに、防壁・軍銃兵・照準の項目を足さない（今までの棋譜のハッシュを変えない）");
            True(s.pieces.Find(p => p.type == PieceType.King).serial == 1 && s.raid.nextPieceId == 4, "局面の駒の番号");
            string before = MatchHash.Compute(s);

            using (Fixture g = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
            {
                g.match.SetupRaid(BossDefs.Akaoni, new RaidFormation { pieces = RaidRules.AutoFormation(BossDefs.Akaoni) });
                True(MatchSnapshotIO.RulesetMismatch(s, g.match) == null, "討伐任務の局面を、同じボスの討伐任務の対局に読み込めない");
                MatchSnapshot back = g.Load(MatchSnapshot.FromJson(s.ToJson()));
                Equal(s.ToJson(), back.ToJson(), "書き出し → 読み込み → 書き出しで JSON が変わった（討伐任務）");
                Equal(before, MatchHash.Compute(back), "読み込んだあとのハッシュ（討伐任務）");
                True(g.match.Boss.hp == 21 && g.match.Boss.ai.phase == BossAiState.Telegraph && g.match.Boss.ai.cells.Count == 3, "読み込んだあとのボス");
                True(g.match.Raid.kingHits == 1, "読み込んだあとの王将の被弾");
                True(g.match.Formation.forgePicks.Count == 2, "読み込んだあとの編成（錬成駒）");
                GamePiece q = g.board[GameHelper.CalcPanelNum(7, 8)];
                True(q != null && q.type == PieceType.Kooni && q.minion.Active && q.minion.Serial == 2 && q.minion.ToX == 6, "読み込んだあとの手下");
                True(q.serial == 3 && g.match.Raid.nextPieceId == 4, "読み込んだあとの駒の番号");
            }

            // ボスの HP・王将の被弾が変わればハッシュも変わる
            b.hp = 20;
            True(MatchHash.Compute(f.Export()) != before, "ボスの HP が変わってもハッシュが同じ");
            b.hp = 21;
            f.match.Raid.kingHits = 2;
            True(MatchHash.Compute(f.Export()) != before, "王将の被弾が変わってもハッシュが同じ");
            f.match.Raid.kingHits = 1;
            Equal(before, MatchHash.Compute(f.Export()), "元に戻したらハッシュも戻る");

            // 基本対戦の対局には読み込めない・ボスのいない討伐任務の局面は通らない
            using (Fixture h = new Fixture(BattleRulesets.Kassen))
            {
                True(MatchSnapshotIO.RulesetMismatch(s, h.match) != null, "討伐任務の局面を基本対戦の対局に読み込めてしまう");
            }
            MatchSnapshot noBoss = MatchSnapshot.FromJson(s.ToJson());
            noBoss.boss.active = false;
            True(MatchSnapshotIO.Validate(noBoss) != null, "ボスのいない討伐任務の局面が Validate を通った");
        }
    }

    // 大将軍の局面：陣の駒（軍銃兵の照準・次に撃つ時刻、防壁の HP、軍歩兵の動き）・弓の照準・駒の番号が、書き出し → JSON → 読み込み → 書き出しで戻り、
    // ハッシュも同じ。ハッシュに防壁の HP・軍銃兵の照準・弓の照準が入る。番号のない局面を読むと、読み込んだ順に番号を振る
    public static void CheckDaishogunStateRoundTrip()
    {
        using (Fixture f = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
        {
            f.match.Turn.BeginRealtime();
            BossDef def = BossDefs.Daishogun;
            True(def.Implemented && BossDefs.Validate(def).Count == 0, "大将軍の定義が正しくない");
            Equal(21, def.Enemies.Length, "大将軍の陣の駒の数（軍銃兵 6・防壁 6・軍歩兵 9）");
            f.match.SetupRaid(def, new RaidFormation { pieces = RaidRules.AutoFormation(def) });
            f.match.Clock.Tick = 230;
            f.match.Clock.Time = 220;

            BossState b = f.match.Boss;
            Equal("daishogun", b.id, "ボスの Id");
            b.ai.phase = BossAiState.Telegraph;
            b.ai.until = 300;
            b.ai.attack = "bow";
            b.ai.last = "guard";
            b.ai.hasCells = true;
            b.ai.cells = new List<MatchSquare> { new MatchSquare(0, 4) };
            b.ai.aim = new AimData { active = true, target = 1, hasCell = true, x = 0, y = 4, locked = true };

            GamePiece king = f.NewPiece(PieceType.King, PlayerType.Player1, 0, 4);
            king.IsInitial = true;
            king.serial = 1;
            GamePiece gun = f.NewPiece(PieceType.GunJu, PlayerType.Player2, 8, 0);
            gun.serial = 2;
            gun.minion = new MinionData { Serial = 0, Gunner = true, GunNext = 320, Aim = new AimData { active = true, target = 1, hasCell = true, x = 0, y = 4 } };
            GamePiece wall = f.NewPiece(PieceType.Bouheki, PlayerType.Player2, 7, 0);
            wall.serial = 3;
            wall.hp = 2;
            wall.minion = new MinionData { Serial = 1 };
            GamePiece foot = f.NewPiece(PieceType.GunFu, PlayerType.Player2, 5, 3);
            foot.serial = 4;
            foot.minion = new MinionData { Active = true, Serial = 2, Next = 240, Planned = true, HasTo = true, ToX = 4, ToY = 3 };
            f.match.Raid.minionSerial = 3;
            f.match.Raid.nextPieceId = 5;

            MatchSnapshot s = f.Export();
            True(MatchSnapshotIO.Validate(s) == null, "大将軍の局面が Validate を通らない：" + MatchSnapshotIO.Validate(s));
            PieceSnapshot gs = s.pieces.Find(p => p.type == PieceType.GunJu);
            True(gs != null && gs.gunner && !gs.minion && gs.gunNext == 320 && gs.aim.active && gs.aim.target == 1 && gs.aim.x == 0 && gs.aim.y == 4 && !gs.aim.locked, "局面の軍銃兵の照準");
            PieceSnapshot ws = s.pieces.Find(p => p.type == PieceType.Bouheki);
            True(ws != null && ws.hp == 2 && !ws.minion && !ws.gunner && ws.minionSerial == 1, "局面の防壁の HP");
            True(s.boss.ai.aim.active && s.boss.ai.aim.locked && s.boss.ai.aim.target == 1, "局面の弓の照準");

            string text = MatchHash.BuildText(s);
            True(text.Contains("[\"bossRaid\",\"kassen\",\"daishogun\"]"), "ハッシュにモード・戦闘型・ボスが入る（大将軍）");
            True(text.Contains("[[5,3,240,[4,3]]]"), "ハッシュに軍歩兵の次のマスが入る");
            True(text.EndsWith(",[[[7,0,2]],[[8,0,320,[1,[0,4],0]]],[[1,[0,4],1],[[0,4]]]]]"),
                "ハッシュに防壁の HP・軍銃兵の照準・弓の照準が入る：" + text.Substring(System.Math.Max(0, text.Length - 90)));
            string before = MatchHash.Compute(s);

            using (Fixture g = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
            {
                g.match.SetupRaid(def, new RaidFormation { pieces = RaidRules.AutoFormation(def) });
                MatchSnapshot back = g.Load(MatchSnapshot.FromJson(s.ToJson()));
                Equal(s.ToJson(), back.ToJson(), "書き出し → 読み込み → 書き出しで JSON が変わった（大将軍）");
                Equal(before, MatchHash.Compute(back), "読み込んだあとのハッシュ（大将軍）");
                GamePiece q = g.board[GameHelper.CalcPanelNum(8, 0)];
                True(q != null && q.type == PieceType.GunJu && q.minion.Gunner && q.minion.GunNext == 320 && q.minion.Aim.target == 1 && q.serial == 2, "読み込んだあとの軍銃兵");
                GamePiece w = g.board[GameHelper.CalcPanelNum(7, 0)];
                True(w != null && w.type == PieceType.Bouheki && w.hp == 2 && w.serial == 3, "読み込んだあとの防壁");
                True(g.match.Boss.ai.aim.active && g.match.Boss.ai.aim.locked && g.match.Raid.nextPieceId == 5, "読み込んだあとの弓の照準・駒の番号");
            }
            using (Fixture a = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
            {
                a.match.SetupRaid(BossDefs.Akaoni, new RaidFormation { pieces = RaidRules.AutoFormation(BossDefs.Akaoni) });
                True(MatchSnapshotIO.RulesetMismatch(s, a.match) != null, "大将軍の局面を赤鬼王の対局に読み込めてしまう");
            }

            // 防壁の HP・照準が変わればハッシュも変わる
            wall.hp = 1;
            True(MatchHash.Compute(f.Export()) != before, "防壁の HP が変わってもハッシュが同じ");
            wall.hp = 2;
            MinionData m = gun.minion;
            m.Aim.locked = true;
            gun.minion = m;
            True(MatchHash.Compute(f.Export()) != before, "軍銃兵の照準が固まってもハッシュが同じ");
            m.Aim.locked = false;
            gun.minion = m;
            Equal(before, MatchHash.Compute(f.Export()), "元に戻したらハッシュも戻る（大将軍）");

            // 駒の番号のない局面（番号を足す前に書き出したもの）は、読み込んだ順に番号を振る
            MatchSnapshot old = MatchSnapshot.FromJson(s.ToJson());
            foreach (PieceSnapshot p in old.pieces) p.serial = 0;
            old.raid.nextPieceId = 1;
            using (Fixture g = new Fixture(BattleRulesets.Kassen, GameModes.BossRaid))
            {
                g.match.SetupRaid(def, new RaidFormation { pieces = RaidRules.AutoFormation(def) });
                MatchSnapshot back = g.Load(old);
                True(back.pieces.TrueForAll(p => p.serial > 0) && back.raid.nextPieceId == back.pieces.Count + 1, "番号のない局面を読むと、読み込んだ順に番号を振る");
            }

            // 防壁の HP が正しくない局面は通らない
            MatchSnapshot bad = MatchSnapshot.FromJson(s.ToJson());
            bad.pieces.Find(p => p.type == PieceType.Bouheki).hp = 9;
            True(MatchSnapshotIO.Validate(bad) != null, "防壁の HP が正しくない局面が Validate を通った");
        }
    }

    // 防壁の後ろの守り（WallRules.IsShielded）と、ボスの席の駒の性質が Prototype と同じ
    public static void CheckWallRules()
    {
        Dictionary<int, PieceType> board = new Dictionary<int, PieceType>
        {
            [8 * 10 + 0] = PieceType.GunJu, [7 * 10 + 0] = PieceType.Bouheki, [8 * 10 + 1] = PieceType.GunJu, [6 * 10 + 2] = PieceType.Pawn, [5 * 10 + 2] = PieceType.Bouheki,
        };
        System.Func<int, int, PieceType?> at = (x, y) => board.TryGetValue(x * 10 + y, out PieceType t) ? t : (PieceType?)null;
        True(WallRules.IsShielded(at, 8, 0), "防壁より奥の軍銃兵が守られていない");
        True(!WallRules.IsShielded(at, 8, 1), "防壁のない筋の軍銃兵が守られている");
        True(!WallRules.IsShielded(at, 7, 0), "防壁そのものが守られている");
        True(!WallRules.IsShielded(at, 6, 2), "プレイヤーの駒が守られている");
        True(!WallRules.IsShielded(at, 4, 4), "空きマスが守られている");
        foreach (PieceType t in new[] { PieceType.Kooni, PieceType.GunFu, PieceType.GunJu, PieceType.Bouheki })
        {
            True(PieceRules.IsMinion(t) && !PieceRules.GoesToHand(t) && !PieceRules.CanPromote(t) && PieceValues.Of(t) == 0, $"{PieceNames.Full(t)} がボスの席の駒の性質になっていない");
        }
        True(PieceRules.IsWallPiece(PieceType.Bouheki) && !PieceRules.IsWallPiece(PieceType.GunFu), "防壁の判定");
        Equal(3, WallRules.MaxHp, "防壁の HP");
        Equal(MinionPattern.Forward, MinionRules.PatternOf(PieceType.GunFu), "軍歩兵の動き方");
        Equal(MinionPattern.Gunner, MinionRules.PatternOf(PieceType.GunJu), "軍銃兵の動き方");
        Equal(MinionPattern.None, MinionRules.PatternOf(PieceType.Bouheki), "防壁の動き方");
        Equal(MinionPattern.Chase, MinionRules.PatternOf(PieceType.Kooni), "小赤鬼の動き方");
    }

    // 基本対戦のハッシュは、討伐任務を足す前と同じ形（ボス・王将の被弾・手下の項目を足さない）
    public static void CheckVersusHashHasNoRaidItems()
    {
        using (Fixture f = new Fixture(BattleRulesets.Kassen))
        {
            f.match.Turn.BeginRealtime();
            f.NewPiece(PieceType.King, PlayerType.Player1, 0, 4);
            f.NewPiece(PieceType.King, PlayerType.Player2, 8, 4);
            string text = MatchHash.BuildText(f.Export());
            True(text.Contains("[\"versus\",\"kassen\",null]"), "基本対戦のハッシュのモード・戦闘型");
            True(!text.Contains("akaoni") && !text.Contains("bossRaid"), "基本対戦のハッシュに討伐任務の項目が入っている");
        }
    }

    // 棋譜の版 2：討伐任務のボス（config.boss）と編成（setup）を記録し、読み直せる。版 1（基本対戦）も読める。
    // 知らないボス・正しくない編成の棋譜は読まない
    public static void CheckRaidKifuVersion2()
    {
        RaidFormation formation = SampleFormation();
        KifuFile k = KifuFile.Create(77u, null, null, BattleRulesets.Kassen, GameModes.BossRaid, BossDefs.Akaoni.Id, formation);
        k.actions.Add(new KifuEntry { type = MatchActionType.Move, owner = 0, t = 12, from = new MatchSquare(2, 4), to = new MatchSquare(3, 4), hash = "0123abcd" });
        k.endTick = 40;
        Equal(2, k.version, "新しい棋譜の版");
        string json = k.ToJson();
        KifuFile back = KifuFile.Parse(json);
        Equal("bossRaid", back.config.mode, "棋譜のモード");
        Equal("akaoni", back.config.boss, "棋譜のボス");
        Equal("kassen", back.config.ruleset, "棋譜の戦闘型");
        Equal(formation.pieces.Count, back.setup.pieces.Count, "棋譜の編成（駒の数）");
        True(back.setup.forgePicks.Count == 2 && back.setup.forgePicks[1] == PieceType.Giant, "棋譜の編成（錬成駒）");
        Equal(json, back.ToJson(), "棋譜を読み直して書き出すと JSON が変わった");

        // 版 1（討伐任務を足す前の棋譜。config.boss・setup がない）も読める
        KifuFile v1 = KifuFile.Create(5u, null, null, BattleRulesets.Honshogi);
        string old = v1.ToJson().Replace("\"version\": 2", "\"version\": 1");
        True(old.Contains("\"version\": 1"), "版 1 の棋譜の JSON を作れない");
        KifuFile readOld = KifuFile.Parse(old);
        True(readOld.config.mode == "versus" && readOld.config.boss == "" && readOld.setup.pieces.Count == 0, "版 1 の棋譜を基本対戦として読めない");

        ExpectKifuError(json.Replace("\"version\": 2", "\"version\": 3"), "知らない版の棋譜を読めてしまう");
        ExpectKifuError(json.Replace("\"boss\": \"akaoni\"", "\"boss\": \"nobody\""), "知らないボスの棋譜を読めてしまう");
        KifuFile dai = KifuFile.Parse(json.Replace("\"boss\": \"akaoni\"", "\"boss\": \"daishogun\""));
        Equal("daishogun", dai.config.boss, "大将軍の棋譜のボス");
        KifuFile noKing = KifuFile.Create(77u, null, null, BattleRulesets.Kassen, GameModes.BossRaid, BossDefs.Akaoni.Id,
            new RaidFormation { pieces = new List<RaidPlacement> { new RaidPlacement(PieceType.Pawn, 2, 4) } });
        ExpectKifuError(noKing.ToJson(), "王将のいない編成の棋譜を読めてしまう");
    }

    static void ExpectKifuError(string json, string message)
    {
        try
        {
            KifuFile.Parse(json);
        }
        catch (KifuFormatException)
        {
            return;
        }
        throw new CheckFailedException(message);
    }

    // ルール説明に「討伐任務」のページがあり、ボスの攻撃・ダメージ・コストが定義から出る
    public static void CheckRaidRulesPage()
    {
        List<RulesPage> pages = RulesBook.Build(new RulesSources { Boss = BossDefs.Akaoni });
        RulesPage page = pages.Find(p => p.Title == "討伐任務");
        True(page != null, "ルール説明に「討伐任務」のページがない");
        string text = page.ToPlainText();
        foreach (BossAttackEntry a in BossDefs.Akaoni.Attacks)
        {
            True(text.Contains(BossAttacks.NameOf(a.Id)), $"ルール説明に {BossAttacks.NameOf(a.Id)} がない");
        }
        True(text.Contains($"HP {BossDefs.Akaoni.Hp}") && text.Contains($"上限 {BossDefs.Akaoni.CostLimit}"), "ルール説明に赤鬼王の HP・コストの上限がない");
        True(text.Contains("［この対局］"), "この対局のボスに印が付かない");
        True(text.Contains("破壊砲 8") && text.Contains("隕石 5"), "ルール説明に魔法のダメージがない");
        // 大将軍：攻撃 4 種・陣の駒・勝ちの条件が定義から出る
        foreach (BossAttackEntry a in BossDefs.Daishogun.Attacks)
        {
            True(text.Contains(BossAttacks.NameOf(a.Id)), $"ルール説明に {BossAttacks.NameOf(a.Id)} がない");
        }
        True(text.Contains("軍銃兵 6") && text.Contains("防壁 6") && text.Contains("軍歩兵 9"), "ルール説明に大将軍の陣がない");
        True(text.Contains("全滅させる") && text.Contains($"HP {WallRules.MaxHp}"), "ルール説明に大将軍の勝ちの条件・防壁の HP がない");
        True(!text.Contains("準備中"), "ルール説明に「準備中」のボスが残っている");
        // タイトル画面（TitleView）の入口が「操作」のページに出る
        RulesPage controls = pages.Find(p => p.Title == "操作");
        string ctext = controls != null ? controls.ToPlainText() : "";
        True(ctext.Contains("タイトル画面") && ctext.Contains("通常対局") && ctext.Contains("討伐任務") && ctext.Contains("棋譜を読み込んで再生") && ctext.Contains("タイトルに戻る"),
            "ルール説明の「操作」に、タイトル画面（通常対局・討伐任務・ルール・棋譜を読み込んで再生・タイトルに戻る）がない");
        True(!ctext.Contains("ゲームスタート"), "ルール説明に、古いタイトル（ゲームスタート）が残っている");
        string dai = RulesBook.Build(new RulesSources { Boss = BossDefs.Daishogun }).Find(p => p.Title == "討伐任務").ToPlainText();
        True(dai.IndexOf("［この対局］") > dai.IndexOf("ボスとの戦い"), "大将軍の対局で、この対局の印が付かない");
    }

    // 討伐任務で使える魔法（RaidRules.IsMagicUsable）と、編成・ダメージの表が Prototype の値のまま
    public static void CheckRaidTables()
    {
        foreach (string id in new[] { "Shield", "Wall", "Sword", "Warp", "Bomb", "Wing", "Phalanx", "Meteor", "Cannon", "TimeStop", "UndeadParadise" })
        {
            True(RaidRules.IsMagicUsable(id), $"{id} が討伐任務で使えない");
        }
        foreach (string id in new[] { "Pitfall", "Counter", "Cataclysm", "WorldEnd", "DoubleTurn", "SwapPiece", "Explosion", "ReplicateKing" })
        {
            True(!RaidRules.IsMagicUsable(id), $"{id} が討伐任務で使えてしまう");
        }
        Equal(50, BossDefs.Akaoni.CostLimit, "赤鬼王のチームコストの上限");
        Equal(49, RaidRules.FormationCost(RaidRules.AutoFormation(BossDefs.Akaoni)), "おまかせ配置（本将棋の初期配置）のコスト");
        True(RaidRules.IsValid(SampleFormation(), BossDefs.Akaoni), "おまかせ配置 ＋ 錬成駒 2 種が正しい編成にならない");
        Equal(11, SeatKeys.RaidSolo("x").Forge.Length, "討伐任務の錬成のキーの数（錬成できる駒は最大 11 種）");
    }
}
