using System.Collections.Generic;
using UnityEngine;

// 棋譜（Prototype の src/game/kifu.js）：版・対局の設定・両者の魔法スキルの編成・乱数のシード・行動（MatchAction）の列。
// 各行動には、その行動の処理がすべて終わった時点の盤面のハッシュ（hash。MatchHash。GameSystem.StateHash）を添える。
// 棋譜の再生で盤面がずれたら、何手目でずれたか分かる。JsonUtility でそのまま JSON にする：
//
//  {
//    "format": "ultimate-shogi-unity-kifu", "version": 2, "createdAt": "2026-11-12T12:34:56Z", "app": "0.1",
//    "seed": 123456789,
//    "config": { "mode": "versus", "ruleset": "honshogi", "boss": "", "firstPlayer": 0 },   … 討伐任務は mode "bossRaid"・ruleset "kassen"・boss "akaoni"
//    "setup": { "pieces": [{ "type": 7, "x": 0, "y": 4 }, ...], "forgePicks": [9, 12] },   … 討伐任務の編成（基本対戦は空。版 2 から）
//    "loadouts": [{ "skills": ["Shield", "Sword", "Pitfall", "Meteor", "TimeStop"] }, { ... }],   … 枠の順（空き枠は ""）
//    "endTick": -1,                                                  … 合戦将棋型：保存した時点の tick（本将棋型は -1）
//    "actions": [{ "type": 0, "owner": 0, "t": -1, "from": { "x": 2, "y": 6 }, "to": { "x": 3, "y": 6 }, ..., "hash": "1a2b3c4d" }, ...]
//  }
//
// 合戦将棋型（config.ruleset = "kassen"）の行動には、行動したプレイヤー owner と、行動を実行した tick の t が付く（Prototype の棋譜の版 2 と同じ）。
// 再生は同じシードで始めた対局の両方の席に RealtimeReplayController を付け、記録した tick に記録した順で入れる（GameSystem.Kifu.cs）。
//
// Prototype の棋譜（format "ultimate-shogi-kifu"）とは互換性がない：座標の x と y が逆（MatchAction の説明）・
// 魔法の Id（SkillDefinition.Id。"Shield" など）・行動の type が数値・ハッシュに入れる項目が違うため、別の format 名にしている。
// MonoBehaviour に依存しない（再生・保存は GameSystem.Kifu.cs、ファイルの選択は KifuFileDialog）
[System.Serializable]
public class KifuConfig
{
    // モード（GameModeDef.Id：基本対戦 versus・討伐任務 bossRaid）× 戦闘型（BattleRuleset.Id：本将棋型 honshogi・合戦将棋型 kassen）。Prototype の config
    public string mode = "versus";
    public string ruleset = "honshogi";
    // 討伐任務：ボス（BossDef.Id。基本対戦は ""。版 2 から）
    public string boss = "";

    public BattleRuleset Ruleset => BattleRulesets.ById(ruleset);
    public int firstPlayer = (int)MatchConfig.FirstPlayer;
}

// 1 人分の魔法スキルの編成（枠の順。SkillDefinition.Id、空き枠は ""）
[System.Serializable]
public class KifuLoadout
{
    public List<string> skills = new List<string>();

    public static KifuLoadout From(SkillDeck deck)
    {
        KifuLoadout l = new KifuLoadout();
        if (deck == null || deck.skills == null) return l;
        foreach (SkillDefinition s in deck.skills) l.skills.Add(s != null ? s.Id : "");
        return l;
    }

    public KifuLoadout Clone()
    {
        return new KifuLoadout { skills = new List<string>(skills ?? new List<string>()) };
    }
}

// 棋譜の 1 行動（MatchAction の項目。hash（MatchAction にある）には、その行動の処理がすべて終わった時点の盤面のハッシュ
// （16 進 8 桁。まだ終わっていなければ ""）を入れる）
[System.Serializable]
public class KifuEntry : MatchAction
{
    public static KifuEntry From(MatchAction a)
    {
        KifuEntry e = new KifuEntry
        {
            type = a.type,
            owner = a.owner,
            t = a.t,
            from = a.from,
            to = a.to,
            promote = a.promote,
            piece = a.piece,
            skill = a.skill ?? "",
            targets = a.targets != null ? new List<MatchSquare>(a.targets) : new List<MatchSquare>(),
            orient = a.orient,
        };
        return e;
    }

    // 合戦将棋型の再生（RealtimeReplayController）に渡す記録（owner・t・hash つき）
    public MatchAction ToRecorded()
    {
        MatchAction a = ToAction();
        a.hash = hash ?? "";
        return a;
    }

    // 再生に渡す行動（hash を除く。Prototype の actionOf）
    public MatchAction ToAction()
    {
        return new MatchAction
        {
            type = type,
            owner = owner,
            t = t,
            from = from,
            to = to,
            promote = promote,
            piece = piece,
            skill = skill ?? "",
            targets = targets != null ? new List<MatchSquare>(targets) : new List<MatchSquare>(),
            orient = orient,
        };
    }
}

[System.Serializable]
public class KifuFile
{
    public const string Format = "ultimate-shogi-unity-kifu";
    // 版 2：討伐任務（config.boss と setup）を足した。版 1（基本対戦だけ）も読める
    public const int Version = 2;
    public const int OldestVersion = 1;

    public string format = Format;
    public int version = Version;
    public string createdAt = "";   // 保存した対局を始めた時刻（UTC・ISO 8601。ファイル名にも使う）
    public string app = "";         // 記録したゲームの版（Application.version。参考）
    public uint seed;               // 対局の乱数のシード（MatchState.Rng）
    public KifuConfig config = new KifuConfig();
    public List<KifuLoadout> loadouts = new List<KifuLoadout>();   // [先手, 後手]（討伐任務の後手はボスの席なので空）
    // 討伐任務の編成（Prototype の棋譜の setup：自陣に並べた駒と、錬成駒の編成。基本対戦は空）
    public RaidFormation setup = new RaidFormation();
    // デバッグキー（CellDebugTool）でマスを変えた対局（再生しても同じ盤面にならない）
    public bool debugEdited;
    // 合戦将棋型：保存した時点の tick（Prototype の endTick。本将棋型は -1）
    public int endTick = -1;
    public List<KifuEntry> actions = new List<KifuEntry>();

    // 新しい対局の棋譜（行動はまだない。Prototype の createKifu）
    public static KifuFile Create(uint seed, SkillDeck deck1, SkillDeck deck2, BattleRuleset ruleset = null,
                                  GameModeDef mode = null, string bossId = null, RaidFormation formation = null)
    {
        return new KifuFile
        {
            config = new KifuConfig { mode = (mode ?? GameModes.Versus).Id, ruleset = (ruleset ?? BattleRulesets.Honshogi).Id, boss = bossId ?? "" },
            setup = formation != null ? formation.Clone() : new RaidFormation(),
            createdAt = System.DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
            app = Application.version,
            seed = seed,
            loadouts = new List<KifuLoadout> { KifuLoadout.From(deck1), KifuLoadout.From(deck2) },
        };
    }

    public int Count => actions != null ? actions.Count : 0;

    public string ToJson()
    {
        return JsonUtility.ToJson(this, true) + "\n";
    }

    // JSON 文字列を棋譜として読む（Prototype の parseKifu）。形式が違えば理由つきの KifuFormatException
    public static KifuFile Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new KifuFormatException("ファイルが空です");
        // 形式の確認だけを先に読む（JsonUtility は知らない項目を無視するので、ほかの JSON も読めてしまう）
        KifuHeader header = KifuHeader.Read(text);
        if (header == null) throw new KifuFormatException("JSON として読めません");
        if (header.format == MatchSnapshot.Format) throw new KifuFormatException("局面のファイルです（「局面を読み込む」で開いてください）");
        if (header.format == "ultimate-shogi-kifu") throw new KifuFormatException("Prototype（Web 版）の棋譜は Unity 版では再生できません（座標・魔法の名前が違うため）");
        if (header.format != Format) throw new KifuFormatException("この対局の棋譜ファイルではありません");
        if (header.version < OldestVersion || header.version > Version) throw new KifuFormatException($"対応していない棋譜の版です（version {header.version}）");

        KifuFile k;
        try
        {
            k = JsonUtility.FromJson<KifuFile>(text);
        }
        catch (System.ArgumentException)
        {
            throw new KifuFormatException("JSON として読めません");
        }
        if (k == null || k.loadouts == null || k.loadouts.Count != 2 || k.actions == null)
        {
            throw new KifuFormatException("棋譜の中身が壊れています");
        }
        if (k.config == null) k.config = new KifuConfig();
        if (string.IsNullOrEmpty(k.config.ruleset)) k.config.ruleset = BattleRulesets.Honshogi.Id;
        if (string.IsNullOrEmpty(k.config.mode)) k.config.mode = GameModes.Versus.Id;
        if (k.config.boss == null) k.config.boss = "";
        if (k.setup == null) k.setup = new RaidFormation();
        if (k.setup.pieces == null) k.setup.pieces = new List<RaidPlacement>();
        if (k.setup.forgePicks == null) k.setup.forgePicks = new List<PieceType>();
        if (GameModes.ById(k.config.mode) == null || k.config.Ruleset == null)
        {
            throw new KifuFormatException($"Unity 版にはまだない対局の設定です（{k.config.mode} × {k.config.ruleset}）");
        }
        if (k.config.mode == GameModes.BossRaid.Id)
        {
            // 討伐任務：ボスと編成が要る（ボスの戦闘型・編成の決まりに合わないものは再生できない）
            BossDef boss = BossDefs.ById(k.config.boss);
            if (boss == null || !boss.Implemented) throw new KifuFormatException($"Unity 版にはまだないボスの棋譜です（{k.config.boss}）");
            if (k.config.ruleset != boss.RulesetId) throw new KifuFormatException($"討伐任務の棋譜の戦闘型が違います（{k.config.ruleset}）");
            List<string> errors = RaidRules.Validate(k.setup, boss, out _);
            if (errors.Count > 0) throw new KifuFormatException("討伐任務の棋譜の編成が正しくありません（" + errors[0] + "）");
        }
        foreach (KifuLoadout l in k.loadouts)
        {
            if (l.skills == null) l.skills = new List<string>();
        }
        foreach (KifuEntry e in k.actions)
        {
            if (e == null) throw new KifuFormatException("棋譜の中身が壊れています");
            if (e.targets == null) e.targets = new List<MatchSquare>();
            if (e.skill == null) e.skill = "";
            if (e.hash == null) e.hash = "";
            // 合戦将棋型は、どの行動にも行動したプレイヤーと tick がある
            if (k.config.Ruleset.Realtime && (!e.Owner.HasValue || e.t < 0)) throw new KifuFormatException("合戦将棋型の棋譜の行動に、行動したプレイヤー（owner）か時刻（t）がありません");
        }
        return k;
    }

    // 保存するファイル名（Prototype の downloadKifu と同じ ultimate-shogi-日時.json。日時は createdAt（UTC））
    public string FileName => $"ultimate-shogi-{Stamp(createdAt)}.json";

    // "2026-11-12T12:34:56Z" → "20261112-123456"（読めなければ今の時刻）
    public static string Stamp(string iso)
    {
        System.DateTime t;
        if (!System.DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t))
        {
            t = System.DateTime.UtcNow;
        }
        return t.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture);
    }
}

// 棋譜・局面のファイルの形式の確認用（format と version だけ読む）
[System.Serializable]
public class KifuHeader
{
    public string format = "";
    public int version;

    public static KifuHeader Read(string text)
    {
        try
        {
            return JsonUtility.FromJson<KifuHeader>(text);
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }
}

// 棋譜・局面のファイルが読めない（理由は Message。画面にそのまま出す）
public class KifuFormatException : System.Exception
{
    public KifuFormatException(string message) : base(message) { }
}
