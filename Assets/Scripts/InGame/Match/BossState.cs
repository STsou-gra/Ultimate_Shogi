using System.Collections.Generic;

// ボス・討伐任務の状態（Prototype の src/core/boss/state.js の state.boss・state.raid、minions.js の駒の minion）。
// プレーンなクラスで、JsonUtility でそのまま JSON にできる（BoardSnapshot・MatchSnapshot がそのまま持つ）。
// ボスは駒ではない：盤の駒の配列には入れず、Covers(x, y) で引く。
//
// 座標は GamePiece と同じ（x = 段、y = 筋）。ボスは (x, y) を「段・筋のいちばん小さいマス」として、段の方向に h・筋の方向に w マスを占める。
// 向き facingX・facingY は上下左右の単位ベクトル（段・筋）。開始時はプレイヤー側（段が小さくなる向き：facingX = -1）。
// JsonUtility は null を書けないので、「なし」は active = false・""・has〜 = false で表す

// 照準（弓・軍銃兵など、プレイヤーの駒を 1 枚選んで追う攻撃。Prototype の src/core/boss/aim.js の { target, cell, locked }）。
// JsonUtility でそのまま JSON にできるよう、「なし」は active = false・target = 0・hasCell = false で表す
[System.Serializable]
public struct AimData
{
    public bool active;      // 照準を持つか
    public int target;       // 追っている駒の番号（駒の serial。いなければ 0）
    public bool hasCell;     // 照準のマスがあるか（選べる駒がなければ false）
    public int x;            // 照準のマス（撃つマス）
    public int y;
    public bool locked;      // 固まったか（撃つ LockSecBefore 秒前から。固まったら照準のマスは動かない）

    public MatchSquare Cell => hasCell ? new MatchSquare(x, y) : MatchSquare.None;

    public override string ToString()
    {
        if (!active) return "なし";
        return $"#{target} {(hasCell ? $"({x}, {y})" : "(なし)")}{(locked ? " 固定" : "")}";
    }
}

// ボスの AI の状態機械（BossEngine が進める。Prototype の state.boss.ai）
[System.Serializable]
public class BossAiState
{
    public const string Cooldown = "cooldown";    // 行動ペース（次の攻撃を選ぶまで）
    public const string Telegraph = "telegraph";  // 予告（範囲が赤く光る）
    public const string Acting = "acting";        // 続く行動（暴れ回り）
    public const string Resting = "resting";      // 続く行動のあとの休憩

    public string phase = Cooldown;
    public int until;                              // このフェーズが終わる対局の時間（tick）
    public string attack = "";                     // 予告中・行動中の攻撃（BossAttacks の Id。なければ ""）
    public bool hasCells;                          // 予告の範囲があるか（暴れ回りは範囲を出さない）
    public List<MatchSquare> cells = new List<MatchSquare>();   // 予告の範囲（予告を始めた時点で確定する）
    public string last = "";                       // 前回の攻撃（2 回続けない攻撃の判定。なければ ""）

    // 続く行動の途中のデータ（暴れ回り：次の 1 歩。Prototype の ai.data = { nextAt, next }）
    public bool hasData;
    public int nextAt = -1;                        // 次の 1 歩の時刻（最後の 1 歩のあとは -1）
    public bool hasNext;                           // 次の 1 歩の行き先があるか（行ける方向がなければ false）
    public MatchSquare next = MatchSquare.None;    // 次の 1 歩のあとのボスの位置（x, y）

    // 予告中の照準（弓。Prototype の ai.data.aim。なければ aim.active = false）。固まったかは aim.locked（Prototype の ai.data.locked と同じ時に立つ）
    public AimData aim;

    public BossAiState Clone()
    {
        BossAiState c = (BossAiState)MemberwiseClone();
        c.cells = new List<MatchSquare>(cells);
        return c;
    }
}

[System.Serializable]
public class BossState
{
    public bool active;        // ボスがいるか（討伐任務の対局だけ true）
    public string id = "";     // BossDefs の Id
    public int x;              // 占めるマスのうち、段のいちばん小さいマス
    public int y;              // 占めるマスのうち、筋のいちばん小さいマス
    public int w = 3;          // 幅（筋の方向）
    public int h = 3;          // 奥行き（段の方向）
    public int hp;
    public int maxHp;
    public int facingX = -1;   // 向き（段の方向。-1 = プレイヤー側）
    public int facingY;        // 向き（筋の方向）
    public BossAiState ai = new BossAiState();

    public bool IsAlive => active && hp > 0;

    // (px, py) がボスのマスか（倒れたボスはどのマスも占めない。Prototype の bossAt）
    public bool Covers(int px, int py)
    {
        return IsAlive && CoversAt(x, y, px, py);
    }

    // ボスが (bx, by) にいるとしたら (px, py) を占めるか
    public bool CoversAt(int bx, int by, int px, int py)
    {
        return px >= bx && px < bx + h && py >= by && py < by + w;
    }

    public BossDef Def => BossDefs.ById(id);

    public BossState Clone()
    {
        BossState c = (BossState)MemberwiseClone();
        c.ai = ai != null ? ai.Clone() : new BossAiState();
        return c;
    }

    // ボスの定義からボスの状態を作る（Prototype の createBoss）
    public static BossState Create(BossDef def)
    {
        BossState b = new BossState
        {
            active = true, id = def.Id, x = def.StartX, y = def.StartY, w = def.W, h = def.H,
            hp = def.Hp, maxHp = def.Hp, facingX = -1, facingY = 0,
        };
        b.ai.until = MatchPolicy.SecToTicks(def.CooldownSec);
        return b;
    }
}

// 討伐任務の状態（Prototype の state.raid のうち、対局中に変わる値。編成は MatchSnapshot・棋譜が持つ）
[System.Serializable]
public class RaidState
{
    public bool active;          // 討伐任務の対局か
    public int kingHits;         // 王将がボスの攻撃を受けた回数（ボスの KingMaxHits 回目で負け）
    public int minionSerial;     // 次に現れる手下の番号（現れた順。手下を動かす順・乱数を引く順を決める。Prototype の駒の id の順）
    public int nextPieceId = 1;  // 次に作る駒の番号（駒の serial。Prototype の s.nextId。作った順に 1 から振る：照準が駒を選ぶ順・追う駒を指すのに使う）

    public RaidState Clone()
    {
        return (RaidState)MemberwiseClone();
    }
}

// ボスの席の駒（手下）の状態（Prototype の駒の minion = { next, planned, to } と gunner = { next, aim }）。
// 一定の間隔で動く手下（小赤鬼・軍歩兵）は Active、照準で撃つ手下（軍銃兵）は Gunner が true。防壁はどちらも false（番号 Serial だけ持つ）
public struct MinionData
{
    public bool Active;      // 一定の間隔で動く手下の状態を持つか（小赤鬼・軍歩兵）
    public int Serial;       // 現れた順の番号（ボスの席の駒すべて。動かす順）
    public int Next;         // 次に動く対局の時間（tick）
    public bool Planned;     // 行き先を決めたか（Next の WarnSec 秒前に決めて表示する）
    public bool HasTo;       // 行き先があるか（動けなければ false）
    public int ToX;
    public int ToY;

    public bool Gunner;      // 照準で撃つ手下の状態を持つか（軍銃兵）
    public int GunNext;      // 次に撃つ対局の時間（tick）
    public AimData Aim;      // 照準（周期の始めに選んだ駒を追い、撃つ LockSecBefore 秒前に固まる）
}

// 手下の動き方（Prototype の minions.js の PATTERNS）
public enum MinionPattern
{
    Chase,     // 周り 8 マスで、隣のプレイヤーの駒を襲うか王将へ近づく（小赤鬼）
    Forward,   // 前（プレイヤー側）へ 1 マスずつ進む（軍歩兵）
    Gunner,    // 動かずに、照準で追った駒のマスを撃つ（軍銃兵）
    None,      // 動かない（防壁）
}

// 手下の動き方の数値（Prototype の src/defs/pieces.js の KOONI・GUNFU・GUNJU の minion）
public static class MinionRules
{
    public const float StepSec = 3f;   // 小赤鬼・軍歩兵：この秒数ごとに 1 マス動く
    public const float WarnSec = 1f;   // 行き先はこの秒数だけ前に決めて表示する
    public const string AttackId = "summon";      // 小赤鬼の攻撃が駒に当たったときの攻撃の Id（演出・ログ用）
    public const string MarchAttackId = "march";  // 軍歩兵
    public const string GunshotAttackId = "gunshot"; // 軍銃兵

    // 軍銃兵：GunnerIntervalSec 秒ごとに照準のマスを撃つ。照準は撃つ GunnerLockSecBefore 秒前に固まる。
    // 最初に撃つのは GunnerOffsetSec + GunnerIntervalSec 秒後（テストで書き換えられるように変数で持つ）
    public const float GunnerIntervalSec = 8f;
    public const float GunnerLockSecBefore = 3f;
    public static float GunnerOffsetSec = 0f;

    public static MinionPattern PatternOf(PieceType type)
    {
        switch (type)
        {
            case PieceType.GunFu: return MinionPattern.Forward;
            case PieceType.GunJu: return MinionPattern.Gunner;
            case PieceType.Bouheki: return MinionPattern.None;
            default: return MinionPattern.Chase;
        }
    }

    // 手下の攻撃が駒に当たったときの攻撃の Id
    public static string AttackIdOf(PieceType type)
    {
        switch (type)
        {
            case PieceType.GunFu: return MarchAttackId;
            case PieceType.GunJu: return GunshotAttackId;
            default: return AttackId;
        }
    }

    // 手下の攻撃の名前（ログ・ルール説明用）
    public static string AttackName(string attackId)
    {
        switch (attackId)
        {
            case MarchAttackId: return "軍歩兵の攻撃";
            case GunshotAttackId: return "軍銃兵の射撃";
            case AttackId: return null; // 小赤鬼号令（BossAttacks の名前を使う）
            default: return null;
        }
    }
}

// 防壁の決まり（Prototype の BOUHEKI の wall: { hp: 3, damagePerHit: 1 } と src/core/board.js の isShieldedByWall）
public static class WallRules
{
    public const int MaxHp = 3;          // 置いたときの HP
    public const int DamagePerHit = 1;   // プレイヤーの攻撃は何で当てても 1 回につきこれだけ減る

    // 防壁の後ろの守り：(x, y) のボスの席の駒が、同じ筋でそれより手前（プレイヤー側。段が小さい）にある防壁に守られているか。
    // 守られた駒は、その防壁が残っている間、プレイヤーのどの攻撃も受けない（移動の行き先・狙撃の対象・取り・消失・押し出しのどれにもならない）。
    // typeAt：(x, y) の駒の種類（駒がなければ null）
    public static bool IsShielded(System.Func<int, int, PieceType?> typeAt, int x, int y)
    {
        PieceType? q = typeAt(x, y);
        if (!q.HasValue || !PieceRules.IsMinion(q.Value)) return false;
        for (int xx = x - 1; xx >= 0; xx--)
        {
            PieceType? w = typeAt(xx, y);
            if (w.HasValue && PieceRules.IsWallPiece(w.Value)) return true;
        }
        return false;
    }
}

// プレイヤーの攻撃がボスに当たったときの種類（Prototype の damage.js の source）
public enum BossHitKind
{
    Piece,      // 駒をボスのマスへ動かした（駒の種類・成りで変わる）
    Special,    // 必殺技
    Magic,      // 魔法（魔法ごとの値）
    Explosion,  // 火薬の爆発
    Snipe,      // 狙撃
    Phalanx,    // ファランクスの突き
}

[System.Serializable]
public struct BossHit
{
    public BossHitKind kind;
    public PieceType piece;    // Piece：動かした駒 ／ Special：必殺技を使った駒
    public bool promoted;      // Piece：成った駒か
    public string magic;       // Magic：魔法の Id（SkillDefinition.Id。RaidRules.MagicBossDamage の表を引く）

    public static BossHit OfPiece(PieceType type, bool promoted) => new BossHit { kind = BossHitKind.Piece, piece = type, promoted = promoted };
    public static BossHit OfSpecial(PieceType type) => new BossHit { kind = BossHitKind.Special, piece = type };
    public static BossHit OfMagic(string skillId) => new BossHit { kind = BossHitKind.Magic, magic = skillId };
    public static readonly BossHit Explosion = new BossHit { kind = BossHitKind.Explosion };
    public static readonly BossHit Snipe = new BossHit { kind = BossHitKind.Snipe };
    public static readonly BossHit Phalanx = new BossHit { kind = BossHitKind.Phalanx };
}

// ボスの動きの通知（演出・テスト用。Prototype の core が積むイベントのうち、ボス・手下の分）
public enum BossEventType
{
    Phase,          // フェーズが変わった（Phase・Until・Attack）
    Telegraph,      // 予告（Attack・Cells（範囲を出さない予告は null）・Sec・Until・Text）
    TelegraphUpdate,// 予告の範囲が動いた（照準が駒を追う）・固まった（Attack・Cells・Locked）
    TelegraphEnd,   // 予告の表示を消す
    Attack,         // 攻撃が当たった（Attack・Cells・Hits）
    HitPiece,       // ボスの攻撃が駒に当たった（Piece・At・Attack・Result）
    KingHit,        // 王将がボスの攻撃を受けた（Piece・Hits 回目・Max 回目で負け）
    Step,           // ボスが動いた（From・To はボスの位置。Attack）
    StepWarn,       // 次の 1 歩の行き先の表示（Cells。空なら消す。Sec）
    Shove,          // 踏まれて生き残った駒が押し出された（Piece・From・To）
    MinionSpawned,  // 手下が現れた（Piece・At・Attack）
    MinionPlanned,  // 手下が次の行き先を決めた（Piece・From・To。動けなければ HasTo = false）
    MinionStep,     // 手下が 1 マス動いた（Piece・From・To）
    MinionStrike,   // 手下がプレイヤーの駒を襲った（Piece・Target・At）
    Damaged,        // ボスがダメージを受けた（Amount・Hp・MaxHp・Source・At）
    Defeated,       // ボスの HP が 0 になった
    Guarded,        // 守り（守備）の間に当たって、0 ダメージで防いだ（Source・At）
    GuardStart,     // 守備を始めた（Sec）
    GuardEnd,       // 守備が終わった
    Deployed,       // 出陣命令で兵を出した（Cells：出したマス）
    GunnerAim,      // 軍銃兵の照準が変わった・固まった（Piece・HasTo・To：照準のマス・Locked）
    GunnerShot,     // 軍銃兵が照準のマスを撃った（Piece・From・At・Hits：当たった駒の数）
    WallHit,        // 防壁がプレイヤーの攻撃を受けて HP が減った（Piece・At・Hp・MaxHp・Text：攻撃の名前）。対局（GameSystem）が出す
    WallBroken,     // 防壁の HP が 0 になって崩れた（Piece・At）。対局（GameSystem）が出す
}

public class BossEvent
{
    public BossEventType Type;
    public string Phase;
    public string Attack;
    public int Until;
    public float Sec;
    public string Text;
    public List<MatchSquare> Cells;
    public int Hits;
    public int Max;
    public string Result;
    public object Piece;      // 駒（盤面のスナップショットでは SnapshotPiece、対局では GamePiece）
    public object Target;
    public MatchSquare From = MatchSquare.None;
    public MatchSquare To = MatchSquare.None;
    public MatchSquare At = MatchSquare.None;
    public bool HasTo;
    public int Amount;
    public int Hp;
    public int MaxHp;
    public BossHit Source;
    public bool Locked;       // TelegraphUpdate・GunnerAim：照準が固まったか

    // ボスの攻撃が駒に当たった結果（Result）
    public const string Guarded = "guarded";   // 盾で防いだ（盾は割れる）
    public const string King = "king";         // 王将の被弾
    public const string Smashed = "smashed";   // 撃破
    public const string Exploded = "exploded"; // 火薬が爆発
    public const string Endured = "endured";   // 巨人が耐えた（HP -1）
}
