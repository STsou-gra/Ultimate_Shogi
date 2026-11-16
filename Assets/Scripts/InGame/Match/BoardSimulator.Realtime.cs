// 盤面のシミュレーションの続き：合戦将棋型（手番のないリアルタイム制。BoardSnapshot.IsRealtime）。
// GameObject の側の GameSystem.Realtime.cs（Prototype の src/core/clock.js の tick・turn.js の afterRealtimeAction・
// policies.js の actorOf / isDropLocked / burnUntilFor / isCoolingDown）を、BoardSnapshot の上に移したもの。
// GameSystem.Realtime.cs の AfterRealtimeAction・RealtimeTick・MatchPolicy を変えたら、ここも同じように変えること。
//
//  ・行動するプレイヤーは MatchAction.owner（-1 なら current）。手番はなく、どちらの席の行動も当てはめられる。
//    相手の時間停止中は動けない。行動のあと（AfterRealtimeAction）：手数 +1 → 動いた駒に連打の間隔 → 停止世界の回数（錬成は数えない）
//    → 勝敗 → 超常魔法の解放（手番は交代しない。ファランクス・アンデッド・世界の終わりは Tick で進む）
//  ・Tick：時間を 1 tick 進める（毎秒のエナジー・鎮火・世界の終わり・自動行動・解放。停止世界の間は止まった時間だけ数えて解除する）
//  ・「N ターン」の読み替え：敵陣への打ち込み禁止は開始から DropLockSec 秒、炎上の期限は clockTime の tick、世界の終わりの発動時刻も clockTime
//  ・詰み・千日手・打ち歩詰めはない（MatchPolicy.JudgesCheckmate・ForbidsUchifuzume）。2回行動は使えない
public static partial class BoardSimulator
{
    // 秒 → tick（MatchPolicy.SecToTicks）
    static int Ticks(float sec) => MatchPolicy.SecToTicks(sec);

    static RulesetDurations Durations(BoardSnapshot s) => s.Ruleset.Durations ?? new RulesetDurations();

    // 行動するプレイヤー（MatchPolicy.ActorOf。本将棋型：手番のプレイヤー（owner があって手番と違えば null）／ 合戦将棋型：owner（なければ current））
    public static PlayerType? ActorOf(BoardSnapshot s, MatchAction a)
    {
        if (a == null) return null;
        if (!s.IsRealtime) return a.owner < 0 || a.owner == s.current ? s.Current : (PlayerType?)null;
        int o = a.owner >= 0 ? a.owner : s.current;
        return o == 0 || o == 1 ? (PlayerType)o : (PlayerType?)null;
    }

    // 「今」の時刻（MatchPolicy.Now。本将棋型：ターン数 ／ 合戦将棋型：clockTime）
    public static int Now(BoardSnapshot s) => s.IsRealtime ? s.clockTime : s.turnNumber;

    // 敵陣に持ち駒を打てない期間か（MatchPolicy.IsDropLocked）
    public static bool IsDropLocked(BoardSnapshot s)
    {
        if (s.IsRealtime) return s.clockTime < Ticks(Durations(s).DropLockSec);
        return s.turnNumber <= MatchConfig.EnemyZoneDropLockTurns;
    }

    // 「turns ターン炎上」の期限（MatchPolicy.BurnUntilFor）
    static int BurnUntilFor(BoardSnapshot s, int turns)
    {
        if (s.IsRealtime) return s.clockTime + turns * Ticks(Durations(s).BurnSecPerTurn);
        return s.turnNumber + turns - 1;
    }

    // 駒 p が連打の間隔のために、まだ行動できないか（MatchPolicy.IsCoolingDown）
    public static bool IsCoolingDown(BoardSnapshot s, SnapshotPiece p)
    {
        return s.IsRealtime && p != null && p.cooldownUntil > s.clockTick;
    }

    // 動いた駒が次の行動をできるようになるまでの tick（MatchPolicy.PieceCooldownTicks）
    static int PieceCooldownTicks(BoardSnapshot s) => s.IsRealtime ? Ticks(s.Ruleset.PieceCooldownSec) : 0;

    // 停止世界が時間で解除されるまでの tick（MatchPolicy.TimeStopTicks）
    static int TimeStopTicks(BoardSnapshot s) => s.IsRealtime ? Ticks(Durations(s).TimeStopSec) : 0;

    // 合戦将棋型：sec 秒ごとの時刻か（MatchPolicy.IsEvery）
    static bool IsEvery(BoardSnapshot s, float sec)
    {
        int n = Ticks(sec);
        return s.IsRealtime && n > 0 && s.clockTime % n == 0;
    }

    // 行動のあと（GameSystem.AfterRealtimeAction・Prototype の afterRealtimeAction）。piece は動いた駒（移動・必殺技・狙撃。なければ null）
    static void AfterRealtimeAction(BoardSnapshot s, SnapshotPiece piece, bool forge)
    {
        s.moveNumber++;
        int cd = PieceCooldownTicks(s);
        if (cd > 0 && piece != null && s.Contains(piece)) piece.cooldownUntil = s.clockTick + cd;

        if (s.timeStopped)
        {
            if (s.timeStopJustStarted)
            {
                // 停止世界を発動した行動（数えない）
                s.timeStopJustStarted = false;
                s.timeStopReleasable = true;
            }
            else if (!forge)
            {
                s.timeStopRemaining--;
                if (s.timeStopRemaining <= 0) ClearTimeStop(s);
            }
        }
        if (ResolveKingLoss(s)) return;
        if (s.skillsKnown) CheckTranscendentUnlock(s);
    }

    // 時間を 1 tick 進める（GameSystem.RealtimeTick・Prototype の clock.js の tick）。合戦将棋型で対局中でなければ何もしない
    //  1. clockTick++。停止世界の間は止まっている時間だけ数え、TimeStopSec 秒で解除する（対局の時間は進めない。2〜6 も行わない）
    //  2. clockTime++
    //  3. 毎秒：エナジー +EnergyPerSec（両者）
    //  4. 期限の来た炎上の鎮火
    //  5. 世界の終わり（WorldEndIntervalSec 秒ごと）・ファランクスとアンデッドの自動行動（AutoActSec 秒ごと。先手 → 後手）
    //  6. 超常魔法の解放の判定（UnlockCheckSec 秒ごと）
    public static void Tick(BoardSnapshot s)
    {
        if (!s.IsRealtime || s.gameOver) return;
        s.clockTick++;
        if (s.timeStopped)
        {
            s.timeStopElapsed++;
            if (s.timeStopElapsed >= TimeStopTicks(s))
            {
                ClearTimeStop(s);
                ResolveKingLoss(s);
            }
            return;
        }

        s.clockTime++;
        RulesetDurations d = Durations(s);
        if (IsEvery(s, 1f))
        {
            AddEnergy(s, PlayerType.Player1, s.Ruleset.EnergyPerSec);
            AddEnergy(s, PlayerType.Player2, s.Ruleset.EnergyPerSec);
        }
        ExtinguishExpired(s, s.clockTime);

        if (!s.skillsKnown) return; // 自動行動・世界の終わり・解放は、必殺技・魔法を知っているスナップショットだけ

        if (IsEvery(s, d.WorldEndIntervalSec))
        {
            int n = WorldEndCellsThisRound(s);
            if (n > 0) WorldEndCollapse(s, n);
            if (ResolveKingLoss(s)) return;
        }
        if (IsEvery(s, d.AutoActSec))
        {
            foreach (PlayerType side in new[] { PlayerType.Player1, PlayerType.Player2 })
            {
                RunPhalanx(s, side);
                if (ResolveKingLoss(s)) return;
                RunUndead(s, side);
                if (ResolveKingLoss(s)) return;
            }
        }
        if (IsEvery(s, d.UnlockCheckSec)) CheckTranscendentUnlock(s);
    }

    // n tick 進める（終局したら止める）
    public static void Advance(BoardSnapshot s, int n)
    {
        for (int i = 0; i < n && !s.gameOver; i++) Tick(s);
    }

    // 時間停止の状態を消す（予定はシミュレーションでは近似ですでに起こしてある）
    static void ClearTimeStop(BoardSnapshot s)
    {
        s.timeStopped = false;
        s.timeStopOwner = -1;
        s.timeStopReleasable = false;
        s.timeStopJustStarted = false;
        s.timeStopRemaining = 0;
        s.timeStopElapsed = 0;
        s.heldSquares.Clear();
        foreach (SnapshotPiece p in s.pieces)
        {
            p.frozen = false;
            p.pendingPromote = false;
        }
    }
}
