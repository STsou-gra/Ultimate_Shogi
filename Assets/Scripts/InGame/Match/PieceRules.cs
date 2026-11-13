// 駒の種類ごとの性質（Prototype の src/defs/pieces.js の explosive・sniper・capturable と HAND_ORDER）
// MonoBehaviour に依存しないので、ルール判定・CPU・サーバーからも同じ判定を使える
public static class PieceRules
{
    // 持ち駒の並び（表示・局面の記録・持ち駒の選択の順）。新しい駒は末尾に足す
    public static readonly PieceType[] HandOrder =
    {
        PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral,
        PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook,
        PieceType.Gunpowder, PieceType.Sniper, PieceType.Bird,
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

    // 成れる駒か（王将・金将・火薬・狙撃手・鳥は成らない）
    public static bool CanPromote(PieceType type)
    {
        switch (type)
        {
            case PieceType.King:
            case PieceType.GoldGeneral:
            case PieceType.Gunpowder:
            case PieceType.Sniper:
            case PieceType.Bird:
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

    // 駒 mover が target のいるマスに入れるか（駒だけを見る。マスの状態は IsBlockedFor）：空き・相手の駒・火薬（敵味方問わず）。
    // 鳥は火薬を爆発させないので、火薬もほかの駒と同じ扱い（相手の火薬は取れる・味方の火薬には入れない）。Prototype の board.js の canLandOn
    public static bool CanLandOn(GamePiece mover, GamePiece target)
    {
        return target == null || target.player != mover.player || Detonates(mover, target);
    }

    // ------------------------------------------------------------
    //  翼状態（Prototype の src/core/board.js の hasWing・hasMagicWing・isWingType・detonates・isBlockedFor・isHazardFor）
    //  マスの効果を見るときは、駒ごとにこれらを使う。新しい移動・突進・押し出しを足すときもこれらを通すと翼に対応できる
    // ------------------------------------------------------------

    // 駒の種類が常に翼状態か（鳥。持ち駒を打つマスの判定にも使う）
    public static bool IsWingType(PieceType type)
    {
        return type == PieceType.Bird;
    }

    // 翼状態か（鳥、または通常魔法「翼」をかけた駒）。マスの効果（破壊・炎上・落とし穴）を受けない
    public static bool HasWing(GamePiece p)
    {
        return p != null && (p.hasWing || IsWingType(p.type));
    }

    // 通常魔法「翼」による翼状態か（いるマスが破壊されても消失しない。鳥は消失する）
    public static bool HasMagicWing(GamePiece p)
    {
        return p != null && p.hasWing;
    }

    // 駒 mover が火薬 target のマスに入ると爆発するか（鳥は爆発させずに取る。魔法「翼」の駒は止まれば爆発する）
    public static bool Detonates(GamePiece mover, GamePiece target)
    {
        return target != null && IsExplosive(target.type) && !(mover != null && IsWingType(mover.type));
    }

    // 壁のマスか（Prototype の通常魔法「壁」の isWallCell）。翼状態の駒も含めて「入れない・通れない」（翼状態でも壁は通れない）
    public static bool IsWallCell(BoardCells cells, int x, int y)
    {
        return cells != null && cells.IsWall(x, y);
    }

    // 駒 p にとって (x, y) が入れない・通れないマスか（破壊マス・壁。翼の駒は破壊マスにも入れるが、壁は通れない）
    public static bool IsBlockedFor(BoardCells cells, GamePiece p, int x, int y)
    {
        if (cells == null) return false;
        return IsWallCell(cells, x, y) || (cells.IsBroken(x, y) && !HasWing(p));
    }

    // 駒の種類 type を (x, y) に打てないマスか（持ち駒は駒がないので種類で判定する。鳥は破壊マスにも打てる）
    public static bool IsBlockedForType(BoardCells cells, PieceType type, int x, int y)
    {
        if (cells == null) return false;
        return IsWallCell(cells, x, y) || (cells.IsBroken(x, y) && !IsWingType(type));
    }

    // 駒 p が (x, y) に入ると消失するマスか（炎上・落とし穴。翼の駒には効かない）
    public static bool IsHazardFor(BoardCells cells, GamePiece p, int x, int y)
    {
        return cells != null && cells.IsHazard(x, y) && !HasWing(p);
    }

    // 駒の種類 type を (x, y) に打つと消失するマスか（持ち駒を打つとき。鳥には効かない）
    public static bool IsHazardForType(BoardCells cells, PieceType type, int x, int y)
    {
        return cells != null && cells.IsHazard(x, y) && !IsWingType(type);
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
