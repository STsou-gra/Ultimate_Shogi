using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 盤面のスナップショット（Prototype の src/core/state.js の GameState に当たる、GameObject に依存しない対局の状態）。
// 駒・マスの状態・持ち駒・エナジー・必殺技のチャージ・盾などを持つ。GameSystem.CaptureSnapshot で今の対局から作り、
// BoardSimulator で行動（MatchAction）を当てはめる（演出なし）。CPU の先読み・棋譜・照合に使う。JsonUtility でそのまま JSON にできる。
//
// 座標は GamePiece と同じ（x = 段、y = 筋）。マスの添字は GameHelper.CalcPanelNum(x, y)（= x + y * 9）。
// プレイヤーは (int)PlayerType（0 = Player1・1 = Player2）。
// 時間停止中に作ったスナップショットは、合法手（予定のある駒は動かせない・予定の駒が残るマスには打てない）は分かるが、
// 行動を当てはめるシミュレーションはしない（BoardSimulator.Apply が断る）
[System.Serializable]
public class SnapshotPiece
{
    public PieceType type;
    public int owner;              // (int)PlayerType
    public int x;
    public int y;
    public bool promoted;
    public int moveCount;          // 移動回数（必殺技のチャージ）
    public bool shield;            // 盾状態
    public bool phalanx;           // ファランクス状態
    public bool undead;            // アンデッド
    public bool initial;           // 初期配置の駒（倒されると fallenInitial に記録する）
    public bool frozen;            // 時間停止中：消失・爆発の予定があり、もう動かせない
    public bool pendingPromote;    // 時間停止中：成りの予定がある（もう成れない）

    public PlayerType Owner => (PlayerType)owner;

    public SnapshotPiece Clone()
    {
        return (SnapshotPiece)MemberwiseClone();
    }

    // 比べる・ログ用の短い文字列（例：「先手の歩+ 移動2 盾」）
    public string Describe()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(Owner == PlayerType.Player1 ? "先手の" : "後手の").Append(PieceNames.Short(type));
        if (promoted) sb.Append('+');
        if (moveCount > 0) sb.Append(" 移動").Append(moveCount);
        if (shield) sb.Append(" 盾");
        if (phalanx) sb.Append(" ファランクス");
        if (undead) sb.Append(" アンデッド");
        if (!initial) sb.Append(" (初期配置でない)");
        return sb.ToString();
    }
}

[System.Serializable]
public class SnapshotCell
{
    public CellState state = CellState.Normal;
    public int burnUntil = -1;     // 炎上が終わるターン（炎上中でなければ -1）
    public int trap = -1;          // 落とし穴の持ち主（(int)PlayerType。なければ -1）

    public bool IsBroken => state == CellState.Broken;
    public bool IsBurning => state == CellState.Burning;
    public bool HasTrap => trap >= 0;
    public bool IsHazard => IsBurning || HasTrap;

    public SnapshotCell Clone()
    {
        return (SnapshotCell)MemberwiseClone();
    }

    public string Describe()
    {
        string s = state == CellState.Broken ? "破壊" : state == CellState.Burning ? $"炎上（第{burnUntil}ターンまで）" : "通常";
        return HasTrap ? $"{s}・落とし穴（{(trap == 0 ? "先手" : "後手")}）" : s;
    }
}

[System.Serializable]
public class SnapshotPlayer
{
    public int energy;
    public int[] hand = new int[PieceRules.HandOrder.Length];    // 持ち駒の数（PieceRules.HandOrder の順）
    public List<PieceType> fallenInitial = new List<PieceType>(); // 倒された初期配置の駒（倒された順）
    public bool transcendentUnlocked;                             // 超常魔法を解放した（シミュレーションでは変えない）
    public bool transcendentUsed;                                 // 超常魔法を使った（〃）
    public int worldEndSince = -1;                                // 世界の終わりを発動したターン（未発動なら -1）
    public List<SnapshotSkill> skills = new List<SnapshotSkill>();   // 編成した魔法（編成の枠の順。空の枠は入れない）

    public SnapshotPlayer Clone()
    {
        SnapshotPlayer p = (SnapshotPlayer)MemberwiseClone();
        p.hand = (int[])hand.Clone();
        p.fallenInitial = new List<PieceType>(fallenInitial);
        p.skills = new List<SnapshotSkill>(skills);   // 魔法の定義は書き換えないので同じものを使う
        return p;
    }
}

// 錬成のレシピ（駒の種類とコストだけ）
[System.Serializable]
public class SnapshotRecipe
{
    public PieceType piece;
    public int cost;
}

[System.Serializable]
public class BoardSnapshot
{
    public const int CurrentVersion = 1;
    public const int Draw = 2; // winner：引き分け

    public static readonly int PieceTypeCount = System.Enum.GetValues(typeof(PieceType)).Length;

    public int version = CurrentVersion;

    // 手番
    public int firstPlayer;        // 先手（(int)PlayerType）
    public int current;            // 手番のプレイヤー
    public int turnNumber;         // 第Nターン
    public int moveNumber;         // 第M手
    public bool extraTurn;         // 2回行動：次に手番を使う行動のあとも手番が続く

    // 盤面
    public List<SnapshotPiece> pieces = new List<SnapshotPiece>();       // 盤上の駒（並びに意味はない。マスから引くときは At）
    public SnapshotCell[] cells = NewCells();                            // マスの状態（添字は GameHelper.CalcPanelNum）
    public SnapshotPlayer[] players = { new SnapshotPlayer(), new SnapshotPlayer() };

    // 対局の決まり（スナップショットを作ったときの値）
    public int[] specialMoves = new int[PieceTypeCount];                 // 駒の種類ごとの、必殺技に必要な移動回数（0 = 必殺技なし。添字は (int)PieceType）
    public List<SnapshotRecipe> recipes = new List<SnapshotRecipe>();    // 錬成のレシピ（並びは錬成ボタンの順）
    public bool online;                                                  // オンライン対戦中（錬成・狙撃・必殺技は使えない）

    // 必殺技・魔法をシミュレーションで判定・実行するための情報（SnapshotSkills.cs・BoardSimulator.Skills.cs）。
    // skillsKnown なら、必殺技・魔法の合法手は BoardSimulator が作る（行動を当てはめたあとも分かる）。
    // false なら今までどおり skillActions（スナップショットを作った局面の合法手）だけ
    public bool skillsKnown;
    public SpecialKind[] specialKinds = new SpecialKind[PieceTypeCount];  // 駒の種類ごとの必殺技（添字は (int)PieceType）

    // 対局の乱数（GameRng。天変地異・世界の終わり・アンデッド）のシードと状態。CPU の先読みは、ここを CPU 自身の乱数で置き換えて
    // 「対局の結果に関わる乱数」を先読みしない（Prototype の search.js の simulate）
    public uint rngSeed;
    public uint rngState;

    // 時間停止（停止世界）
    public bool timeStopped;
    public int timeStopOwner = -1;
    public bool timeStopReleasable;                                      // 今「解除」できる状態か（発動した行動のあと・使い切る前）
    public int timeStopRemaining;                                        // 時間停止の残り回数
    public bool timeStopJustStarted;                                     // 停止世界を発動した行動の処理中（この行動は回数に数えない）
    public List<MatchSquare> heldSquares = new List<MatchSquare>();      // 予定の駒が残っているマス（持ち駒を打てない）

    // 勝敗
    public bool gameOver;
    public int winner = -1;        // (int)PlayerType・引き分けは Draw

    // 必殺技・魔法の合法手（スナップショットを作った局面のものだけ。盤面の効果はシミュレーションしないので、行動を当てはめたら分からなくなる）
    public bool skillActionsKnown;
    public List<MatchAction> skillActions = new List<MatchAction>();

    [System.NonSerialized] private SnapshotPiece[] grid;

    static SnapshotCell[] NewCells()
    {
        SnapshotCell[] c = new SnapshotCell[BoardCells.Size * BoardCells.Size];
        for (int i = 0; i < c.Length; i++) c[i] = new SnapshotCell();
        return c;
    }

    // ------------------------------------------------------------
    //  読む
    // ------------------------------------------------------------

    public PlayerType Current => (PlayerType)current;
    public PlayerType FirstPlayer => (PlayerType)firstPlayer;
    public PlayerType SecondPlayer => BoardZones.Opponent(FirstPlayer);

    public SnapshotPlayer Player(PlayerType p) => players[(int)p];

    // 盤上の駒をマスの添字で引く配列（読むだけ。書き換えは Place / Remove / MovePiece）
    public SnapshotPiece[] Grid
    {
        get
        {
            if (grid == null) RebuildGrid();
            return grid;
        }
    }

    void RebuildGrid()
    {
        grid = new SnapshotPiece[BoardCells.Size * BoardCells.Size];
        foreach (SnapshotPiece p in pieces)
        {
            int i = GameHelper.CalcPanelNum(p.x, p.y);
            if (i >= 0) grid[i] = p;
        }
    }

    // (x, y) の駒（盤の外・空きなら null）
    public SnapshotPiece At(int x, int y)
    {
        int i = GameHelper.CalcPanelNum(x, y);
        return i < 0 ? null : Grid[i];
    }

    // (x, y) のマス（盤の外なら null）
    public SnapshotCell Cell(int x, int y)
    {
        int i = GameHelper.CalcPanelNum(x, y);
        return i < 0 ? null : cells[i];
    }

    public bool IsBroken(int x, int y)
    {
        SnapshotCell c = Cell(x, y);
        return c != null && c.IsBroken;
    }

    public bool IsHazard(int x, int y)
    {
        SnapshotCell c = Cell(x, y);
        return c != null && c.IsHazard;
    }

    // p が盤上にいるか
    public bool Contains(SnapshotPiece p)
    {
        return p != null && At(p.x, p.y) == p;
    }

    public int HandCount(PlayerType owner, PieceType type)
    {
        int i = HandIndex(type);
        return i < 0 ? 0 : Player(owner).hand[i];
    }

    public static int HandIndex(PieceType type)
    {
        return System.Array.IndexOf(PieceRules.HandOrder, type);
    }

    public int KingCount(PlayerType owner)
    {
        int n = 0;
        foreach (SnapshotPiece p in pieces)
        {
            if (p.Owner == owner && p.type == PieceType.King) n++;
        }
        return n;
    }

    // 駒の種類の必殺技に必要な移動回数（0 = 必殺技なし）
    public int SpecialMovesOf(PieceType type)
    {
        int i = (int)type;
        return specialMoves != null && i >= 0 && i < specialMoves.Length ? specialMoves[i] : 0;
    }

    // 必殺技を使えるだけ移動したか（GameSystem.RefreshSpecialReady と同じ。アンデッドは必殺技を使えない）
    public bool IsSpecialReady(SnapshotPiece p)
    {
        int need = SpecialMovesOf(p.type);
        return !p.undead && need > 0 && p.moveCount >= need;
    }

    // ------------------------------------------------------------
    //  書き換える（BoardSimulator・CaptureSnapshot から）
    // ------------------------------------------------------------

    // 駒を盤に置く（p.x・p.y のマス。そこにいた駒は上書きされるので、空きマスに置くこと）
    public void Place(SnapshotPiece p)
    {
        int i = GameHelper.CalcPanelNum(p.x, p.y);
        if (i < 0) return;
        SnapshotPiece old = Grid[i];
        if (old != null) pieces.Remove(old);
        pieces.Add(p);
        grid[i] = p;
    }

    // 駒を盤から取り除く
    public void Remove(SnapshotPiece p)
    {
        if (!Contains(p)) return;
        grid[GameHelper.CalcPanelNum(p.x, p.y)] = null;
        pieces.Remove(p);
    }

    // 駒を (x, y) へ動かす（移動先は空いていること）
    public void MovePiece(SnapshotPiece p, int x, int y)
    {
        int to = GameHelper.CalcPanelNum(x, y);
        if (!Contains(p) || to < 0) return;
        grid[GameHelper.CalcPanelNum(p.x, p.y)] = null;
        p.x = x;
        p.y = y;
        grid[to] = p;
    }

    // ------------------------------------------------------------
    //  写し・JSON
    // ------------------------------------------------------------

    public BoardSnapshot Clone()
    {
        BoardSnapshot s = (BoardSnapshot)MemberwiseClone();
        s.pieces = new List<SnapshotPiece>(pieces.Count);
        foreach (SnapshotPiece p in pieces) s.pieces.Add(p.Clone());
        s.cells = new SnapshotCell[cells.Length];
        for (int i = 0; i < cells.Length; i++) s.cells[i] = cells[i].Clone();
        s.players = new SnapshotPlayer[players.Length];
        for (int i = 0; i < players.Length; i++) s.players[i] = players[i].Clone();
        s.specialMoves = (int[])specialMoves.Clone();
        s.specialKinds = (SpecialKind[])specialKinds.Clone();
        s.recipes = new List<SnapshotRecipe>(recipes);           // レシピは書き換えないので同じものを使う
        s.heldSquares = new List<MatchSquare>(heldSquares);
        s.skillActions = new List<MatchAction>(skillActions.Count);
        foreach (MatchAction a in skillActions) s.skillActions.Add(a.Clone());
        s.grid = null;
        return s;
    }

    public string ToJson(bool pretty = false)
    {
        return JsonUtility.ToJson(this, pretty);
    }

    // 読めなければ null
    public static BoardSnapshot FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            BoardSnapshot s = JsonUtility.FromJson<BoardSnapshot>(json);
            if (s == null || s.version > CurrentVersion) return null;
            s.grid = null;
            return s;
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }

    // ------------------------------------------------------------
    //  比べる（シミュレーションの予測と実際の盤面の照合・JSON の往復の確認）
    // ------------------------------------------------------------

    // other との違いを文で返す（同じなら空）。
    // all = false：シミュレーションで変わる項目だけ（手番・盤面・マス・持ち駒・エナジー・倒された初期配置の駒・2回行動・勝敗）を比べる
    // all = true ：記録しているすべての項目（超常魔法・時間停止・決まり・必殺技と魔法の合法手も）を比べる
    public List<string> Diff(BoardSnapshot other, bool all = false)
    {
        List<string> d = new List<string>();
        if (other == null)
        {
            d.Add("比べる相手がない");
            return d;
        }
        Same(d, "手番", current, other.current);
        Same(d, "ターン数", turnNumber, other.turnNumber);
        Same(d, "手数", moveNumber, other.moveNumber);
        Same(d, "2回行動", extraTurn, other.extraTurn);
        Same(d, "終局", gameOver, other.gameOver);
        Same(d, "勝者", winner, other.winner);

        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                string a = PieceKey(At(x, y), all), b = PieceKey(other.At(x, y), all);
                if (a != b) d.Add($"({x}, {y}) の駒: {Describe(At(x, y))} ／ {Describe(other.At(x, y))}");
                SnapshotCell ca = Cell(x, y), cb = other.Cell(x, y);
                if (ca.state != cb.state || ca.trap != cb.trap || (ca.IsBurning && ca.burnUntil != cb.burnUntil))
                {
                    d.Add($"({x}, {y}) のマス: {ca.Describe()} ／ {cb.Describe()}");
                }
            }
        }
        if (pieces.Count != other.pieces.Count) d.Add($"盤上の駒の数: {pieces.Count} ／ {other.pieces.Count}");

        for (int i = 0; i < players.Length; i++)
        {
            SnapshotPlayer pa = players[i], pb = other.players[i];
            string who = i == 0 ? "先手" : "後手";
            Same(d, $"{who}のエナジー", pa.energy, pb.energy);
            for (int h = 0; h < PieceRules.HandOrder.Length; h++)
            {
                Same(d, $"{who}の持ち駒 {PieceNames.Short(PieceRules.HandOrder[h])}", pa.hand[h], pb.hand[h]);
            }
            Same(d, $"{who}の倒された初期配置の駒", string.Join(",", pa.fallenInitial), string.Join(",", pb.fallenInitial));
            if (all)
            {
                Same(d, $"{who}の超常魔法の解放", pa.transcendentUnlocked, pb.transcendentUnlocked);
                Same(d, $"{who}の超常魔法の使用", pa.transcendentUsed, pb.transcendentUsed);
                Same(d, $"{who}の世界の終わり", pa.worldEndSince, pb.worldEndSince);
            }
        }

        if (all)
        {
            Same(d, "先手", firstPlayer, other.firstPlayer);
            Same(d, "必殺技に必要な移動回数", string.Join(",", specialMoves), string.Join(",", other.specialMoves));
            Same(d, "錬成のレシピ", RecipesKey(), other.RecipesKey());
            Same(d, "オンライン", online, other.online);
            Same(d, "時間停止", $"{timeStopped}/{timeStopOwner}/{timeStopReleasable}", $"{other.timeStopped}/{other.timeStopOwner}/{other.timeStopReleasable}");
            Same(d, "予定の駒が残るマス", string.Join(" ", heldSquares), string.Join(" ", other.heldSquares));
            Same(d, "必殺技・魔法の合法手", SkillActionsKey(), other.SkillActionsKey());
        }
        return d;
    }

    static void Same<T>(List<string> d, string label, T a, T b)
    {
        if (!EqualityComparer<T>.Default.Equals(a, b)) d.Add($"{label}: {a} ／ {b}");
    }

    static string PieceKey(SnapshotPiece p, bool all)
    {
        if (p == null) return "";
        string key = $"{p.type}|{p.owner}|{p.promoted}|{p.moveCount}|{p.shield}|{p.phalanx}|{p.undead}|{p.initial}";
        return all ? $"{key}|{p.frozen}|{p.pendingPromote}" : key;
    }

    static string Describe(SnapshotPiece p)
    {
        return p == null ? "なし" : p.Describe();
    }

    string RecipesKey()
    {
        StringBuilder sb = new StringBuilder();
        foreach (SnapshotRecipe r in recipes) sb.Append(r.piece).Append(':').Append(r.cost).Append(' ');
        return sb.ToString();
    }

    string SkillActionsKey()
    {
        if (!skillActionsKnown) return "（なし）";
        List<string> keys = new List<string>();
        foreach (MatchAction a in skillActions) keys.Add(a.Key);
        return string.Join(" ", keys);
    }
}
