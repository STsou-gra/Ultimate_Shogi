using System.Collections.Generic;

// ボスの定義（Prototype の src/defs/bosses/・src/defs/bossAttacks/）。数値は Prototype と同じ。
// BossDefs.All に足すと、ボス選択の画面（BossSelectView）に並ぶ（一番右に「coming soon！」のカード）。
// ボスの動き（AI・攻撃の範囲・手下）は Match/BossEngine.cs、プレイヤーの攻撃のダメージは Match/RaidRules.cs。
//
// 座標は GamePiece と同じ（x = 段：先手の前進方向が +x、y = 筋）。Prototype（x = 筋・y = 段。先手は下）とは
// 「Unity の x = 8 − Prototype の y、Unity の y = Prototype の x」。ボスは StartX・StartY を「段・筋のいちばん小さいマス」として H × W マスを占める
public class BossAttackEntry
{
    public string Id;        // BossAttacks の Id
    public int Weight;       // 重み付きの抽選の重み
    public bool NoRepeat;    // 2 回続けない
}

public class BossDef
{
    public string Id;
    public string Name;
    public string Title;
    public string Description;
    public string RulesetId = "kassen";  // 戦闘型（討伐任務のボスは合戦将棋型）
    public int W = 3;                    // 幅（筋の方向のマス数）
    public int H = 3;                    // 奥行き（段の方向のマス数）
    public int StartX;                   // 開始位置（占めるマスのうち、段のいちばん小さいマス）
    public int StartY;                   // 開始位置（占めるマスのうち、筋のいちばん小さいマス）
    public int Hp;                       // HP（0 になったら討伐成功）
    public float CooldownSec;            // 行動ペース（前の行動が終わってから次の攻撃を選ぶまでの秒数）
    public BossAttackEntry[] Attacks = new BossAttackEntry[0];
    public int KingMaxHits = 3;          // 王将がボスの攻撃を受けて、この回数目で負け（赤鬼王：2 回まで耐え、3 回目でゲームオーバー）
    public int CostLimit;                // 編成のチームコストの上限
    public int ForgeSlots;               // 錬成駒の編成の数
    public uint Color = 0xd32f2f;        // 表示の色（0xRRGGBB）
    public bool Implemented = true;      // false のボスは、ボス選択の画面に並ぶが選べない（まだ移植していないボス）

    // ダメージの倍率（Prototype の damageTaken。なければ 1）。0 以下なら効かない
    public System.Func<BossHit, float> DamageRate;

    // 攻撃の選び方を上書きする（任意。引数はボスの状態と対局の時間。攻撃の Id を返す。例：HP が半分を切ったら変える・テストで攻撃を固定する）。
    // なければ Attacks の重み付きの抽選（対局の乱数。Prototype の chooseAttack）
    public System.Func<BossState, int, string> ChooseAttack;
}

// ボスの攻撃の定義（Prototype の src/defs/bossAttacks/*.js のうち、数値と表示の文言。範囲と当たり方は BossEngine）
public class BossAttackDef
{
    public string Id;
    public string Name;
    public string Summary;        // ボスのパネルの攻撃の一覧に出す短い説明
    public string Description;
    public float TelegraphSec;    // 予告の秒数（範囲は予告を始めた時点で確定し、そのあと動かない）
    public string TelegraphText;  // 範囲を出さない予告の文言（暴れ回り。範囲を出す攻撃は null）
    public float DurationSec;     // 続く行動（暴れ回り）の秒数（一発の攻撃は 0）
    public float StepSec;         // 続く行動：1 歩の間隔（次の行き先はこの秒数だけ前に表示する）
    public float RestSec;         // 続く行動のあとの休憩の秒数（休憩の間は行動ペースを数えない）
    public uint Color = 0xff5252;

    public bool IsLasting => DurationSec > 0f;
}

public static class BossAttacks
{
    // 薙ぎ払い：ボスに接する 1 列に 5 マス（ボスの幅 3 ＋左右 1 マスずつ。盤の外は切る）。
    // 薙ぎ払う側は予告の始めに重み付きの抽選で決める（正面 4・右 1・左 1・裏 1。範囲が 1 マスもない側は抽選から外す）
    public static readonly BossAttackDef Sweep = new BossAttackDef
    {
        Id = "sweep", Name = "薙ぎ払い", Summary = "前方5×1（ときどき左右・裏側）",
        Description = "前方 5×1 マスを金棒で薙ぎ払う。ときどき右側・左側（1×5）や裏側（5×1）を薙ぎ払う。",
        TelegraphSec = 5f, Color = 0xff5252,
    };
    // 薙ぎ払いの側ごとの重み（正面・右・左・裏の順。Prototype の sweep.js の sides）。テストで書き換えられるように配列で持つ
    public static int[] SweepSideWeights = { 4, 1, 1, 1 };

    // ぶっ叩き：正面の中央の筋に奥行き 3 マス
    public static readonly BossAttackDef Smash = new BossAttackDef
    {
        Id = "smash", Name = "ぶっ叩き", Summary = "正面1×3に叩きつける",
        Description = "正面 1×3 マスに金棒を叩きつける。",
        TelegraphSec = 5f, Color = 0xff6d00,
    };
    public const int SmashDepth = 3;

    // 回転薙ぎ払い：周り（正面 5×1・裏側 5×1・右側 1×5・左側 1×5。3×3 のボスなら周りの 16 マス）
    public static readonly BossAttackDef Spin = new BossAttackDef
    {
        Id = "spin", Name = "回転薙ぎ払い", Summary = "周り（前後5×1・左右1×5）",
        Description = "金棒を振り回し、周り（正面・裏側 5×1、右側・左側 1×5）をまとめて薙ぎ払う。",
        TelegraphSec = 5f, Color = 0xff1744,
    };

    // 小赤鬼号令：予告 4 秒のあと、1一・1二・1三・9一・9二・9三に小赤鬼を呼び出す（盤上は SummonMax 体まで）
    public static readonly BossAttackDef Summon = new BossAttackDef
    {
        Id = "summon", Name = "小赤鬼号令", Summary = "1一〜1三・9一〜9三に小赤鬼",
        Description = "1一〜1三・9一〜9三に小赤鬼を呼び出す。小赤鬼は 3 秒ごとに周りへ 1 マス動き、踏んだ駒を倒す。",
        TelegraphSec = 4f, Color = 0xff7043,
    };
    // 呼び出すマス（1一・1二・1三・9一・9二・9三の順。Prototype の (8,0)(8,1)(8,2)(0,0)(0,1)(0,2) を Unity の座標に直したもの）
    public static readonly MatchSquare[] SummonSquares =
    {
        new MatchSquare(8, 8), new MatchSquare(7, 8), new MatchSquare(6, 8),
        new MatchSquare(8, 0), new MatchSquare(7, 0), new MatchSquare(6, 0),
    };
    public const PieceType SummonPiece = PieceType.Kooni;
    public static int SummonMax = 12;

    // 暴れ回り：予告 5 秒（範囲は出さず、ボスが赤く光る）→ 10 秒間、1 秒ごとに 8 方向のどれかへ 1 マス動く → 5 秒休む
    public static readonly BossAttackDef Rampage = new BossAttackDef
    {
        Id = "rampage", Name = "暴れ回り", Summary = "盤上を暴れ回り、駒を踏む",
        Description = "盤上を暴れ回り、踏みつけた駒を蹴散らす。",
        TelegraphSec = 5f, TelegraphText = "暴れ回り！", DurationSec = 10f, StepSec = 1f, RestSec = 5f, Color = 0xd50000,
    };

    public static readonly IReadOnlyList<BossAttackDef> All = new List<BossAttackDef> { Sweep, Smash, Rampage, Spin, Summon };

    public static BossAttackDef ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BossAttackDef a in All)
        {
            if (a.Id == id) return a;
        }
        return null;
    }

    public static string NameOf(string id)
    {
        BossAttackDef a = ById(id);
        return a != null ? a.Name : "ボスの攻撃";
    }
}

public static class BossDefs
{
    // 赤鬼王（Prototype の src/defs/bosses/akaoni.js）：3×3・HP 30・行動ペース 5 秒・攻撃 5 種。
    // 開始位置は盤の奥の中央（Prototype の (3,0)〜(5,2) ＝ Unity の段 6〜8・筋 3〜5）
    public static readonly BossDef Akaoni = new BossDef
    {
        Id = "akaoni", Name = "赤鬼王", Title = "紅蓮の大鬼",
        Description = "金棒を振り回す巨大な赤鬼の王。周りを薙ぎ払い、叩きつけ、小赤鬼を呼び出し、ときに盤上を暴れ回る。",
        W = 3, H = 3, StartX = 6, StartY = 3, Hp = 30,
        CooldownSec = 5f,
        Attacks = new[]
        {
            new BossAttackEntry { Id = "sweep", Weight = 4 },
            new BossAttackEntry { Id = "smash", Weight = 4 },
            new BossAttackEntry { Id = "spin", Weight = 2 },
            new BossAttackEntry { Id = "summon", Weight = 2, NoRepeat = true },
            new BossAttackEntry { Id = "rampage", Weight = 2, NoRepeat = true },
        },
        KingMaxHits = 3,
        CostLimit = 50, ForgeSlots = 2,
        Color = 0xd32f2f,
    };

    // 大将軍（Prototype の src/defs/bosses/daishogun.js）。まだ移植していないので、ボス選択の画面には並ぶが選べない（Implemented = false）。
    // 移植するときは、攻撃（弓・大剣斬り・守備・出陣命令）と兵（軍歩兵・軍銃兵・防壁）を BossEngine・BossAttacks に足して Implemented を外す
    public static readonly BossDef Daishogun = new BossDef
    {
        Id = "daishogun", Name = "大将軍", Title = "鉄壁の陣",
        Description = "陣の奥から動かず、弓と大剣で攻め、兵を繰り出す大将軍。（準備中：次の移植で実装）",
        W = 3, H = 3, StartX = 6, StartY = 3, Hp = 30,
        CooldownSec = 10f,
        KingMaxHits = 3,
        CostLimit = 50, ForgeSlots = 2,
        Color = 0x5c6bc0,
        Implemented = false,
    };

    public static readonly IReadOnlyList<BossDef> All = new List<BossDef> { Akaoni, Daishogun };

    // Id からボスの定義を引く（なければ null）
    public static BossDef ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BossDef b in All)
        {
            if (b.Id == id) return b;
        }
        return null;
    }

    // ボスの定義の検査（盤は 9×9 だけ）。問題の一覧を返す（空なら OK。Prototype の validateBossDef）
    public static List<string> Validate(BossDef def)
    {
        List<string> errors = new List<string>();
        if (def == null)
        {
            errors.Add("ボスが定義されていません");
            return errors;
        }
        if (!(def.W > 0 && def.H > 0 && def.StartX >= 0 && def.StartY >= 0 && def.StartX + def.H <= BoardCells.Size && def.StartY + def.W <= BoardCells.Size))
        {
            errors.Add("ボスの大きさ・開始位置が盤に収まりません");
        }
        if (def.Hp <= 0) errors.Add("HP は 1 以上");
        foreach (BossAttackEntry a in def.Attacks)
        {
            if (BossAttacks.ById(a.Id) == null) errors.Add($"攻撃 {a.Id} が定義されていません");
        }
        return errors;
    }
}
