using System.Collections.Generic;

// 駒が倒された方法（Prototype の effects.js の capture / vanish）
public enum DefeatKind
{
    Capture, // 取り：エナジー配分あり・取った側の持ち駒になる
    Vanish,  // 消失：エナジー・持ち駒化なし（爆発など）
}

// 倒された初期配置の駒の記録（超常魔法「アンデッド・パラダイス」で復活させる）
public struct FallenPieceRecord
{
    public PieceType Type;
    public DefeatKind Kind;
    public int TurnNumber; // 倒されたターン
}

// 対局の状態のうち、新しいルール（ターン・エナジー・倒された初期配置の駒・乱数・超常魔法）の分。
// MonoBehaviour に依存しないので、後で棋譜・CPU・オンラインの同期に使える。
// 盤面と持ち駒は今のところ GameBoard・Player が持っている
public class MatchState
{
    public TurnState Turn { get; }
    public EnergyState Energy { get; }

    // 対局の結果に関わる乱数（天変地異・世界の終わりなど）。シードは対局の開始時に決める（GameRng.NewSeed）
    public GameRng Rng { get; }

    private readonly List<FallenPieceRecord>[] fallenInitial = { new List<FallenPieceRecord>(), new List<FallenPieceRecord>() };

    // 超常魔法（Prototype の state.transcendentUnlocked / transcendentUsed）。添字は (int)PlayerType。
    // 解放は一度 true になったら、条件が崩れても戻らない。超常魔法は 1 対局に 1 回だけ使える
    private readonly bool[] transcendentUnlocked = new bool[2];
    private readonly bool[] transcendentUsed = new bool[2];

    // 超常魔法「世界の終わり」を発動したターン（未発動なら null。Prototype の state.worldEndSince）
    private readonly int?[] worldEndSince = new int?[2];

    public MatchState(PlayerType firstPlayer, uint seed)
    {
        Turn = new TurnState(firstPlayer);
        Energy = new EnergyState();
        Rng = new GameRng(seed);
    }

    // 倒された自分の初期配置の駒（持ち主ごと。取り・消失どちらも）
    public IReadOnlyList<FallenPieceRecord> FallenInitial(PlayerType owner)
    {
        return fallenInitial[(int)owner];
    }

    public void RecordFallenInitial(PlayerType owner, PieceType type, DefeatKind kind)
    {
        fallenInitial[(int)owner].Add(new FallenPieceRecord { Type = type, Kind = kind, TurnNumber = Turn.TurnNumber });
    }

    // 記録を消す（アンデッド・パラダイスで復活させたとき。空きマスが足りず復活できなかった駒の分も消える。Prototype と同じ）
    public void ClearFallenInitial(PlayerType owner)
    {
        fallenInitial[(int)owner].Clear();
    }

    // ------------------------------------------------------------
    //  超常魔法（Prototype の src/core/turn.js の checkTranscendentUnlock・play.js の castMagic）
    // ------------------------------------------------------------

    public bool IsTranscendentUnlocked(PlayerType owner)
    {
        return transcendentUnlocked[(int)owner];
    }

    public bool IsTranscendentUsed(PlayerType owner)
    {
        return transcendentUsed[(int)owner];
    }

    // 解放する。今回はじめて解放したら true
    public bool UnlockTranscendent(PlayerType owner)
    {
        if (transcendentUnlocked[(int)owner]) return false;
        transcendentUnlocked[(int)owner] = true;
        return true;
    }

    // 超常魔法を使った（発動してエナジーを払った時点で記録する）
    public void MarkTranscendentUsed(PlayerType owner)
    {
        transcendentUsed[(int)owner] = true;
    }

    // ------------------------------------------------------------
    //  世界の終わり（Prototype の src/core/play.js の startWorldEnd・worldEndCellsPerTurn）
    // ------------------------------------------------------------

    // owner が世界の終わりを発動したターン（未発動なら null）
    public int? WorldEndSince(PlayerType owner)
    {
        return worldEndSince[(int)owner];
    }

    public bool IsWorldEndActive => worldEndSince[0].HasValue || worldEndSince[1].HasValue;

    // 世界の終わりを始める（今のターンから。1 対局に 1 回なので、もう始まっていれば何もしない）
    public void StartWorldEnd(PlayerType owner)
    {
        if (!worldEndSince[(int)owner].HasValue) worldEndSince[(int)owner] = Turn.TurnNumber;
    }

    // 今のターンの終わりに崩れるマスの数（発動したプレイヤー 1 人につき MatchConfig.WorldEndCells）。
    // 発動したターンの分は発動したときに崩れているので、そのターンの終わりには数えない
    public int WorldEndCellsThisRound
    {
        get
        {
            int n = 0;
            foreach (int? since in worldEndSince)
            {
                if (since.HasValue && since.Value < Turn.TurnNumber) n += MatchConfig.WorldEndCells;
            }
            return n;
        }
    }

    // ------------------------------------------------------------
    //  局面の読み込み（MatchSnapshotIO.Restore。GameSystem.ImportState から）。通知は出さない
    // ------------------------------------------------------------

    public void RestoreTranscendent(PlayerType owner, bool unlocked, bool used)
    {
        transcendentUnlocked[(int)owner] = unlocked;
        transcendentUsed[(int)owner] = used;
    }

    public void RestoreWorldEnd(PlayerType owner, int? since)
    {
        worldEndSince[(int)owner] = since;
    }

    public void RestoreFallenInitial(PlayerType owner, IEnumerable<FallenPieceRecord> records)
    {
        List<FallenPieceRecord> list = fallenInitial[(int)owner];
        list.Clear();
        if (records != null) list.AddRange(records);
    }

    // 千日手の「同じ局面」の判定用に、超常魔法の状態を文字列にする
    // （Prototype の hash.js と同じく、解放・使用・世界の終わり・倒された初期配置の駒の種類）
    public void AppendKey(System.Text.StringBuilder sb)
    {
        sb.Append('$');
        for (int i = 0; i < 2; i++)
        {
            sb.Append(transcendentUnlocked[i] ? 'u' : '-');
            sb.Append(transcendentUsed[i] ? 'x' : '-');
            sb.Append(worldEndSince[i].HasValue ? worldEndSince[i].Value.ToString() : "-").Append(',');
            foreach (FallenPieceRecord f in fallenInitial[i]) sb.Append((int)f.Type).Append('.');
            sb.Append(';');
        }
    }
}
