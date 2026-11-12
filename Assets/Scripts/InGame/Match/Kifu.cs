using System.Collections.Generic;
using UnityEngine;

// 棋譜（Prototype の src/game/kifu.js）：版・対局の設定・両者の魔法スキルの編成・乱数のシード・行動（MatchAction）の列。
// 各行動には、その行動の処理がすべて終わった時点の盤面のハッシュ（hash。MatchHash。GameSystem.StateHash）を添える。
// 棋譜の再生で盤面がずれたら、何手目でずれたか分かる。JsonUtility でそのまま JSON にする：
//
//  {
//    "format": "ultimate-shogi-unity-kifu", "version": 1, "createdAt": "2026-11-12T12:34:56Z", "app": "0.1",
//    "seed": 123456789,
//    "config": { "mode": "versus", "ruleset": "honshogi", "firstPlayer": 0 },
//    "loadouts": [{ "skills": ["Shield", "Sword", "Pitfall", "Meteor", "TimeStop"] }, { ... }],   … 枠の順（空き枠は ""）
//    "actions": [{ "type": 0, "owner": 0, "from": { "x": 2, "y": 6 }, "to": { "x": 3, "y": 6 }, ..., "hash": "1a2b3c4d" }, ...]
//  }
//
// Prototype の棋譜（format "ultimate-shogi-kifu"）とは互換性がない：座標の x と y が逆（MatchAction の説明）・
// 魔法の Id（SkillDefinition.Id。"Shield" など）・行動の type が数値・ハッシュに入れる項目が違うため、別の format 名にしている。
// MonoBehaviour に依存しない（再生・保存は GameSystem.Kifu.cs、ファイルの選択は KifuFileDialog）
[System.Serializable]
public class KifuConfig
{
    // Unity 側は今のところ 基本対戦 × 本将棋型 だけ（Prototype の config。合戦将棋型・討伐任務を入れたら値を足す）
    public string mode = "versus";
    public string ruleset = "honshogi";
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

    // 再生に渡す行動（hash を除く。Prototype の actionOf）
    public MatchAction ToAction()
    {
        return new MatchAction
        {
            type = type,
            owner = owner,
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
    public const int Version = 1;

    public string format = Format;
    public int version = Version;
    public string createdAt = "";   // 保存した対局を始めた時刻（UTC・ISO 8601。ファイル名にも使う）
    public string app = "";         // 記録したゲームの版（Application.version。参考）
    public uint seed;               // 対局の乱数のシード（MatchState.Rng）
    public KifuConfig config = new KifuConfig();
    public List<KifuLoadout> loadouts = new List<KifuLoadout>();   // [先手, 後手]
    // デバッグキー（CellDebugTool）でマスを変えた対局（再生しても同じ盤面にならない）
    public bool debugEdited;
    public List<KifuEntry> actions = new List<KifuEntry>();

    // 新しい対局の棋譜（行動はまだない。Prototype の createKifu）
    public static KifuFile Create(uint seed, SkillDeck deck1, SkillDeck deck2)
    {
        return new KifuFile
        {
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
        if (header.version != Version) throw new KifuFormatException($"対応していない棋譜の版です（version {header.version}）");

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
        if (k.config.mode != "versus" || k.config.ruleset != "honshogi")
        {
            throw new KifuFormatException($"Unity 版にはまだない対局の設定です（{k.config.mode} × {k.config.ruleset}）");
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
