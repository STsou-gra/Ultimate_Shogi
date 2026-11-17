using System.Collections.Generic;

// モードの定義：「誰と戦い、何をすれば勝ちか」（Prototype の src/defs/modes/）。
//  ・Versus（基本対戦）   … 2人で対戦・CPU と対戦。両方の席がプレイヤー。王将をすべて倒したら勝ち（本将棋型は詰み・千日手も）
//  ・BossRaid（討伐任務） … プレイヤー 1 人（先手・盤の手前）対 ボス（後手の席。行動するのは Match/BossEngine.cs の AI）。
//                           ボスの HP を 0 にすれば討伐成功。王将が倒れた・王将の被弾がボスの上限に達したら討伐失敗
//
// 対局の設定 MatchSetup.Mode（MatchMode）から決まり、棋譜の config.mode・局面の mode・ハッシュには Id が入る。
// ほかの処理はモードの Id を直接見ずに、ここの値と MatchPolicy を見る（if (mode == "bossRaid") を散らさない）。
// MonoBehaviour に依存しない（盤面のスナップショット・棋譜・テストからも同じ値を使える）
public class GameModeDef
{
    public string Id;
    public string Name;

    // 後手の席がボスか（その席は行動できず、時間・手番のエナジーも増えない。Prototype の seats: ['player', 'boss'] と energySeats: [0]）
    public bool BossSeat;

    // 超常魔法を対局の開始時点から解放済みにする（プレイヤーの席だけ。1 対局 1 回の制限は残る。Prototype の transcendentUnlockedFromStart）
    public bool TranscendentUnlockedFromStart;

    // 停止世界に行動回数の上限（MatchConfig.TimeStopMoves）があるか。false なら合戦将棋型の TimeStopSec 秒だけ止まる（Prototype の timeStopMoveLimit）
    public bool TimeStopLimitsMoves = true;

    // 盤上の駒の移動・必殺技（ボスへの攻撃を含む）ごとに得るエナジー（打つ・魔法・錬成・狙撃は対象外。Prototype の afterAction・CONFIG.RAID_MOVE_ENERGY）
    public int MoveEnergy;

    // owner の席がプレイヤーの席か（討伐任務の後手の席はボス）
    public bool IsPlayerSeat(PlayerType owner)
    {
        return !(BossSeat && owner == PlayerType.Player2);
    }

    // 時間・手番でエナジーが増える席か
    public bool GainsEnergy(PlayerType owner)
    {
        return IsPlayerSeat(owner);
    }
}

public static class GameModes
{
    public static readonly GameModeDef Versus = new GameModeDef
    {
        Id = "versus",
        Name = "基本対戦",
    };

    public static readonly GameModeDef BossRaid = new GameModeDef
    {
        Id = "bossRaid",
        Name = "討伐任務",
        BossSeat = true,
        TranscendentUnlockedFromStart = true,
        TimeStopLimitsMoves = false,
        MoveEnergy = 2,
    };

    public static readonly IReadOnlyList<GameModeDef> All = new List<GameModeDef> { Versus, BossRaid };

    // Id からモードを引く（なければ基本対戦）
    public static GameModeDef ById(string id)
    {
        foreach (GameModeDef m in All)
        {
            if (m.Id == id) return m;
        }
        return Versus;
    }
}
