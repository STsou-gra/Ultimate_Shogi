// 駒の種類ごとの性質（Prototype の src/defs/pieces.js の explosive・sniper・capturable と HAND_ORDER）
// MonoBehaviour に依存しないので、ルール判定・CPU・サーバーからも同じ判定を使える
public static class PieceRules
{
    // 持ち駒の並び（表示・局面の記録・持ち駒の選択の順）。新しい駒は末尾に足す
    public static readonly PieceType[] HandOrder =
    {
        PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral,
        PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook,
        PieceType.Gunpowder, PieceType.Sniper,
    };

    // 他の駒が重なると爆発する（火薬）
    public static bool IsExplosive(PieceType type)
    {
        return type == PieceType.Gunpowder;
    }

    // 「狙撃」で王将以外の相手の駒を取れる（狙撃手）
    public static bool IsSniper(PieceType type)
    {
        return type == PieceType.Sniper;
    }

    // 成れる駒か（王将・金将・火薬・狙撃手は成らない）
    public static bool CanPromote(PieceType type)
    {
        switch (type)
        {
            case PieceType.King:
            case PieceType.GoldGeneral:
            case PieceType.Gunpowder:
            case PieceType.Sniper:
                return false;
            default:
                return true;
        }
    }

    // 取ったときに持ち駒になるか（王将は持ち駒の枠がない。火薬は Prototype の capturable: false）
    public static bool GoesToHand(PieceType type)
    {
        return type != PieceType.King && type != PieceType.Gunpowder;
    }

    // 駒 mover が target のいるマスに入れるか：空き・相手の駒・火薬（敵味方問わず）。Prototype の board.js の canLandOn
    public static bool CanLandOn(GamePiece mover, GamePiece target)
    {
        return target == null || target.player != mover.player || IsExplosive(target.type);
    }

    // 爆発の範囲（中心から EXPLOSION_RADIUS マス以内）に入っているか
    public static bool IsInExplosion(int centerX, int centerY, int x, int y)
    {
        int r = MatchConfig.ExplosionRadius;
        return System.Math.Abs(x - centerX) <= r && System.Math.Abs(y - centerY) <= r;
    }

    // 爆発で消失する駒か（王将は爆発では倒せない：火薬・爆発スキル共通）
    public static bool IsDestroyedByExplosion(PieceType type)
    {
        return !(type == PieceType.King && MatchConfig.ExplosionSparesKing);
    }
}
