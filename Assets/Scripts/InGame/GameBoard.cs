using UnityEngine;
using System.Collections.Generic;

public class PieceSpawnData
{
    public int X;           // 盤面のX座標
    public int Y;           // 盤面のY座標
    public PieceType type;  // 駒の種類 (King, Pawnなど)
    public PlayerType player; // どちらのプレイヤーか
}

[System.Serializable]
struct PieceMap
{
    public PieceType type;
    public GameObject prefab;
}
public class GameBoard : MonoBehaviour
{
    private List<GameSelectPanel> selectPanelArray = new List<GameSelectPanel>();

    // 9x9の盤面を想定。巨人（3×3 マスを占める駒。GiantRules）は、占めるマス全部に同じ駒が入る（駒の X・Y は中央のマス）。
    // 盤の全部を回すときは、巨人を 1 回だけ数えるように Pieces() を使うか、中央のマスかを確かめること
    private GamePiece[] boardData = new GamePiece[81];

    // マスの状態（破壊・炎上・落とし穴）。駒の配列とは別のプレーンなクラスで持つ（添字は駒の配列と同じ）。
    // 書き換えは GameSystem.BreakCell / SetBurning / SetTrap / EnterCell を通すこと
    public BoardCells Cells { get; } = new BoardCells();

    // 時間停止（超常魔法「停止世界」）の状態。時間停止中だけ GameSystem が入れる（それ以外は null）。
    // 盤だけを受け取る判定（持ち駒のルール・魔法の空きマスなど）が、予定の駒が残っているマスを見るのに使う
    public TimeStopState TimeStop { get; set; }

    // 討伐任務のボス（盤の駒の配列には入れない。対局の始めに GameSystem が入れる。討伐任務でなければ null）。
    // 盤だけを受け取る判定（移動・持ち駒のルール・魔法の空きマスなど）が、ボスのマスを見るのに使う
    public BossState Boss { get; set; }

    // 討伐任務の状態（GameSystem が入れる。討伐任務でなければ null）。駒を作るたびに、駒の番号（GamePiece.serial）を作った順に振る
    // （RaidState.nextPieceId。ボスの弓・軍銃兵の照準が駒を選ぶ順・追う駒を指すのに使う）。局面の読み込みの間は null にして、番号を振らない
    public RaidState Raid { get; set; }

    void AssignSerial(GamePiece piece)
    {
        if (piece != null && Raid != null && Raid.active) piece.serial = Raid.nextPieceId++;
    }

    // 防壁の後ろの守り（討伐任務。WallRules.IsShielded）：(x, y) のボスの席の駒が、同じ筋で手前にある防壁に守られているか。
    // 守られた駒は、プレイヤーのどの攻撃も受けない（移動の行き先・狙撃の対象・取り・消失・押し出しのどれにもならない）
    public bool IsShieldedByWall(int x, int y)
    {
        if (Boss == null) return false;
        return WallRules.IsShielded((px, py) =>
        {
            GamePiece q = GetPieceAt(px, py);
            return q != null ? q.type : (PieceType?)null;
        }, x, y);
    }

    // 駒 mover が (x, y) の駒 target のマスに入れないか：防壁に守られた相手の駒（火薬を爆発させる場合を除く。Prototype の board.js の canLandOn）
    public bool IsShieldedTarget(GamePiece mover, int x, int y)
    {
        GamePiece target = GetPieceAt(x, y);
        return target != null && mover != null && target.player != mover.player && !PieceRules.Detonates(mover, target) && IsShieldedByWall(x, y);
    }

    // (x, y) がボスのマスか（倒れたボスはどのマスも占めない）
    public bool BossAt(int x, int y)
    {
        return Boss != null && Boss.Covers(x, y);
    }

    // マスの一覧のうち、ボスに重なる最初のマス（なければ null）
    public Vector2Int? BossOverlap(IEnumerable<Vector2Int> cells)
    {
        if (Boss == null || !Boss.IsAlive || cells == null) return null;
        foreach (Vector2Int c in cells)
        {
            if (Boss.Covers(c.x, c.y)) return c;
        }
        return null;
    }

    // 走り駒の通り道（移動先の手前まで）にボスのマスがあるか（走り駒はボスで止まる。GameHelper.IsPathBlocked の討伐任務の分）
    public bool PathCrossesBoss(int fromX, int fromY, int toX, int toY)
    {
        if (Boss == null || !Boss.IsAlive) return false;
        int dx = toX - fromX, dy = toY - fromY;
        if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) return false;
        int stepX = System.Math.Sign(dx), stepY = System.Math.Sign(dy);
        int x = fromX + stepX, y = fromY + stepY;
        for (int n = 0; n < 10 && (x != toX || y != toY); n++)
        {
            if (Boss.Covers(x, y)) return true;
            x += stepX;
            y += stepY;
        }
        return false;
    }

    // 初期配置の差し替え（討伐任務の編成。GameSystem.Start が UseLayout で入れる）と、盤の準備（Start）が済んだか
    private List<PieceSpawnData> layoutOverride;
    private bool started;

    // 初期配置を layout にする（討伐任務の編成）。盤の準備がもう済んでいれば、並べ直す
    // 盤の準備（Start）がまだでも、ここですぐ並べる（討伐任務：このあとボスの席の初期配置の駒を置き、軍銃兵が照準を選ぶので、
    // 編成の駒が先に盤にいる必要がある。Start では並べ直さない）
    public void UseLayout(List<PieceSpawnData> layout)
    {
        layoutOverride = layout;
        RemoveAllPieces();
        SetupInitialPieces();
    }

    private bool initialPlaced;


    [SerializeField] private List<PieceMap> pieceMapList = new List<PieceMap>();

    void Start()
    {
        GameSelectPanel[] foundPanels = GetComponentsInChildren<GameSelectPanel>(true);
        selectPanelArray.AddRange(foundPanels);
        Debug.Log($"{selectPanelArray.Count}");

        for (int i = 0; i < selectPanelArray.Count; i++)
        {
            var panel = selectPanelArray[i];
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);

            Vector2 location = GameHelper.CalcPanelLocation(x, y);
            panel.transform.localPosition = location;
        }
        AllDeactivePanel();
        started = true;
        if (!initialPlaced) SetupInitialPieces();
    }

    public void ActivePanel(int num)
    {
        if (num < 0 || num >= selectPanelArray.Count)
        {
            return;
        }
        selectPanelArray[num].gameObject.SetActive(true);
    }

    public void AllDeactivePanel()
    {
        foreach (var panel in selectPanelArray)
        {
            panel.gameObject.SetActive(false);
        }
    }
    // 指定座標に駒があるか確認する関数
    public bool HasPieceAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        return (index != -1 && boardData[index] != null);
    }
    void SetupInitialPieces()
    {
        initialPlaced = true;
        List<PieceSpawnData> layout = layoutOverride ?? CreateLayout();
        foreach (PieceSpawnData item in layout)
        {
            int index = GameHelper.CalcPanelNum(item.X, item.Y);

            // ここで本物の駒（MonoBehaviour）を生成する
            GameObject prefab = GetPrefabByType(item.type);
            if (prefab == null)
            {
                Debug.LogError($"Prefab not found for type: {item.type}");
                continue;
            }
            GameObject obj = Instantiate(prefab, transform);

            // データをセット
            GamePiece piece = obj.GetComponent<GamePiece>();
            piece.player = item.player;
            piece.X = item.X; // 座標も忘れずにセット
            piece.Y = item.Y;
            piece.IsInitial = true; // 初期配置の駒（倒されるとプレイヤーごとに記録される）
            piece.Cells = Cells;

            piece.transform.localPosition = GameHelper.CalcPanelLocation(item.X, item.Y);

            if (piece.player == PlayerType.Player2)
            {
                piece.transform.localRotation = Quaternion.Euler(0, 0, 180);
            }
            boardData[index] = piece;
            AssignSerial(piece); // 討伐任務：編成の順に駒の番号を振る
        }
    }

    public GamePiece GetPieceAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index == -1) return null;
        return boardData[index];
    }

    public void UpdateBoardData(int fromX, int fromY, int toX, int toY)
    {
        int fromIndex = GameHelper.CalcPanelNum(fromX, fromY);
        int toIndex = GameHelper.CalcPanelNum(toX, toY);

        boardData[toIndex] = boardData[fromIndex];
        boardData[fromIndex] = null;
    }

    public GamePiece[] GetBoardData()
    {
        return boardData;
    }

    // 盤上の駒（盤の添字の順。巨人は中央のマスで 1 回だけ）
    public List<GamePiece> Pieces()
    {
        List<GamePiece> res = new List<GamePiece>();
        for (int i = 0; i < boardData.Length; i++)
        {
            GamePiece p = boardData[i];
            if (p != null && GameHelper.CalcPanelNum(p.X, p.Y) == i) res.Add(p);
        }
        return res;
    }

    // 駒が占めるマス（ふつうの駒は 1 マス、巨人は中央 (x, y) の 3×3）
    public static List<Vector2Int> Footprint(GamePiece piece, int x, int y)
    {
        List<Vector2Int> res = new List<Vector2Int>();
        if (!PieceRules.IsGiant(piece))
        {
            res.Add(new Vector2Int(x, y));
            return res;
        }
        foreach (MatchSquare c in GiantRules.Cells(x, y)) res.Add(c);
        return res;
    }

    // 駒を占めるマス全部に書く（中央 (x, y)）
    void PutCells(GamePiece piece, int x, int y)
    {
        foreach (Vector2Int c in Footprint(piece, x, y))
        {
            int i = GameHelper.CalcPanelNum(c.x, c.y);
            if (i != -1) boardData[i] = piece;
        }
    }

    // 駒がいるマスを空ける（piece がいるマスだけ）
    void ClearCells(GamePiece piece)
    {
        foreach (Vector2Int c in Footprint(piece, piece.X, piece.Y))
        {
            int i = GameHelper.CalcPanelNum(c.x, c.y);
            if (i != -1 && boardData[i] == piece) boardData[i] = null;
        }
    }

    // 巨人を中央 (cx, cy) へ動かす（データだけ。移動先のマスは空いていること。火薬のマスに重なるときは、呼び出し側が先に火薬を外す）
    public void MoveGiant(GamePiece giant, int cx, int cy)
    {
        if (giant == null) return;
        ClearCells(giant);
        giant.X = cx;
        giant.Y = cy;
        PutCells(giant, cx, cy);
    }

    // 盤から駒を取り除いて破棄する（盤面の操作だけ）。
    // 対局中に駒を倒すときは、ここを直接呼ばずに GameSystem.CapturePiece（取り）/ VanishPiece（消失）を通すこと
    // （エナジー配分・持ち駒・初期配置の駒の記録・盾などの割り込みをそちらで行う）
    public bool RemovePieceAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index != -1 && boardData[index] != null)
        {
            GamePiece piece = boardData[index];
            // 消される駒の種類をチェック
            bool isKing = (piece.type == PieceType.King);
            // データ（配列）を空にする（巨人は占めるマス全部）
            ClearCells(piece);
            boardData[index] = null;
            // 見た目（GameObject）を削除
            Destroy(piece.gameObject);
            return isKing; // 玉が取られたかどうかを返す
        }
        return false;
    }

    // 駒を盤のデータから外す（GameObject は残す。時間停止の「予定の駒」として、その場に灰色で残すとき）
    public void DetachPiece(GamePiece piece)
    {
        if (piece == null) return;
        ClearCells(piece);
    }

    // 外していた駒を (x, y) に戻す（盾で取りが防がれたなど。そのマスは空いていること）
    public void AttachPiece(GamePiece piece, int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (piece == null || index == -1) return;
        piece.X = x;
        piece.Y = y;
        PutCells(piece, x, y);
    }

    // 盤のデータから外していた駒を破棄する（盤のデータは変えない。そのマスに今いる別の駒は消さない）
    public void DestroyDetached(GamePiece piece)
    {
        if (piece != null) Destroy(piece.gameObject);
    }

    // 駒を作って (x, y) に置く（盤面の操作だけ）。作った駒を返す（置けなければ null）。
    // 対局中に持ち駒を打つときは、置いたあとに GameSystem.EnterCell を呼ぶこと（炎上のマスなら消失する）。
    // attach が false なら盤のデータには入れない（局面の読み込みで、時間停止の「予定の駒」をその場に置くとき）
    public GamePiece SpawnPiece(PieceType type, PlayerType player, int x, int y, bool attach = true)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index == -1) return null;

        GameObject prefab = GetPrefabByType(type);
        GamePiece piece;
        if (prefab != null)
        {
            piece = Instantiate(prefab, transform).GetComponent<GamePiece>();
        }
        else if (PieceRules.IsMinion(type))
        {
            // ボスの席の駒（小赤鬼・軍歩兵・軍銃兵・防壁）はプレハブを持たない：実行時に絵を作る（Piece/PieceKooni.cs・PieceSoldier.cs）
            piece = type == PieceType.Kooni ? (GamePiece)PieceKooni.Create(transform) : PieceSoldier.Create(transform, type);
        }
        else
        {
            Debug.LogError($"Prefab not found for type: {type}");
            return null;
        }
        piece.player = player;
        piece.X = x;
        piece.Y = y;
        piece.Cells = Cells;
        piece.transform.localPosition = GameHelper.CalcPanelLocation(x, y);

        // 後手の駒は逆向き（手下は顔の絵なので回さない）
        if (piece.player == PlayerType.Player2 && !PieceRules.IsMinion(type))
        {
            piece.transform.localRotation = Quaternion.Euler(0, 0, 180);
        }
        if (PieceRules.IsGiant(piece)) piece.hp = GiantRules.MaxHp; // 巨人は HP 3（局面の読み込みでは ApplyPiece が書き換える）
        if (PieceRules.IsWallPiece(type)) piece.hp = WallRules.MaxHp; // 防壁も HP 3
        AssignSerial(piece); // 討伐任務：作った順に駒の番号を振る（打った駒・呼び出された手下・アンデッド）
        if (attach) PutCells(piece, x, y);
        return piece;
    }

    // 盤上の駒をすべて取り除いて破棄する（局面の読み込みで盤を作り直すとき。盤のデータから外している駒は呼び出し側が消す）
    public void RemoveAllPieces()
    {
        HashSet<GamePiece> destroyed = new HashSet<GamePiece>(); // 巨人は 9 マスにいるので 1 回だけ
        for (int i = 0; i < boardData.Length; i++)
        {
            if (boardData[i] != null && destroyed.Add(boardData[i])) Destroy(boardData[i].gameObject);
            boardData[i] = null;
        }
    }

    private GameObject GetPrefabByType(PieceType type)
    {
        foreach (var map in pieceMapList)
        {
            if (map.type == type) return map.prefab;
        }
        return null;
    }

    // ========================================================
    // スキル用：指定したプレイヤーの特定の駒がどこにあるかを探す
    // ========================================================
    public Vector2Int FindPiece(PlayerType player, PieceType type)
    {
        // 9x9の盤面を全スキャン
        for (int x = 0; x < 9; x++)
        {
            for (int y = 0; y < 9; y++)
            {
                int index = GameHelper.CalcPanelNum(x, y);
                GamePiece piece = boardData[index];

                // 指定されたプレイヤーの、指定された種類の駒が見つかったらその座標を返す
                if (piece != null && piece.player == player && piece.type == type)
                {
                    return new Vector2Int(x, y);
                }
            }
        }
        // 見つからなかった場合は、エラー値として (-1, -1) を返す
        return new Vector2Int(-1, -1);
    }

    // ========================================================
    // 超常魔法「天変地異」用：駒をまとめて並べ替える（データだけ。見た目は MagicFx が飛ばす）。
    // from[i] にいた駒が to[i] へ移る（from と to は同じマスの集まりを並べ替えたもの）。駒の X・Y も更新する
    // ========================================================
    public void RearrangePieces(IReadOnlyList<Vector2Int> from, IReadOnlyList<Vector2Int> to)
    {
        if (from == null || to == null || from.Count != to.Count) return;
        GamePiece[] before = (GamePiece[])boardData.Clone();
        foreach (Vector2Int f in from)
        {
            int index = GameHelper.CalcPanelNum(f.x, f.y);
            if (index >= 0) boardData[index] = null;
        }
        for (int i = 0; i < from.Count; i++)
        {
            int src = GameHelper.CalcPanelNum(from[i].x, from[i].y);
            int dst = GameHelper.CalcPanelNum(to[i].x, to[i].y);
            if (src < 0 || dst < 0) continue;
            GamePiece piece = before[src];
            boardData[dst] = piece;
            if (piece != null)
            {
                piece.X = to[i].x;
                piece.Y = to[i].y;
            }
        }
    }

    // ========================================================
    // スキル用：盤面上の2つの座標にある駒を「データ」も「見た目」も入れ替える
    // ========================================================
    public void SwapPiecePosition(Vector2Int posA, Vector2Int posB)
    {
        int indexA = GameHelper.CalcPanelNum(posA.x, posA.y);
        int indexB = GameHelper.CalcPanelNum(posB.x, posB.y);

        // 1. boardData（配列データ）の入れ替え
        GamePiece pieceA = boardData[indexA];
        GamePiece pieceB = boardData[indexB];

        boardData[indexA] = pieceB;
        boardData[indexB] = pieceA;

        // 2. それぞれの駒の内部座標(X, Y)の更新と、3D上の見た目(localPosition)の移動
        if (pieceA != null)
        {
            pieceA.X = posB.x;
            pieceA.Y = posB.y;
            pieceA.MoveTo(GameHelper.CalcPanelLocation(posB.x, posB.y)); // 見た目をBの場所へ
        }

        if (pieceB != null)
        {
            pieceB.X = posA.x;
            pieceB.Y = posA.y;
            pieceB.MoveTo(GameHelper.CalcPanelLocation(posA.x, posA.y)); // 見た目をAの場所へ
        }
    }

    public List<PieceSpawnData> CreateLayout()
    {
        List<PieceSpawnData> layout = new List<PieceSpawnData>();
        // 例：歩 (Fu) を並べる
        for (int i = 0; i < 9; i++)
        {
            layout.Add(new PieceSpawnData { X = 2, Y = i, player = PlayerType.Player1, type = PieceType.Pawn });
            layout.Add(new PieceSpawnData { X = 6, Y = i, player = PlayerType.Player2, type = PieceType.Pawn });
        }

        // 玉 (King)
        layout.Add(new PieceSpawnData { X = 0, Y = 4, player = PlayerType.Player1, type = PieceType.King });
        layout.Add(new PieceSpawnData { X = 8, Y = 4, player = PlayerType.Player2, type = PieceType.King });

        // 他の駒（飛車、角、金、銀...）も同様に Add していきます
        layout.Add(new PieceSpawnData { X = 1, Y = 7, player = PlayerType.Player1, type = PieceType.Rook });
        layout.Add(new PieceSpawnData { X = 7, Y = 1, player = PlayerType.Player2, type = PieceType.Rook });
        layout.Add(new PieceSpawnData { X = 1, Y = 1, player = PlayerType.Player1, type = PieceType.Bishop });
        layout.Add(new PieceSpawnData { X = 7, Y = 7, player = PlayerType.Player2, type = PieceType.Bishop });

        layout.Add(new PieceSpawnData { X = 0, Y = 3, player = PlayerType.Player1, type = PieceType.GoldGeneral });
        layout.Add(new PieceSpawnData { X = 0, Y = 5, player = PlayerType.Player1, type = PieceType.GoldGeneral });
        layout.Add(new PieceSpawnData { X = 8, Y = 3, player = PlayerType.Player2, type = PieceType.GoldGeneral });
        layout.Add(new PieceSpawnData { X = 8, Y = 5, player = PlayerType.Player2, type = PieceType.GoldGeneral });

        layout.Add(new PieceSpawnData { X = 0, Y = 2, player = PlayerType.Player1, type = PieceType.SilverGeneral });
        layout.Add(new PieceSpawnData { X = 0, Y = 6, player = PlayerType.Player1, type = PieceType.SilverGeneral });
        layout.Add(new PieceSpawnData { X = 8, Y = 2, player = PlayerType.Player2, type = PieceType.SilverGeneral });
        layout.Add(new PieceSpawnData { X = 8, Y = 6, player = PlayerType.Player2, type = PieceType.SilverGeneral });

        layout.Add(new PieceSpawnData { X = 0, Y = 1, player = PlayerType.Player1, type = PieceType.Knight });
        layout.Add(new PieceSpawnData { X = 0, Y = 7, player = PlayerType.Player1, type = PieceType.Knight });
        layout.Add(new PieceSpawnData { X = 8, Y = 1, player = PlayerType.Player2, type = PieceType.Knight });
        layout.Add(new PieceSpawnData { X = 8, Y = 7, player = PlayerType.Player2, type = PieceType.Knight });

        layout.Add(new PieceSpawnData { X = 0, Y = 0, player = PlayerType.Player1, type = PieceType.Lance });
        layout.Add(new PieceSpawnData { X = 0, Y = 8, player = PlayerType.Player1, type = PieceType.Lance });
        layout.Add(new PieceSpawnData { X = 8, Y = 8, player = PlayerType.Player2, type = PieceType.Lance });
        layout.Add(new PieceSpawnData { X = 8, Y = 0, player = PlayerType.Player2, type = PieceType.Lance });
        return layout;
    }
}
