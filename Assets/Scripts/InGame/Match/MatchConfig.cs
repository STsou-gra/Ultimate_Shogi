// 対局ルールの数値（Prototype の src/config.js の CONFIG に当たる）
// MonoBehaviour に依存しないので、棋譜の再生・CPU・サーバーからも同じ値を使える
// （true / false の切り替えは static readonly にしている。const だと使う側で「到達できないコード」の警告が出るため）
public static class MatchConfig
{
    // 手番開始時に得るエナジー
    public const int PointsPerTurn = 1;

    // このターン数以下の間は、敵陣に持ち駒を打てない
    public const int EnemyZoneDropLockTurns = 10;

    // エナジーの上限（0 = 上限なし）。DEVELOPMENT_PLAN の A-3 が決まったら値を入れる
    public const int EnergyMax = 0;

    // true にすると駒錬成で手番を消費する（Prototype の CONFIG.FORGE_ENDS_TURN）
    public static readonly bool ForgeEndsTurn = false;

    // true なら魔法スキルの発動で手番を消費する（Prototype の CONFIG.MAGIC_ENDS_TURN）。
    // スキルごとの SkillDefinition.endsTurn が true のスキルだけが対象（2回行動など、今までのスキルは false のまま）
    public static readonly bool MagicEndsTurn = true;

    // true にすると爆発範囲内の火薬も誘爆する（Prototype の CONFIG.CHAIN_EXPLOSION）
    public static readonly bool ChainExplosion = false;

    // 爆発の範囲（1 = 中心を含む 3×3。Prototype の CONFIG.EXPLOSION_RADIUS）
    public const int ExplosionRadius = 1;

    // 爆発（火薬・爆発スキル）に巻き込まれた王将は残る。
    // Prototype の explode は王将も消すが、DEVELOPMENT_PLAN の「爆発では王将を倒せない」に揃えた
    public static readonly bool ExplosionSparesKing = true;

    // 超常魔法「世界の終わり」で 1 ターンに崩れるマスの数（Prototype の CONFIG.WORLD_END_CELLS。両者が発動すると重なる）
    public const int WorldEndCells = 3;

    // 超常魔法「停止世界」の時間停止中に行動できる回数（Prototype の CONFIG.TIME_STOP_MOVES。移動・打つ・魔法・必殺技・狙撃が各 1 回）
    public const int TimeStopMoves = 10;

    // 合戦将棋型の時間の単位：1 tick = 50ms（1 秒に 20 回。Prototype の CONFIG.TICK_MS）。対局の時間 MatchState.Clock はこの整数
    public const int TickMs = 50;

    // 合戦将棋型：1 フレームで進める時間の上限（ミリ秒。重いフレームのあとに一気に進めすぎない。Prototype の CONFIG.MAX_FRAME_MS）
    public const int MaxFrameMs = 250;

    // 先手（最初に指すプレイヤー）。Player1 = 盤の手前（X = 0〜2 が自陣）
    public const PlayerType FirstPlayer = PlayerType.Player1;
}
