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

    public int AbilityOnOff = 0;//0がoff
    

    [SerializeField] private List<PieceMap> pieceMapList = new List<PieceMap>();

    void Start()
    {
        GameSelectPanel[] foundPanels = GetComponentsInChildren<GameSelectPanel>(true);
        selectPanelArray.AddRange(foundPanels);
        Debug.Log($"{selectPanelArray.Count}");

        for(int i = 0; i < selectPanelArray.Count; i++)
        {
            var panel = selectPanelArray[i];
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);

            Vector2 location = GameHelper.CalcPanelLocation(x, y);
            panel.transform.localPosition = location;
        }
        AllDeactivePanel();
        SetupInitialPieces();
    }

    public void ActivePanel(int num)
    {
        if(num < 0 || num >= selectPanelArray.Count)
        {
            return;
        }
        selectPanelArray[num].gameObject.SetActive(true);
    }

    public void AllDeactivePanel()
    {
        foreach(var panel in selectPanelArray)
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

    public void ChangeAbilityOnOff(int i){
        AbilityOnOff = i;
    }

    public bool RemovePieceAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        if (index != -1 && boardData[index] != null)
        {
            // 消される駒の種類をチェック
            bool isKing = (boardData[index].type == PieceType.King);
            if(AbilityOnOff == 1){
                // 消される駒の種類をチェック
                if(boardData[index].type == PieceType.Rook){
                    isKing = (boardData[index].type == PieceType.Rook);
                }
            }
            // 見た目（GameObject）を削除
            Destroy(boardData[index].gameObject);
            // データ（配列）を空にする
            boardData[index] = null;
            return isKing; // 玉が取られたかどうかを返す
        }
        return false;
    }

    private GameObject GetPrefabByType(PieceType type)
    {
        foreach (var map in pieceMapList)
        {
            if (map.type == type) return map.prefab;
        }
        return null;
    }

    public List<PieceSpawnData> CreateLayout()
    {
        List<PieceSpawnData> layout = new List<PieceSpawnData>();
        // 例：歩 (Fu) を並べる
        for (int i = 0; i < 9; i++)
        {
            layout.Add(new PieceSpawnData { X = 2, Y = i,  player = PlayerType.Player1, type = PieceType.Pawn });
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
