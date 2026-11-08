// 駒ごとの動きの形（Prototype の src/defs/pieces.js の move / promotedMove）。
// 向きと距離だけを見る（盤の端・マスの状態・途中の駒は見ない。途中の駒は GameHelper.IsPathBlocked、破壊マスは GamePiece.CanMove が見る）。
// GameObject の駒（Piece/*.cs の CanMoveByPattern）と、盤面のスナップショット（BoardSimulator・SnapshotRules）の両方がこれを使う。
//
// 盤の座標は X が段（先手の前進方向が +X）、Y が筋。dx・dy は移動先 - 移動元。
// 今までの各駒のクラスの判定をそのまま移したもの（移動しない dx = dy = 0 でも true になる駒があるのも同じ。
// 自分のいるマスへの移動は BaseMoveRule が「自分の駒があるマス」として弾く）
public static class PieceMoves
{
    public static bool CanMoveByPattern(PieceType type, PlayerType owner, bool promoted, int dx, int dy)
    {
        switch (type)
        {
            case PieceType.Pawn:
                // 歩：前に 1 マス。と金は金将と同じ
                if (promoted) return Gold(owner, dx, dy);
                return dx == Forward(owner) && dy == 0;

            case PieceType.Lance:
            {
                // 香車：前にどこまでも（途中の駒は IsPathBlocked）。成香は金将と同じ
                if (promoted) return Gold(owner, dx, dy);
                int fx = owner == PlayerType.Player1 ? -dx : dx;
                int fy = owner == PlayerType.Player1 ? -dy : dy;
                return fx < 0 && fy == 0;
            }

            case PieceType.Knight:
            {
                // 桂馬：2 つ前の左右（飛び越える）。成桂は金将と同じ
                if (promoted) return Gold(owner, dx, dy);
                int fx = owner == PlayerType.Player1 ? -dx : dx;
                int fy = owner == PlayerType.Player1 ? -dy : dy;
                return fx == -2 && Abs(fy) == 1;
            }

            case PieceType.SilverGeneral:
            {
                // 銀将：前 3 方向と斜め後ろ 2 方向。成銀は金将と同じ
                if (promoted) return Gold(owner, dx, dy);
                int fx = owner == PlayerType.Player1 ? -dx : dx;
                int fy = owner == PlayerType.Player1 ? -dy : dy;
                return (fx == -1 && Abs(fy) <= 1) || (fx == 1 && Abs(fy) == 1);
            }

            case PieceType.GoldGeneral:
                // 金将：前後左右 4 方向と斜め前 2 方向
                return Gold(owner, dx, dy);

            case PieceType.Bishop:
                // 角行：斜めにどこまでも。竜馬は周囲 1 マスも
                if (promoted) return Abs(dx) == Abs(dy) || (Abs(dx) <= 1 && Abs(dy) <= 1);
                return Abs(dx) == Abs(dy);

            case PieceType.Rook:
                // 飛車：縦横にどこまでも。竜王は周囲 1 マスも
                if (promoted) return (dx == 0 || dy == 0) || (Abs(dx) <= 1 && Abs(dy) <= 1);
                return dx == 0 || dy == 0;

            case PieceType.King:
                // 王将：周囲 1 マス
                return Abs(dx) <= 1 && Abs(dy) <= 1;

            default:
                // 火薬・狙撃手：自分では動けない
                return false;
        }
    }

    // 前進の向き（先手 +1、後手 -1）
    public static int Forward(PlayerType owner)
    {
        return owner == PlayerType.Player1 ? 1 : -1;
    }

    // 金将の動き（と金・成香・成桂・成銀も）：前後左右 4 方向と斜め前 2 方向
    static bool Gold(PlayerType owner, int dx, int dy)
    {
        int rx = owner == PlayerType.Player1 ? -dx : dx;
        return Abs(rx) <= 1 && Abs(dy) <= 1 && !(rx == 1 && Abs(dy) == 1);
    }

    static int Abs(int v) => v < 0 ? -v : v;
}
