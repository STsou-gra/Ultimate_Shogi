using System.Collections.Generic;
using UnityEngine;

// SnapshotSuite（Assets/Editor/SnapshotTests。Unity の Test Runner の EditMode と dotnet run の両方で動くテスト）が、
// 今のルール（Rules/GameRules.cs の CheckRules・IGameRule）の判定に渡す盤と駒を作る入口の Unity の版。
// 本物の GameBoard・GamePiece（種類ごとの Piece〜）を、見た目のない GameObject に付けて作る（プレハブは使わない。
// EditMode なので Awake・Start・Update は呼ばれない）。シーンには出さず保存もしない（HideAndDontSave）。
// 作ったものは ReleaseAll でまとめて消す（SnapshotSuite.Run の終わりと、局面をたくさん作るテストの途中で呼ばれる）。
// dotnet run の版は Tests/SnapshotTests/UnityStubs.cs（ただのクラスの GamePiece・GameBoard）
public static class TestBoards
{
    static readonly List<GameObject> created = new List<GameObject>();

    public static GameBoard NewBoard()
    {
        return NewObject("TestBoard").AddComponent<GameBoard>();
    }

    // 駒を作って盤の (x, y) に置く（巨人は中央 (x, y) の 3×3 マス。GameBoard.AttachPiece）
    public static GamePiece NewPiece(GameBoard board, PieceType type, PlayerType owner, int x, int y)
    {
        GamePiece piece = AddPiece(NewObject($"Test_{owner}_{type}"), type);
        piece.type = type;
        piece.player = owner;
        piece.Cells = board.Cells;
        board.AttachPiece(piece, x, y);
        return piece;
    }

    public static void ReleaseAll()
    {
        foreach (GameObject obj in created)
        {
            if (obj != null) Object.DestroyImmediate(obj);
        }
        created.Clear();
    }

    static GameObject NewObject(string name)
    {
        GameObject obj = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        created.Add(obj);
        return obj;
    }

    // 種類ごとの駒のクラス（GameBoard のプレハブ・PieceKooni.Create・PieceSoldier.Create と同じクラス。動きの形は PieceMoves・GiantRules）
    static GamePiece AddPiece(GameObject obj, PieceType type)
    {
        switch (type)
        {
            case PieceType.King: return obj.AddComponent<PieceKing>();
            case PieceType.Rook: return obj.AddComponent<PieceRook>();
            case PieceType.Bishop: return obj.AddComponent<PieceBishop>();
            case PieceType.GoldGeneral: return obj.AddComponent<PieceGoldGeneral>();
            case PieceType.SilverGeneral: return obj.AddComponent<PieceSilverGeneral>();
            case PieceType.Knight: return obj.AddComponent<PieceKnight>();
            case PieceType.Lance: return obj.AddComponent<PieceLance>();
            case PieceType.Pawn: return obj.AddComponent<PiecePawn>();
            case PieceType.Gunpowder: return obj.AddComponent<PieceGunpowder>();
            case PieceType.Sniper: return obj.AddComponent<PieceSniper>();
            case PieceType.Bird: return obj.AddComponent<PieceBird>();
            case PieceType.Giant: return obj.AddComponent<PieceGiant>();
            case PieceType.Kooni: return obj.AddComponent<PieceKooni>();
            default: return obj.AddComponent<PieceSoldier>(); // 軍歩兵・軍銃兵・防壁
        }
    }
}
