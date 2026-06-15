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
        int dx = toX - fromX;
        int dy = toY - fromY;

        // 1マスの移動なら、途中のマスは存在しないのでブロックされない
        if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) return false;

        // 進む方向を特定（1, 0, -1 のいずれかになる）
        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);

        int checkX = fromX + stepX;
        int checkY = fromY + stepY;
        int safetyCount = 0;
        // 移動先に到達する直前まで1マスずつ確認
        while ((checkX != toX || checkY != toY) && safetyCount < 10)
        {
            safetyCount++;
            if (checkX < 0 || checkX > 8 || checkY < 0 || checkY > 8) break;
            int index = CalcPanelNum(checkX, checkY);
            if (index != -1 && boardData[index] != null)
            {
                return true; // 途中に駒があった！
            }

            checkX += stepX;
            checkY += stepY;
        }

        return false; // 途中に駒はなかった
    }
}
