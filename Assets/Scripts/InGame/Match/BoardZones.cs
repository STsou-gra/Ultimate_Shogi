// 陣地（Prototype の src/core/board.js の zoneOf / isOwnZone / isEnemyZone）
//
// Unity 版の盤の座標は X が「段」（Player1 の前進方向が +X）、Y が「筋」。
// Prototype の y（段）は Unity の X に当たるので、ここの引数 rank には駒・マスの X を渡す。
//   X = 0〜2 … 先手（Player1）陣（盤の手前）
//   X = 3〜5 … 中立
//   X = 6〜8 … 後手（Player2）陣（盤の奥）
public enum BoardZone
{
    Player1Camp, // 先手陣
    Neutral,     // 中立
    Player2Camp, // 後手陣
}

public static class BoardZones
{
    public const int CampDepth = 3; // 自陣の段数

    // 段（Unity の X）がどの陣地か
    public static BoardZone ZoneOf(int rank)
    {
        if (rank < CampDepth) return BoardZone.Player1Camp;
        if (rank >= 9 - CampDepth) return BoardZone.Player2Camp;
        return BoardZone.Neutral;
    }

    // player の陣地
    public static BoardZone CampOf(PlayerType player)
    {
        return player == PlayerType.Player1 ? BoardZone.Player1Camp : BoardZone.Player2Camp;
    }

    // 段 rank が player の自陣か
    public static bool IsOwnZone(PlayerType player, int rank)
    {
        return ZoneOf(rank) == CampOf(player);
    }

    // 段 rank が player から見た敵陣か
    public static bool IsEnemyZone(PlayerType player, int rank)
    {
        return ZoneOf(rank) == CampOf(Opponent(player));
    }

    public static PlayerType Opponent(PlayerType player)
    {
        return player == PlayerType.Player1 ? PlayerType.Player2 : PlayerType.Player1;
    }
}
