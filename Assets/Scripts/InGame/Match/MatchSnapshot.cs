using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

// 対局の局面（Prototype の Game.exportState / importState の状態と、src/core/hash.js の hashState）。
// 盤・駒・マスの状態・持ち駒・エナジー・手番・ターン数・超常魔法・倒された初期配置の駒・乱数の状態などを、
// JsonUtility でそのまま JSON にできるデータにしたもの。GameSystem.CaptureSnapshot で作り、GameSystem.ImportState で読み込む。
//
//  ・Hash … 盤面のハッシュ（FNV-1a 32bit の 16 進 8 桁）。棋譜の各行動に添えて、再生で盤面がずれていないかを確かめる。
//    千日手の記録（positions）と作った時刻は入れない。Unity の中で決定的になるよう、文字列はすべて整数と記号だけで作る
//    （float・ToString の書式・Dictionary の並び・GetHashCode に頼らない）。
//    **棋譜の互換性のため、ハッシュに入れる項目と並び（HashText）は変えないこと**。項目を足すときは、
//    今までの棋譜のハッシュが変わらないよう、その状態があるときだけ最後に足す（Prototype の hash.js と同じ決め方）
//  ・時間停止中の予定（timeStop）はハッシュには入るが、時間停止中の局面は書き出せない（読み込みでは元に戻せないため）
//
// 座標は GamePiece の X・Y と同じ（x = 段、y = 筋。Prototype とは逆）
[System.Serializable]
public class SnapshotPiece
{
    public int x;
    public int y;
    public PieceType type;
    public int owner;          // (int)PlayerType
    public bool promoted;
    public int moveCount;      // 必殺技のチャージ
    public bool shield;
    public bool phalanx;
    public bool undead;
    public bool initial;       // 初期配置の駒（倒されると FallenInitial に記録される）
}

[System.Serializable]
public class SnapshotCell
{
    public CellState state;
    public int burnUntil = -1; // 炎上が終わるターン（炎上中でなければ -1）
    public int trap = -1;      // 落とし穴の持ち主（(int)PlayerType。なければ -1）
}

[System.Serializable]
public class SnapshotHand
{
    public List<int> counts = new List<int>(); // PieceRules.HandOrder の順の枚数
}

[System.Serializable]
public class SnapshotFallen
{
    public int owner;
    public PieceType type;
    public DefeatKind kind;
    public int turn;
}

// 千日手の判定用の局面の記録（GameSystem.positionHistory。ハッシュには入れない）
[System.Serializable]
public class SnapshotPosition
{
    public string key = "";
    public int side;
    public bool inCheck;
}

// 時間停止中の状態（ハッシュ用。書き出しはしない）
[System.Serializable]
public class SnapshotTimeStop
{
    public bool active;
    public int owner;
    public int total;
    public int remaining;
    public bool ending;
    public bool justStarted;
    public List<SnapshotPending> pending = new List<SnapshotPending>();
    public List<SnapshotPiece> held = new List<SnapshotPiece>(); // 盤のデータから外して、その場に残している駒
}

[System.Serializable]
public class SnapshotPending
{
    public TimeStopKind kind;
    public int x;
    public int y;
    public bool done;
    public bool undone;  // もとになった移動が盾で押し戻されて取り消された
}

[System.Serializable]
public class MatchSnapshot
{
    public const string Format = "ultimate-shogi-unity-state";
    public const int Version = 1;

    public string format = Format;
    public int version = Version;
    public string createdAt = "";

    public uint seed;
    public uint rngState;
    public List<KifuLoadout> loadouts = new List<KifuLoadout>();

    public int current;        // 手番のプレイヤー（(int)PlayerType）
    public int turnNumber;
    public int moveNumber;
    public bool gameOver;
    public bool extraTurn;     // 2回行動で、この手番がもう 1 回続く

    public List<int> energy = new List<int>();             // [先手, 後手]
    public List<SnapshotHand> hands = new List<SnapshotHand>();
    public List<SnapshotPiece> pieces = new List<SnapshotPiece>();   // 盤の添字（GameHelper.CalcPanelNum）の順
    public List<SnapshotCell> cells = new List<SnapshotCell>();      // 81 マス（盤の添字の順）

    public List<bool> transcendentUnlocked = new List<bool>();
    public List<bool> transcendentUsed = new List<bool>();
    public List<int> worldEndSince = new List<int>();     // 世界の終わりを発動したターン（未発動は -1）
    public List<SnapshotFallen> fallen = new List<SnapshotFallen>();

    public SnapshotTimeStop timeStop = new SnapshotTimeStop();
    public List<SnapshotPosition> positions = new List<SnapshotPosition>();

    // 書き出したときのハッシュ（読み込んだ局面のハッシュと比べて、状態の入れ忘れを見つける）
    public string hash = "";

    // ------------------------------------------------------------
    //  ハッシュ（Prototype の hashState）
    // ------------------------------------------------------------

    public string Hash => Fnv1a(HashText());

    // ハッシュにする文字列。並びと区切りは変えないこと（今までの棋譜のハッシュが変わる）
    public string HashText()
    {
        StringBuilder sb = new StringBuilder(1024);
        sb.Append("c").Append(current).Append("|t").Append(turnNumber).Append("|m").Append(moveNumber)
          .Append("|g").Append(B(gameOver)).Append("|x").Append(B(extraTurn));
        sb.Append("|r").Append(rngState.ToString(CultureInfo.InvariantCulture));

        sb.Append("|e");
        foreach (int e in energy) sb.Append(e).Append(',');

        sb.Append("|h");
        foreach (SnapshotHand h in hands)
        {
            foreach (int n in h.counts) sb.Append(n).Append(',');
            sb.Append(';');
        }

        sb.Append("|p");
        foreach (SnapshotPiece p in pieces) AppendPiece(sb, p);

        sb.Append("|q");
        foreach (SnapshotCell c in cells)
        {
            sb.Append((int)c.state);
            if (c.burnUntil >= 0) sb.Append('f').Append(c.burnUntil);
            if (c.trap >= 0) sb.Append('t').Append(c.trap);
            sb.Append(',');
        }

        sb.Append("|s");
        for (int i = 0; i < 2; i++)
        {
            sb.Append(B(At(transcendentUnlocked, i))).Append(B(At(transcendentUsed, i)))
              .Append(i < worldEndSince.Count ? worldEndSince[i] : -1).Append(';');
        }

        sb.Append("|f");
        foreach (SnapshotFallen f in fallen) sb.Append(f.owner).Append('.').Append((int)f.type).Append('.').Append((int)f.kind).Append('.').Append(f.turn).Append(',');

        if (timeStop != null && timeStop.active)
        {
            sb.Append("|z").Append(timeStop.owner).Append('.').Append(timeStop.total).Append('.').Append(timeStop.remaining)
              .Append('.').Append(B(timeStop.ending)).Append(B(timeStop.justStarted)).Append(':');
            foreach (SnapshotPending e in timeStop.pending)
            {
                sb.Append((int)e.kind).Append('.').Append(e.x).Append('.').Append(e.y).Append('.').Append(B(e.done)).Append(B(e.undone)).Append(',');
            }
            sb.Append(':');
            foreach (SnapshotPiece p in timeStop.held) AppendPiece(sb, p);
        }
        return sb.ToString();
    }

    static void AppendPiece(StringBuilder sb, SnapshotPiece p)
    {
        sb.Append(p.x).Append('.').Append(p.y).Append('.').Append((int)p.type).Append('.').Append(p.owner).Append(B(p.promoted))
          .Append(p.moveCount).Append('.').Append(B(p.shield)).Append(B(p.phalanx)).Append(B(p.undead)).Append(B(p.initial)).Append(',');
    }

    static char B(bool v) => v ? '1' : '0';

    static bool At(List<bool> list, int i) => list != null && i < list.Count && list[i];

    // FNV-1a 32bit の 16 進 8 桁（Prototype の hashState と同じ計算。文字は UTF-16 のコード単位ごと）
    public static string Fnv1a(string text)
    {
        uint h = 0x811c9dc5u;
        unchecked
        {
            for (int i = 0; i < text.Length; i++)
            {
                h ^= text[i];
                h *= 0x01000193u;
            }
        }
        return h.ToString("x8", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------
    //  JSON
    // ------------------------------------------------------------

    public string ToJson()
    {
        return JsonUtility.ToJson(this, true) + "\n";
    }

    // 保存するファイル名（ultimate-shogi-state-日時.json。日時は書き出した時刻（UTC））
    public string FileName => $"ultimate-shogi-state-{KifuFile.Stamp(createdAt)}.json";

    // JSON 文字列を局面として読む。形式が違えば理由つきの KifuFormatException
    public static MatchSnapshot Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new KifuFormatException("ファイルが空です");
        KifuHeader header = KifuHeader.Read(text);
        if (header == null) throw new KifuFormatException("JSON として読めません");
        if (header.format == KifuFile.Format) throw new KifuFormatException("棋譜のファイルです（「棋譜を読み込んで再生」で開いてください）");
        if (header.format != Format) throw new KifuFormatException("この対局の局面のファイルではありません");
        if (header.version != Version) throw new KifuFormatException($"対応していない局面の版です（version {header.version}）");

        MatchSnapshot s;
        try
        {
            s = JsonUtility.FromJson<MatchSnapshot>(text);
        }
        catch (System.ArgumentException)
        {
            throw new KifuFormatException("JSON として読めません");
        }
        if (s == null || s.loadouts == null || s.loadouts.Count != 2 || s.pieces == null || s.cells == null || s.cells.Count != BoardCells.Size * BoardCells.Size
            || s.hands == null || s.hands.Count != 2 || s.energy == null || s.energy.Count != 2)
        {
            throw new KifuFormatException("局面の中身が壊れています");
        }
        if (s.gameOver) throw new KifuFormatException("対局が終わった局面は読み込めません");
        if (s.timeStop != null && s.timeStop.active) throw new KifuFormatException("時間停止中の局面は読み込めません");
        if (s.current != 0 && s.current != 1) throw new KifuFormatException("局面の手番が壊れています");
        foreach (SnapshotPiece p in s.pieces)
        {
            if (p == null || !BoardCells.InBounds(p.x, p.y) || (p.owner != 0 && p.owner != 1)) throw new KifuFormatException("局面の駒が壊れています");
        }
        foreach (KifuLoadout l in s.loadouts)
        {
            if (l.skills == null) l.skills = new List<string>();
        }
        if (s.fallen == null) s.fallen = new List<SnapshotFallen>();
        if (s.positions == null) s.positions = new List<SnapshotPosition>();
        if (s.transcendentUnlocked == null) s.transcendentUnlocked = new List<bool>();
        if (s.transcendentUsed == null) s.transcendentUsed = new List<bool>();
        if (s.worldEndSince == null) s.worldEndSince = new List<int>();
        if (s.timeStop == null) s.timeStop = new SnapshotTimeStop();
        return s;
    }
}
