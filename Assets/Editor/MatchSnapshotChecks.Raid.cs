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

            f.NewPiece(PieceType.King, PlayerType.Player1, 0, 4).IsInitial = true;
            f.NewPiece(PieceType.Pawn, PlayerType.Player1, 2, 4).IsInitial = true;
            GamePiece minion = f.NewPiece(PieceType.Kooni, PlayerType.Player2, 7, 8);
            minion.minion = new MinionData { Active = true, Serial = 2, Next = 280, Planned = true, HasTo = true, ToX = 6, ToY = 7 };

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
        ExpectKifuError(json.Replace("\"boss\": \"akaoni\"", "\"boss\": \"daishogun\""), "まだ遊べないボスの棋譜を読めてしまう");
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
