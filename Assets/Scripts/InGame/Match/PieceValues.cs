// 駒の価値（エナジー配分の基準。Prototype の src/defs/pieces.js の value）
// 駒が取られると、取られた側が価値の全額、取った側が価値の半分（切り捨て）のエナジーを得る。
// 各駒プレハブの piecePoint は使わない（値をここ1か所で管理し、CPU・棋譜・サーバーからも引けるようにする）
public static class PieceValues
{
    public static int Of(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return 1;
            case PieceType.Lance: return 2;
            case PieceType.Knight: return 2;
            case PieceType.SilverGeneral: return 3;
            case PieceType.GoldGeneral: return 3;
            case PieceType.Bishop: return 5;
            case PieceType.Rook: return 5;
            case PieceType.King: return 10;
            case PieceType.Gunpowder: return 0; // 取られても誰もエナジーを得ない（爆発するので通常は取られない）
            case PieceType.Sniper: return 3;
            case PieceType.Bird: return 1;      // Prototype の TORI の value
            default: return 0;
        }
    }

    // 駒を取られた側が得るエナジー
    public static int CapturedSideGain(PieceType type)
    {
        return Of(type);
    }

    // 駒を取った側が得るエナジー
    public static int CapturerGain(PieceType type)
    {
        return Of(type) / 2; // 0 以上なので切り捨てになる
    }
}
