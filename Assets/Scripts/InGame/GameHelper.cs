using UnityEngine;

public static class GameHelper
{
    const int GAME_LOCATION_WIDTH = 9;
    const int GAME_LOCATION_HEIGHT = 9;
    const int BLOCK_SIZE = 1;
    const float OFFSET_SIZE = 0.0f;
    const int GAME_FILED_WIDTH  = GAME_LOCATION_WIDTH * BLOCK_SIZE; // 戦闘フィールドの幅
    const int GAME_FILED_HEIGHT = GAME_LOCATION_HEIGHT * BLOCK_SIZE; // 戦闘フィールドの奥行き

    public static int CalcPanelNum(int x, int y)
    {
        if(x < 0 || x >= GAME_LOCATION_WIDTH || y < 0 || y >= GAME_LOCATION_HEIGHT)
        {
            return -1; // 無効な位置
        }
        int Num = x + (y * GAME_LOCATION_WIDTH);
        if(Num >= GAME_LOCATION_WIDTH * GAME_LOCATION_HEIGHT)
        {
            return -1; // 無効な位置
        }
        return Num;
    }

    public static Vector2 CalcPanelLocation(int x, int y)
    {
        Vector2 Location;
        Location.x  = y * BLOCK_SIZE + OFFSET_SIZE - GAME_FILED_WIDTH * 0.5f + BLOCK_SIZE * 0.5f;
        Location.y  = x * BLOCK_SIZE + OFFSET_SIZE - GAME_FILED_HEIGHT * 0.5f + BLOCK_SIZE * 0.5f;
        return Location;
    }

    // 添字から座標を取得
    public static void CalcPanelPosition(out int X, out int Y, int Num)
    {
        X = Num % GAME_LOCATION_WIDTH;
        Y = Num / GAME_LOCATION_WIDTH;
    }

    public static bool IsPathBlocked(GamePiece[] boardData, int fromX, int fromY, int toX, int toY)
    {
        return IsPathBlocked(boardData, null, null, fromX, fromY, toX, toY);
    }

    public static bool IsPathBlocked(GamePiece[] boardData, BoardCells cells, int fromX, int fromY, int toX, int toY)
    {
        return IsPathBlocked(boardData, cells, null, fromX, fromY, toX, toY);
    }

    // 走り駒（香・角・飛・竜・馬）の通り道の判定（Prototype の moves.js の getMoves の slides）。
    // 移動先の手前までのマスに、駒・破壊マス・壁・炎上・落とし穴があれば通れない
    // （破壊マス・壁の手前で止まる／炎上・落とし穴のマスには入れるが、そこで止まるので先へは行けない）。
    // mover が翼状態の駒（PieceRules.HasWing）なら、破壊・炎上・落とし穴のマスと火薬の上を通り抜ける（通っただけでは爆発しない。壁は通れない）。
    // cells が null ならマスの状態は見ない。mover が null なら翼状態ではない駒として扱う
    public static bool IsPathBlocked(GamePiece[] boardData, BoardCells cells, GamePiece mover, int fromX, int fromY, int toX, int toY)
    {
        int dx = toX - fromX;
        int dy = toY - fromY;

        // 1マスの移動なら、途中のマスは存在しないのでブロックされない
        if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) return false;

        // 進む方向を特定（1, 0, -1 のいずれかになる）
        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        bool wing = PieceRules.HasWing(mover);

        int checkX = fromX + stepX;
        int checkY = fromY + stepY;
        int safetyCount = 0;
        // 移動先に到達する直前まで1マスずつ確認
        while ((checkX != toX || checkY != toY) && safetyCount < 10)
        {
            safetyCount++;
            if (checkX < 0 || checkX > 8 || checkY < 0 || checkY > 8) break;
            int index = CalcPanelNum(checkX, checkY);
            GamePiece q = index != -1 ? boardData[index] : null;
            if (q != null && !(wing && PieceRules.IsExplosive(q.type)))
            {
                return true; // 途中に駒があった！（翼の駒は火薬の上を通り抜ける）
            }
            if (cells != null && (PieceRules.IsBlockedFor(cells, mover, checkX, checkY) || PieceRules.IsHazardFor(cells, mover, checkX, checkY)))
            {
                return true; // 途中に破壊マス・壁・炎上・落とし穴があった（翼の駒は壁以外を通り抜ける）
            }

            checkX += stepX;
            checkY += stepY;
        }

        return false; // 途中に駒はなかった
    }
}
