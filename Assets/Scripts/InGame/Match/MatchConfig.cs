// 対局ルールの数値（Prototype の src/config.js の CONFIG に当たる）
// MonoBehaviour に依存しないので、棋譜の再生・CPU・サーバーからも同じ値を使える
public static class MatchConfig
{
    // 手番開始時に得るエナジー
    public const int PointsPerTurn = 1;

    // このターン数以下の間は、敵陣に持ち駒を打てない
    public const int EnemyZoneDropLockTurns = 10;

    // エナジーの上限（0 = 上限なし）。DEVELOPMENT_PLAN の A-3 が決まったら値を入れる
    public const int EnergyMax = 0;

    // 先手（最初に指すプレイヤー）。Player1 = 盤の手前（X = 0〜2 が自陣）
    public const PlayerType FirstPlayer = PlayerType.Player1;
}
