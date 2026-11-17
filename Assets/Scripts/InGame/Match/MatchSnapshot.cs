using System.Collections.Generic;
using UnityEngine;

// 局面の書き出し（Prototype の src/core/state.js の GameState と Game.exportState / importState）。
// 今の局面を 1 つのプレーンなクラスにまとめたもの。JsonUtility でそのまま JSON にできる（ToJson / FromJson）。
// 書き出しは GameSystem.ExportState、読み込みは GameSystem.ImportState（盤・駒の GameObject・UI を作り直す）。
// 中身を詰める・戻す処理は MatchSnapshotIO、盤面のハッシュは MatchHash。
//
//  format / version … 形式の名前と版（版を上げたら MatchSnapshotIO.Validate で古い版を読めるようにする）
//  firstPlayer      … 先手（(int)PlayerType）
//  current          … 手番（(int)PlayerType）
//  turnNumber       … ターン数（先手・後手が 1 回ずつ指して 1 ターン）／ moveNumber … 手数
//  extraTurn        … 2回行動の続きがあるか（手番を終える次の行動のあとも同じプレイヤーの手番が続く）
//  gameOver / winner / draw … 勝敗が決まったか・勝者（(int)PlayerType。引き分け・未決着は -1）・引き分けか
//  rng              … 対局の結果に関わる乱数 GameRng の状態 { seed, state }
//  players          … プレイヤーごと（添字 = (int)PlayerType）：エナジー・持ち駒・魔法の編成・超常魔法・世界の終わり・倒された初期配置の駒
//  pieces           … 駒。盤上の駒と、時間停止の「予定の駒」（held = true。盤のデータから外してその場に残している駒）。
//                     id は書き出すたびに振り直す番号（1 から。盤の添字の順 → 予定の駒）。時間停止の予定から駒を指すのに使う
//  cells            … 通常でないマス（破壊・炎上・落とし穴）だけ。盤の添字の順
//  timeStop         … 時間停止（停止世界）中なら active = true
//  positions        … 千日手の判定用に記録した局面（手番の始まりごと）
//  ruleset          … 戦闘型（BattleRuleset.Id。"honshogi" / "kassen"。版 1 のまま足した項目：ない JSON は本将棋型）
//  clock            … 合戦将棋型の時間（MatchClock の tick と対局の時間。本将棋型は active = false）。
//                     合戦将棋型では、駒の連打の間隔（PieceSnapshot.cooldownUntil。tick）・炎上の期限（CellSnapshot.burnUntil。対局の時間の tick）・
//                     世界の終わりの発動時刻（PlayerSnapshot.worldEndSince。対局の時間）・停止世界の経過（TimeStopSnapshot.elapsed。tick）も時間で入る
//  mode             … モード（GameModeDef.Id。"versus" = 基本対戦 ／ "bossRaid" = 討伐任務。版 1 のまま足した項目：ない JSON は基本対戦）
//  boss / raid      … 討伐任務だけ（BossState・RaidState。それ以外は active = false）：ボスの位置・HP・向き・AI のフェーズと期限・予告の範囲・
//                     暴れ回りの次の 1 歩・弓の照準、王将の被弾の数・手下の番号・駒の番号。手下の動き（小赤鬼・軍歩兵：次に動く時刻・行き先 ／
//                     軍銃兵：次に撃つ時刻・照準）と防壁の HP は PieceSnapshot の minion〜・gunner〜・hp
//  formation        … 討伐任務の編成（自陣に並べた駒・錬成駒。錬成できる駒はここから決まる）
//
// 座標は GamePiece の X・Y と同じ（x = 段、y = 筋。Prototype とは x と y が逆）。
// JsonUtility は null を書けないので、「なし」は -1（駒の id・マス・プレイヤー・ターン）で表す。列挙は数値で書かれる
[System.Serializable]
public class MatchSnapshot
{
    public const string Format = "ultimate-shogi-state";
    public const int CurrentVersion = 1;

    public string format = Format;
    public int version = CurrentVersion;

    public int firstPlayer;
    public int current;
    public int turnNumber;
    public int moveNumber = 1;
    public bool extraTurn;

    public bool gameOver;
    public int winner = -1;
    public bool draw;

    public RngSnapshot rng = new RngSnapshot();
    public List<PlayerSnapshot> players = new List<PlayerSnapshot>();
    public List<PieceSnapshot> pieces = new List<PieceSnapshot>();
    public List<CellSnapshot> cells = new List<CellSnapshot>();
    public TimeStopSnapshot timeStop = new TimeStopSnapshot();
    public List<PositionSnapshot> positions = new List<PositionSnapshot>();
    public string ruleset = BattleRulesets.Honshogi.Id;
    public ClockSnapshot clock = new ClockSnapshot();
    public string mode = "versus";
    public BossState boss = new BossState();
    public RaidState raid = new RaidState();
    public RaidFormation formation = new RaidFormation();

    // モード（知らない Id なら基本対戦）
    public GameModeDef Mode => GameModes.ById(mode);
    public bool HasBoss => boss != null && boss.active;
    public bool IsRaid => raid != null && raid.active;

    // 戦闘型（知らない Id なら null）
    public BattleRuleset Ruleset => BattleRulesets.ById(string.IsNullOrEmpty(ruleset) ? BattleRulesets.Honshogi.Id : ruleset);

    // owner のプレイヤーの分（なければ null）
    public PlayerSnapshot Player(PlayerType owner)
    {
        if (players == null) return null;
        foreach (PlayerSnapshot p in players)
        {
            if (p != null && p.owner == (int)owner) return p;
        }
        return null;
    }

    // ------------------------------------------------------------
    //  JSON（JsonUtility）
    // ------------------------------------------------------------

    public string ToJson(bool prettyPrint = false)
    {
        return JsonUtility.ToJson(this, prettyPrint);
    }

    // 読めなければ null（中身が正しいかは MatchSnapshotIO.Validate で調べる）
    public static MatchSnapshot FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            MatchSnapshot s = JsonUtility.FromJson<MatchSnapshot>(json);
            if (s != null) s.FillMissing();
            return s;
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    // 複製（JSON を通す）
    public MatchSnapshot Clone()
    {
        return FromJson(ToJson());
    }

    // JSON に無かったリストを空にしておく（手で書いた JSON・古い版を読んだとき）
    void FillMissing()
    {
        if (format == null) format = "";
        if (rng == null) rng = new RngSnapshot();
        if (players == null) players = new List<PlayerSnapshot>();
        if (pieces == null) pieces = new List<PieceSnapshot>();
        if (cells == null) cells = new List<CellSnapshot>();
        if (timeStop == null) timeStop = new TimeStopSnapshot();
        if (positions == null) positions = new List<PositionSnapshot>();
        if (string.IsNullOrEmpty(ruleset)) ruleset = BattleRulesets.Honshogi.Id;
        if (clock == null) clock = new ClockSnapshot();
        if (string.IsNullOrEmpty(mode)) mode = GameModes.Versus.Id;
        if (boss == null) boss = new BossState();
        if (boss.ai == null) boss.ai = new BossAiState();
        if (boss.ai.cells == null) boss.ai.cells = new List<MatchSquare>();
        if (boss.id == null) boss.id = "";
        if (boss.ai.attack == null) boss.ai.attack = "";
        if (boss.ai.last == null) boss.ai.last = "";
        if (raid == null) raid = new RaidState();
        if (formation == null) formation = new RaidFormation();
        if (formation.pieces == null) formation.pieces = new List<RaidPlacement>();
        if (formation.forgePicks == null) formation.forgePicks = new List<PieceType>();
        foreach (PlayerSnapshot p in players) p?.FillMissing();
        timeStop.FillMissing();
    }
}

// 合戦将棋型の時間（MatchClock）。tick … 行動の t（停止世界の間も進む）／ time … 対局の時間（停止世界の間は止まる）
[System.Serializable]
public class ClockSnapshot
{
    public bool active;
    public int tick;
    public int time;
}

// 乱数 GameRng（Seed・State は uint。JSON で読みやすいように long で持つ）
[System.Serializable]
public class RngSnapshot
{
    public long seed;
    public long state;
}

[System.Serializable]
public class PlayerSnapshot
{
    public int owner;                    // (int)PlayerType
    public int energy;
    public List<HandCount> hand = new List<HandCount>();       // 持ち駒（PieceRules.HandOrder の順。0 枚の駒も書く）
    public List<string> loadout = new List<string>();          // 魔法の編成（SkillDeck の枠の順に SkillDefinition.Id。空の枠は ""）
    public bool transcendentUnlocked;    // 超常魔法が解放されたか
    public bool transcendentUsed;        // 超常魔法を使ったか
    public int worldEndSince = -1;       // 世界の終わりを発動したターン（合戦将棋型は対局の時間。未発動なら -1）
    public List<FallenSnapshot> fallenInitial = new List<FallenSnapshot>(); // 倒された初期配置の駒（倒された順）

    public int HandCountOf(PieceType type)
    {
        if (hand == null) return 0;
        foreach (HandCount h in hand)
        {
            if (h.piece == type) return h.count;
        }
        return 0;
    }

    internal void FillMissing()
    {
        if (hand == null) hand = new List<HandCount>();
        if (loadout == null) loadout = new List<string>();
        if (fallenInitial == null) fallenInitial = new List<FallenSnapshot>();
    }
}

[System.Serializable]
public struct HandCount
{
    public PieceType piece;
    public int count;
}

[System.Serializable]
public struct FallenSnapshot
{
    public PieceType type;
    public DefeatKind kind;
    public int turn; // 倒されたターン
}

[System.Serializable]
public class PieceSnapshot
{
    public int id;
    public PieceType type;
    public int owner;       // (int)PlayerType
    public int x;           // 段
    public int y;           // 筋
    public bool promoted;
    public int moveCount;   // 必殺技のチャージ（盤上で動いた回数）
    public bool initial;    // 初期配置の駒か
    public bool shield;     // 盾状態
    public bool phalanx;    // ファランクス状態
    public bool undead;     // アンデッド
    public bool wing;       // 翼状態（通常魔法「翼」。鳥は駒の種類で決まるので入れない）
    public bool counter;    // カウンター状態（通常魔法「カウンター」）
    public bool held;       // 時間停止の予定の駒（盤のデータから外して、その場に残している）
    public int hp;          // 巨人・防壁の残りの HP（それ以外は 0。巨人の x・y は 3×3 の中央のマス）
    public int cooldownUntil; // 合戦将棋型：連打の間隔が終わる tick（GamePiece.cooldownUntilTick。0 = 動いたことがない）
    // 討伐任務：ボスの手下（小赤鬼）の状態（MinionData。手下でない駒は minion = false のまま）
    public bool minion;       // 手下の状態を持つか
    public int minionSerial;  // 現れた順の番号（手下を動かす順）
    public int minionNext;    // 次に動く対局の時間（tick）
    public bool minionPlanned; // 行き先を決めたか
    public bool minionHasTo;  // 行き先があるか
    public int minionToX = -1;
    public int minionToY = -1;
    // 討伐任務（大将軍）：軍銃兵の状態（次に撃つ時刻・照準）と、駒の番号（作った順。照準が追う駒を指す。GamePiece.serial）。
    // 防壁は minion・gunner とも false で、minionSerial（置いた順）と hp だけ持つ。版 1 のまま足した項目：ない JSON は 0・false
    public bool gunner;       // 照準で撃つ手下（軍銃兵）の状態を持つか
    public int gunNext;       // 次に撃つ対局の時間（tick）
    public AimData aim;       // 軍銃兵の照準
    public int serial;        // 駒の番号（討伐任務でなければ 0）

    public MinionData Minion => new MinionData
    {
        Active = minion, Serial = minionSerial, Next = minionNext, Planned = minionPlanned, HasTo = minionHasTo, ToX = minionToX, ToY = minionToY,
        Gunner = gunner, GunNext = gunNext, Aim = aim,
    };
}

// 通常でないマス 1 つ（BoardCell）
[System.Serializable]
public struct CellSnapshot
{
    public int x;
    public int y;
    public CellState state;
    public int burnUntil; // 炎上が終わるターン（合戦将棋型は対局の時間の tick。なければ -1）
    public int trap;      // 落とし穴の持ち主（(int)PlayerType。なければ -1）
    public int wall;      // 壁を建てた側（(int)PlayerType。state が Wall のときだけ使う）
}

// 時間停止の状態（TimeStopState）。駒は PieceSnapshot.id、移動は moves の添字で指す（なければ -1）
[System.Serializable]
public class TimeStopSnapshot
{
    public bool active;
    public int owner;
    public int total;
    public int remaining;
    public bool ending;
    public bool justStarted;
    public int elapsed;      // 合戦将棋型：止まってからの tick（TimeStopState.Elapsed。TimeStopSec 秒で解除）
    public List<TimeStopMoveSnapshot> moves = new List<TimeStopMoveSnapshot>();
    public List<TimeStopPendingSnapshot> pending = new List<TimeStopPendingSnapshot>();

    internal void FillMissing()
    {
        if (moves == null) moves = new List<TimeStopMoveSnapshot>();
        if (pending == null) pending = new List<TimeStopPendingSnapshot>();
        foreach (TimeStopPendingSnapshot e in pending)
        {
            if (e != null && e.reason == null) e.reason = "";
        }
    }
}

[System.Serializable]
public class TimeStopMoveSnapshot
{
    public int piece = -1;
    public MatchSquare from = MatchSquare.None;
    public MatchSquare to = MatchSquare.None;
    public bool captured;
    public bool bounced;
    public bool undone;
    public bool counted;
}

[System.Serializable]
public class TimeStopPendingSnapshot
{
    public TimeStopKind kind;
    public int piece = -1;   // 関係する駒
    public int move = -1;    // その駒の移動の記録（moves の添字）
    public MatchSquare at = MatchSquare.None;
    public bool done;

    public int target = -1;  // 取られる駒・消える駒・爆発する火薬
    public PieceType targetType;
    public int byOwner;      // 取り：取った側
    public string reason = "";
    public MatchSquare hitDirection = new MatchSquare(0, 0); // 倒されるときに飛ばされる向き（演出用）

    public TimeStopCellEffect cellEffect;
    public int cellOwner;
    public int turns;
    public BossHit bossHit;  // ボスへのダメージの予定（kind が Boss のとき）：攻撃の種類
}

// 千日手の判定用に記録した局面 1 つ（GameSystem の positionHistory）
[System.Serializable]
public struct PositionSnapshot
{
    public string key;
    public int side;     // 手番（(int)PlayerType）
    public bool inCheck; // 手番のプレイヤーが王手されていたか
}
