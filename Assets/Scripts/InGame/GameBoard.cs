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

    private GamePiece[] boardData = new GamePiece[81]; // 9x9の盤面を想定


    [SerializeField] private List<PieceMap> pieceMapList = new List<PieceMap>();

    // 消失の理由（PieceVanished・演出で使う）
    public const string ReasonBurning = "炎上";
    public const string ReasonTrap = "落とし穴";
    public const string ReasonBroken = "マス破壊";

    /// <summary>マスの状態（破壊・炎上・落とし穴）。駒の配列 boardData とは別に持つ</summary>
    public BoardCells Cells { get; } = new BoardCells();

    /// <summary>駒が消失した（盤のデータからはもう外れている）。引数は消えた駒と理由（Reason〜）</summary>
    public event System.Action<GamePiece, string> PieceVanished;

    private BoardCellView cellView;
    private GameSystem gameSystem;

    // マスの炎上の期限を数えるターン数（1P・2P が1回ずつ行動して1ターン）
    private int CurrentTurnNumber => gameSystem != null ? gameSystem.TurnNumber : 1;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();

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
        SetupInitialPieces();

        cellView = gameObject.AddComponent<BoardCellView>();
        cellView.Init(this);
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
        List<PieceSpawnData> layout = CreateLayout();
        foreach (PieceSpawnData item in layout)
        {
            int index = GameHelper.CalcPanelNum(item.X, item.Y);

            // ここで本物の駒（MonoBehaviour）を生成する
            GameObject prefab = GetPrefabByType(item.type);
            GameObject obj = Instantiate(prefab, transform);

            // データをセット
            GamePiece piece = obj.GetComponent<GamePiece>();
            piece.player = item.player;
            piece.X = item.X; // 座標も忘れずにセット
            piece.Y = item.Y;

            piece.transform.localPosition = GameHelper.CalcPanelLocation(item.X, item.Y);

            if (piece.player == PlayerType.Player2)
            {
                piece.transform.localRotation = Quaternion.Euler(0, 0, 180);
            }
            boardData[index] = piece;
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

    public bool RemovePieceAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index != -1 && boardData[index] != null)
        {
            // 消される駒の種類をチェック
            bool isKing = (boardData[index].type == PieceType.King);
            // 見た目（GameObject）を削除
            Destroy(boardData[index].gameObject);
            // データ（配列）を空にする
            boardData[index] = null;
            return isKing; // 玉が取られたかどうかを返す
        }
        return false;
    }

    public void SpawnPiece(PieceType type, PlayerType player, int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index == -1) return;

        GameObject prefab = GetPrefabByType(type);
        if (prefab == null)
        {
            Debug.LogError($"Prefab not found for type: {type}");
            return;
        }

        GameObject obj = Instantiate(prefab, transform);
        GamePiece piece = obj.GetComponent<GamePiece>();
        piece.player = player;
        piece.X = x;
        piece.Y = y;
        piece.transform.localPosition = GameHelper.CalcPanelLocation(x, y);

        if (piece.player == PlayerType.Player2)
        {
            piece.transform.localRotation = Quaternion.Euler(0, 0, 180);
        }
        boardData[index] = piece;
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
    // マスの状態（プロトタイプ src/core/effects.js の breakCell / setBurning / setTrap / enterCell の移植）
    // ========================================================

    /// <summary>
    /// マスを破壊する（対局終了まで戻らない）。そこにいた駒は消失する。
    /// 戻り値: 破壊したか（盤外・すでに破壊されていれば false）
    /// </summary>
    public bool BreakCell(int x, int y)
    {
        if (!Cells.Break(x, y)) return false;
        if (cellView != null) cellView.PlayBreak(x, y);
        GamePiece piece = GetPieceAt(x, y);
        if (piece != null) VanishPiece(piece, ReasonBroken);
        return true;
    }

    /// <summary>
    /// マスを turns ターンのあいだ炎上させる（いまのターンを 1 ターン目として数え、ターンの終わりに期限が来たら鎮火）。
    /// すでにいる駒には影響せず、あとから入った駒が消失する。破壊マスは炎上しない。戻り値: 炎上させたか
    /// </summary>
    public bool SetBurning(int x, int y, int turns)
    {
        if (!Cells.SetBurning(x, y, turns, CurrentTurnNumber)) return false;
        if (cellView != null) cellView.PlayBurning(x, y);
        return true;
    }

    /// <summary>
    /// 落とし穴を仕掛ける（両プレイヤーに見える）。駒がいるマス・破壊マス・炎上マス・すでに落とし穴があるマスには置けない。
    /// 敵味方に関係なく、入った駒は消失して穴も消える。戻り値: 仕掛けたか
    /// </summary>
    public bool SetTrap(int x, int y, PlayerType owner)
    {
        if (HasPieceAt(x, y)) return false;
        if (!Cells.SetTrap(x, y, owner)) return false;
        if (cellView != null) cellView.PlayTrapSet(x, y);
        return true;
    }

    /// <summary>
    /// 駒 piece が (x,y) に入ったときのマスの効果（移動・持ち駒を打つ・スキルで置く、のあとに呼ぶ）。
    /// 駒はもう boardData の (x,y) に置かれている前提。落とし穴なら消失して穴も消える、炎上なら消失する。
    /// 戻り値: そのマスに残れたか（false = 消失した）
    /// </summary>
    public bool EnterCell(GamePiece piece, int x, int y)
    {
        CellData cell = Cells.Get(x, y);
        if (piece == null || cell == null) return true;

        if (cell.HasTrap)
        {
            Cells.ClearTrap(x, y); // 穴の表示は、駒が吸い込まれ終わってから閉じる
            VanishPiece(piece, ReasonTrap);
            return false;
        }
        if (cell.IsBurning)
        {
            VanishPiece(piece, ReasonBurning);
            return false;
        }
        if (cell.IsBroken)
        {
            VanishPiece(piece, ReasonBroken); // 通常は入れない（念のため）
            return false;
        }
        return true;
    }

    /// <summary>
    /// ターンの終わり（1P・2P の両方が行動し終えたとき）に GameSystem から呼ぶ。
    /// finishedTurn までで炎上が終わるマスを鎮火する。戻り値: 鎮火したマス
    /// </summary>
    public List<Vector2Int> OnRoundEnd(int finishedTurn)
    {
        List<Vector2Int> ended = Cells.ExtinguishExpired(finishedTurn);
        if (ended.Count > 0)
        {
            Debug.Log($"{ended.Count} マスの炎が鎮火した");
            if (cellView != null) cellView.PlayFireOut(ended);
        }
        return ended;
    }

    /// <summary>
    /// 駒の消失（マスの効果による。持ち駒にならず、ポイントも入らない）。
    /// 盤のデータからはすぐに外し、見た目は移動のアニメーションが終わってから消えて破棄される
    /// </summary>
    public void VanishPiece(GamePiece piece, string reason)
    {
        if (piece == null) return;
        int index = System.Array.IndexOf(boardData, piece);
        if (index == -1) return; // すでに盤から外れている
        boardData[index] = null;

        int x, y;
        GameHelper.CalcPanelPosition(out x, out y, index);
        Debug.Log($"{piece.player} の {piece.type} が {reason} で消失 ({x},{y})");
        PieceVanished?.Invoke(piece, reason);

        if (cellView != null) cellView.PlayVanish(piece, reason, x, y);
        else Destroy(piece.gameObject);
    }

    /// <summary>デバッグ用：マスを通常に戻す（破壊マスも戻る。対局のルールでは使わない）</summary>
    public void DebugResetCell(int x, int y)
    {
        Cells.DebugReset(x, y);
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
