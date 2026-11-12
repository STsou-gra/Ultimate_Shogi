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
        foreach (PlayerSnapshot p in players) p?.FillMissing();
        timeStop.FillMissing();
    }
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
    public int worldEndSince = -1;       // 世界の終わりを発動したターン（未発動なら -1）
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
    public bool held;       // 時間停止の予定の駒（盤のデータから外して、その場に残している）
}

// 通常でないマス 1 つ（BoardCell）
[System.Serializable]
public struct CellSnapshot
{
    public int x;
    public int y;
    public CellState state;
    public int burnUntil; // 炎上が終わるターン（なければ -1）
    public int trap;      // 落とし穴の持ち主（(int)PlayerType。なければ -1）
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
}

// 千日手の判定用に記録した局面 1 つ（GameSystem の positionHistory）
[System.Serializable]
public struct PositionSnapshot
{
    public string key;
    public int side;     // 手番（(int)PlayerType）
    public bool inCheck; // 手番のプレイヤーが王手されていたか
}
