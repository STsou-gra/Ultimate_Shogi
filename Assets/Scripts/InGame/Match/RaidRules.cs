using System.Collections.Generic;

// 討伐任務の決まりの数値（Prototype の src/defs/teamCost.js・src/core/boss/formation.js・damage.js・src/config.js の BOSS_DAMAGE）。
//  ・編成：チームコスト制の初期配置（自陣 3 段。王将は必ず 1 枚）・錬成駒の編成・錬成できる駒
//  ・ボスへのダメージ：駒ごと・技ごと・魔法ごとの値の表
//  ・討伐任務で使える魔法
// MonoBehaviour に依存しない（編成画面・対局・盤面のスナップショット・テストが同じ値を使う）。
// 座標は GamePiece と同じ（x = 段、y = 筋。先手の自陣は x = 0〜2）

// 編成の駒 1 枚（棋譜・局面にも残す。JsonUtility でそのまま JSON にできる）
[System.Serializable]
public class RaidPlacement
{
    public PieceType type;
    public int x;
    public int y;

    public RaidPlacement() { }

    public RaidPlacement(PieceType type, int x, int y)
    {
        this.type = type;
        this.x = x;
        this.y = y;
    }

    public RaidPlacement Clone() => new RaidPlacement(type, x, y);
}

// 討伐任務の編成（Prototype の setup = { formation, forgePicks }）
[System.Serializable]
public class RaidFormation
{
    public List<RaidPlacement> pieces = new List<RaidPlacement>();   // 自陣に並べる駒
    public List<PieceType> forgePicks = new List<PieceType>();       // 錬成駒の編成（初期配置に置いていない駒の種類から、ボスの ForgeSlots 種まで）

    public RaidFormation Clone()
    {
        RaidFormation f = new RaidFormation();
        foreach (RaidPlacement p in pieces) f.pieces.Add(p.Clone());
        f.forgePicks.AddRange(forgePicks);
        return f;
    }
}

// 錬成できる駒 1 つ（駒・ボタンの名前・コスト）
public struct RaidForgeOption
{
    public PieceType Type;
    public string Label;
    public int Cost;
    public bool Auto;   // 初期配置に置いてあるので自動で錬成できる（錬成駒には選べない）
}

public static class RaidRules
{
    // ------------------------------------------------------------
    //  編成（チームコスト）
    // ------------------------------------------------------------

    public const PieceType KingType = PieceType.King;

    // 初期配置のチームコスト（合計がボスの CostLimit 以下になるように並べる。王将は 0。必ず 1 枚だけ）。
    // 置けない駒は -1（Prototype の TEAM_COST。火薬 3・狙撃手 8・鳥 2 も置ける。巨人・小赤鬼は置けない）
    public static int TeamCost(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return 1;
            case PieceType.Lance: return 3;
            case PieceType.Knight: return 3;
            case PieceType.SilverGeneral: return 4;
            case PieceType.GoldGeneral: return 4;
            case PieceType.Bishop: return 6;
            case PieceType.Rook: return 6;
            case PieceType.King: return 0;
            case PieceType.Gunpowder: return 3;
            case PieceType.Sniper: return 8;
            case PieceType.Bird: return 2;
            default: return -1;
        }
    }

    public static bool CanPlace(PieceType type) => TeamCost(type) >= 0;

    // 編成画面の駒のボタンのタブと並び順（Prototype の PALETTE_CATEGORIES）
    public static readonly PieceType[] BasicPalette =
    {
        PieceType.King, PieceType.Rook, PieceType.Bishop, PieceType.GoldGeneral, PieceType.SilverGeneral,
        PieceType.Knight, PieceType.Lance, PieceType.Pawn,
    };
    public static readonly PieceType[] OriginalPalette = { PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird };

    // 通常の駒の錬成コスト（エナジー。Prototype の FORGE_COST）。錬成できない駒は -1
    public static int RegularForgeCost(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return 2;
            case PieceType.Lance: return 4;
            case PieceType.Knight: return 4;
            case PieceType.SilverGeneral: return 6;
            case PieceType.GoldGeneral: return 6;
            case PieceType.Bishop: return 10;
            case PieceType.Rook: return 10;
            default: return -1;
        }
    }

    // オリジナル駒の錬成コスト（Prototype の FORGE_RECIPES。Unity の Resources/ForgeRecipeBook と同じ値）。錬成できない駒は -1
    public static int OriginalForgeCost(PieceType type)
    {
        switch (type)
        {
            case PieceType.Gunpowder: return 3;
            case PieceType.Sniper: return 5;
            case PieceType.Bird: return 2;
            case PieceType.Giant: return 5;
            default: return -1;
        }
    }

    // 錬成駒の編成で選べる駒の種類の全部（オリジナル駒 → 通常の駒の順。Prototype の FORGE_PICK_TYPES）
    public static readonly PieceType[] ForgePickTypes =
    {
        PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird, PieceType.Giant,
        PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook,
    };

    // 初期配置の駒の種類から自動で錬成できる駒の並び（Prototype の HAND_ORDER：飛・角・金・銀・桂・香・歩・火薬・狙撃手・鳥・巨人）
    static readonly PieceType[] AutoForgeOrder =
    {
        PieceType.Rook, PieceType.Bishop, PieceType.GoldGeneral, PieceType.SilverGeneral, PieceType.Knight, PieceType.Lance, PieceType.Pawn,
        PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird, PieceType.Giant,
    };

    // 錬成できる駒の種類のレシピ（通常の駒は RegularForgeCost、オリジナル駒は OriginalForgeCost。錬成できない種類は null）
    public static RaidForgeOption? ForgeRecipeOf(PieceType type)
    {
        int cost = OriginalForgeCost(type);
        if (cost < 0 && type != KingType) cost = RegularForgeCost(type);
        if (cost < 0) return null;
        return new RaidForgeOption { Type = type, Label = PieceNames.Short(type), Cost = cost };
    }

    // 編成のコストの合計
    public static int FormationCost(IEnumerable<RaidPlacement> pieces)
    {
        int n = 0;
        if (pieces == null) return n;
        foreach (RaidPlacement f in pieces) n += System.Math.Max(0, TeamCost(f.type));
        return n;
    }

    // 初期配置に置いた駒の種類のうち、自動で錬成できるもの（王将を除く）
    public static HashSet<PieceType> AutoForgeTypes(IEnumerable<RaidPlacement> pieces)
    {
        HashSet<PieceType> res = new HashSet<PieceType>();
        if (pieces == null) return res;
        foreach (RaidPlacement f in pieces)
        {
            if (f.type != KingType && ForgeRecipeOf(f.type).HasValue) res.Add(f.type);
        }
        return res;
    }

    // 錬成駒の候補（ForgePickTypes の順）。Auto = 初期配置に置いてあるので自動で錬成できる（錬成駒には選べない）
    public static List<RaidForgeOption> ForgePickOptions(IEnumerable<RaidPlacement> pieces)
    {
        HashSet<PieceType> auto = AutoForgeTypes(pieces);
        List<RaidForgeOption> res = new List<RaidForgeOption>();
        foreach (PieceType t in ForgePickTypes)
        {
            RaidForgeOption o = ForgeRecipeOf(t).Value;
            o.Auto = auto.Contains(t);
            res.Add(o);
        }
        return res;
    }

    // 錬成駒の編成から、初期配置に置いた駒の種類（自動で錬成できる）を外す（初期配置を変えたときに枠を空ける）
    public static List<PieceType> PrunedForgePicks(IEnumerable<RaidPlacement> pieces, IEnumerable<PieceType> picks)
    {
        HashSet<PieceType> auto = AutoForgeTypes(pieces);
        List<PieceType> res = new List<PieceType>();
        if (picks == null) return res;
        foreach (PieceType t in picks)
        {
            if (!auto.Contains(t)) res.Add(t);
        }
        return res;
    }

    // 編成の検査（Prototype の validateFormation）。問題の一覧を返す（空なら OK）。cost に編成のコストの合計
    public static List<string> Validate(RaidFormation setup, BossDef def, out int cost)
    {
        List<string> errors = new List<string>();
        List<RaidPlacement> formation = setup != null && setup.pieces != null ? setup.pieces : new List<RaidPlacement>();
        List<PieceType> picks = setup != null && setup.forgePicks != null ? setup.forgePicks : new List<PieceType>();
        HashSet<int> seen = new HashSet<int>();
        void Add(string e) { if (!errors.Contains(e)) errors.Add(e); }

        foreach (RaidPlacement f in formation)
        {
            if (!CanPlace(f.type)) { Add($"{PieceNames.Full(f.type)} は初期配置に置けません"); continue; }
            if (!BoardCells.InBounds(f.x, f.y) || !BoardZones.IsOwnZone(PlayerType.Player1, f.x)) { Add("駒は自陣（手前 3 段）にだけ置けます"); continue; }
            if (!seen.Add(GameHelper.CalcPanelNum(f.x, f.y))) Add("同じマスに 2 つの駒は置けません");
        }
        int kings = 0;
        foreach (RaidPlacement f in formation)
        {
            if (f.type == KingType) kings++;
        }
        if (kings == 0) Add("王将を 1 枚入れてください");
        if (kings > 1) Add("王将は 1 枚だけです");
        cost = FormationCost(formation);
        if (cost > def.CostLimit) Add($"コストが上限を超えています（{cost} / {def.CostLimit}）");
        if (picks.Count > def.ForgeSlots) Add($"錬成駒は {def.ForgeSlots} 種までです");
        if (new HashSet<PieceType>(picks).Count != picks.Count) Add("同じ錬成駒は 2 回選べません");
        HashSet<PieceType> auto = AutoForgeTypes(formation);
        foreach (PieceType t in picks)
        {
            if (System.Array.IndexOf(ForgePickTypes, t) < 0) Add($"{PieceNames.Full(t)} は錬成駒に選べません");
            else if (auto.Contains(t)) Add($"{PieceNames.Full(t)} は初期配置にあるので、錬成駒に選ばなくても錬成できます");
        }
        return errors;
    }

    public static bool IsValid(RaidFormation setup, BossDef def)
    {
        return def != null && Validate(setup, def, out _).Count == 0;
    }

    // おまかせ配置の優先順（上から、コストの上限に入るものだけ置く）。全部入れば本将棋の初期配置（20 枚・コスト 49）。
    // Prototype の AUTO_FORMATION を Unity の座標に直したもの（先手の初期配置：GameBoard.CreateLayout と同じマス）
    static readonly RaidPlacement[] AutoOrder = BuildAutoOrder();

    static RaidPlacement[] BuildAutoOrder()
    {
        List<RaidPlacement> list = new List<RaidPlacement> { new RaidPlacement(PieceType.King, 0, 4) };
        for (int y = 0; y < BoardCells.Size; y++) list.Add(new RaidPlacement(PieceType.Pawn, 2, y));
        list.Add(new RaidPlacement(PieceType.GoldGeneral, 0, 3));
        list.Add(new RaidPlacement(PieceType.GoldGeneral, 0, 5));
        list.Add(new RaidPlacement(PieceType.SilverGeneral, 0, 2));
        list.Add(new RaidPlacement(PieceType.SilverGeneral, 0, 6));
        list.Add(new RaidPlacement(PieceType.Rook, 1, 7));
        list.Add(new RaidPlacement(PieceType.Bishop, 1, 1));
        list.Add(new RaidPlacement(PieceType.Knight, 0, 1));
        list.Add(new RaidPlacement(PieceType.Knight, 0, 7));
        list.Add(new RaidPlacement(PieceType.Lance, 0, 0));
        list.Add(new RaidPlacement(PieceType.Lance, 0, 8));
        return list.ToArray();
    }

    // おまかせ配置：優先順に、コストの上限に入るものだけ置く（Prototype の autoFormation）
    public static List<RaidPlacement> AutoFormation(BossDef def)
    {
        List<RaidPlacement> res = new List<RaidPlacement>();
        int cost = 0;
        foreach (RaidPlacement f in AutoOrder)
        {
            int c = System.Math.Max(0, TeamCost(f.type));
            if (cost + c > def.CostLimit) continue;
            cost += c;
            res.Add(f.Clone());
        }
        return res;
    }

    // 錬成できる駒：初期配置の駒の種類（王将を除く。飛・角・金・銀・桂・香・歩・火薬・狙撃手・鳥の順）＋ 錬成駒の編成（選んだ順）。
    // 同じ種類は 1 回だけ（Prototype の raidForgeRecipes）
    public static List<RaidForgeOption> ForgeRecipes(RaidFormation setup)
    {
        List<RaidForgeOption> res = new List<RaidForgeOption>();
        if (setup == null) return res;
        HashSet<PieceType> auto = AutoForgeTypes(setup.pieces);
        foreach (PieceType t in AutoForgeOrder)
        {
            if (auto.Contains(t)) res.Add(ForgeRecipeOf(t).Value);
        }
        foreach (PieceType t in setup.forgePicks)
        {
            RaidForgeOption? r = ForgeRecipeOf(t);
            if (r.HasValue && !res.Exists(x => x.Type == t)) res.Add(r.Value);
        }
        return res;
    }

    // ------------------------------------------------------------
    //  ボスへのダメージ（1 か所で計算する。Prototype の damage.js）
    // ------------------------------------------------------------

    // 駒をボスのマスへ動かしたときのダメージ（成った駒は +1。Prototype の PIECE_DEFS の bossDamage。値のない駒は 1）
    public static int PieceBossDamage(PieceType type, bool promoted)
    {
        int d;
        switch (type)
        {
            case PieceType.Knight:
            case PieceType.SilverGeneral:
            case PieceType.GoldGeneral:
                d = 2;
                break;
            case PieceType.Bishop:
            case PieceType.Rook:
            case PieceType.Giant:
                d = 3;
                break;
            default:
                d = 1; // 歩・香・王・鳥（と、値を持たない火薬・狙撃手・小赤鬼）
                break;
        }
        return promoted ? d + 1 : d;
    }

    public const int SpecialBossDamage = 3;    // 必殺技（どの技も）
    public const int ExplosionBossDamage = 3;  // 火薬の爆発
    public const int SnipeBossDamage = 2;      // 狙撃
    public const int PhalanxBossDamage = 1;    // ファランクスの突き

    // 魔法のダメージ（魔法の Id = SkillDefinition.Id。ボスに効かない魔法は 0）：剣 2・ボム 3・隕石 5・破壊砲 8
    public static int MagicBossDamage(string skillId)
    {
        switch (skillId)
        {
            case "Sword": return 2;
            case "Bomb": return 3;
            case "Meteor": return 5;
            case "Cannon": return 8;
            default: return 0;
        }
    }

    // 基本のダメージ（倍率をかける前）
    public static int BaseBossDamage(BossHit hit)
    {
        switch (hit.kind)
        {
            case BossHitKind.Piece: return PieceBossDamage(hit.piece, hit.promoted);
            case BossHitKind.Special: return SpecialBossDamage;
            case BossHitKind.Magic: return MagicBossDamage(hit.magic);
            case BossHitKind.Explosion: return ExplosionBossDamage;
            case BossHitKind.Snipe: return SnipeBossDamage;
            case BossHitKind.Phalanx: return PhalanxBossDamage;
            default: return 0;
        }
    }

    // ボスへのダメージ（ボスの倍率込み。切り捨て・1 以上。倍率 0 以下なら効かない）
    public static int BossDamage(BossDef def, BossHit hit)
    {
        int baseDamage = BaseBossDamage(hit);
        if (baseDamage <= 0) return 0;
        float k = def != null && def.DamageRate != null ? def.DamageRate(hit) : 1f;
        if (k <= 0f) return 0;
        return System.Math.Max(1, (int)System.Math.Floor(baseDamage * k));
    }

    // 攻撃の名前（ログ・ルール説明用）
    public static string HitLabel(BossHit hit)
    {
        switch (hit.kind)
        {
            case BossHitKind.Piece: return PieceNames.Short(hit.piece) + (hit.promoted ? "（成）" : "");
            case BossHitKind.Special: return "必殺技";
            case BossHitKind.Magic: return "魔法";
            case BossHitKind.Explosion: return "爆発";
            case BossHitKind.Snipe: return "狙撃";
            case BossHitKind.Phalanx: return "ファランクスの槍";
            default: return "";
        }
    }

    // ------------------------------------------------------------
    //  討伐任務で使える魔法（魔法の Id = SkillDefinition.Id）
    // ------------------------------------------------------------

    // Prototype の定義の modes：盾・壁・剣・ワープ・ボム・翼・ファランクス・隕石・破壊砲と、超常魔法の停止世界・アンデッド・パラダイス
    // （魔力の火花は動作確認用。編成では選べない）。落とし穴・カウンター・天変地異・世界の終わりは使えない。
    // Unity にしかない魔法（2回行動・駒入替・爆発・王将複製）も使えない
    static readonly HashSet<string> RaidMagicIds = new HashSet<string>
    {
        "Shield", "Wall", "Sword", "Warp", "Bomb", "Wing", "Phalanx", "Meteor", "Cannon", "TimeStop", "UndeadParadise", "TestSpark",
    };

    public static bool IsMagicUsable(string skillId)
    {
        return !string.IsNullOrEmpty(skillId) && RaidMagicIds.Contains(skillId);
    }

    public const string MagicBlockedText = "討伐任務では使えません";
}
