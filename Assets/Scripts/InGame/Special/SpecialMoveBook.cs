using System.Collections.Generic;
using UnityEngine;

// どの駒がどの必殺技を使うか（Prototype の src/defs/specials/index.js の SPECIAL_MOVES）。
// キーは成る前の駒の種類（成った駒も同じ技を使える）。銀将・王将などは登録なし＝必殺技なし。
// GameSystem に設定しなければ Resources/SpecialMoveBook を読む（それもなければ既定の 6 技を実行時に作る）
[CreateAssetMenu(menuName = "UltimateShogi/Special Move Book")]
public class SpecialMoveBook : ScriptableObject
{
    public const string ResourcePath = "SpecialMoveBook";

    [System.Serializable]
    public class Entry
    {
        public PieceType pieceType;
        public SpecialMove move;
    }

    public List<Entry> entries = new List<Entry>();

    // 駒の種類の必殺技（なければ null）
    public SpecialMove For(PieceType type)
    {
        foreach (Entry e in entries)
        {
            if (e != null && e.pieceType == type && e.move != null) return e.move;
        }
        return null;
    }

    public static SpecialMoveBook LoadDefault()
    {
        SpecialMoveBook book = Resources.Load<SpecialMoveBook>(ResourcePath);
        if (book != null) return book;

        Debug.LogWarning("必殺技の一覧（Resources/SpecialMoveBook）が見つからないので、既定の必殺技を使います");
        book = CreateInstance<SpecialMoveBook>();
        book.Add(PieceType.Pawn, CreateInstance<PawnChargeSpecial>());
        book.Add(PieceType.Lance, CreateInstance<LancePierceSpecial>());
        book.Add(PieceType.Knight, CreateInstance<KnightLeapSpecial>());
        book.Add(PieceType.GoldGeneral, CreateInstance<GoldShockSpecial>());
        book.Add(PieceType.Bishop, CreateInstance<BishopBeamSpecial>());
        book.Add(PieceType.Rook, CreateInstance<RookBeamSpecial>());
        return book;
    }

    void Add(PieceType type, SpecialMove move)
    {
        entries.Add(new Entry { pieceType = type, move = move });
    }
}
