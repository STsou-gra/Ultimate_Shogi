// 駒の日本語名（Prototype の src/defs/pieces.js の name・label）。
// MonoBehaviour に依存しないので、ルール説明・棋譜・CPU からも同じ名前を使える
public static class PieceNames
{
    // 短い名前（持ち駒の一覧・必殺技の説明など。「歩」「香」…）
    public static string Short(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return "歩";
            case PieceType.Lance: return "香";
            case PieceType.Knight: return "桂";
            case PieceType.SilverGeneral: return "銀";
            case PieceType.GoldGeneral: return "金";
            case PieceType.Bishop: return "角";
            case PieceType.Rook: return "飛";
            case PieceType.King: return "玉";
            case PieceType.Gunpowder: return "火薬";
            case PieceType.Sniper: return "狙撃手";
            case PieceType.Bird: return "鳥";
            default: return type.ToString();
        }
    }

    // 正式な名前（「歩兵」ではなく、DEVELOPMENT_PLAN の表記に合わせて「歩」「香車」…）
    public static string Full(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return "歩";
            case PieceType.Lance: return "香車";
            case PieceType.Knight: return "桂馬";
            case PieceType.SilverGeneral: return "銀将";
            case PieceType.GoldGeneral: return "金将";
            case PieceType.Bishop: return "角行";
            case PieceType.Rook: return "飛車";
            case PieceType.King: return "王将";
            case PieceType.Gunpowder: return "火薬";
            case PieceType.Sniper: return "狙撃手";
            case PieceType.Bird: return "鳥";
            default: return type.ToString();
        }
    }

    // 駒の種類（PieceType の並び順）
    public static readonly PieceType[] All =
    {
        PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral,
        PieceType.Bishop, PieceType.Rook, PieceType.King, PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird,
    };
}
