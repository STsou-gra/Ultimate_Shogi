// 対局の時間（合戦将棋型だけ。Prototype の src/core/clockState.js の state.clock）。
//  Tick … RealtimeTick を呼んだ回数（棋譜の行動の t。停止世界の間も進む）
//  Time … 対局の時間（Tick と同じ単位。停止世界の間は止まる。エナジー・炎上・世界の終わり・自動行動はこちらで進む）
public class MatchClock
{
    public int Tick;
    public int Time;
}

// ポリシー：戦闘型（BattleRuleset）の違いを閉じ込める入口（Prototype の src/core/policies.js）。
// ほかの処理は戦闘型の Id を直接見ずに、ここの関数を呼ぶ（if (ruleset == "kassen") を散らさない）。
//  ・演出の方針      … ShowsCutIns・AwaitsEffects（演出だけ。ルールには影響しない）
//  ・手番・判定      … HasTurns・JudgesCheckmate など（合戦将棋型は手番がなく、詰み・千日手・打ち歩詰め・王手の表示はない）
//  ・行動する人      … ActorOf（本将棋型は手番の人。合戦将棋型は MatchAction.owner）
//  ・時間の単位      … SecToTicks（1 tick = MatchConfig.TickMs）
//  ・「N ターン」の読み替え … IsDropLocked・BurnUntilFor・Now・IsEvery など
// MonoBehaviour に依存しない
public class MatchPolicy
{
    private readonly MatchState match;

    public BattleRuleset Ruleset { get; }

    public MatchPolicy(BattleRuleset ruleset, MatchState match)
    {
        Ruleset = ruleset ?? BattleRulesets.Honshogi;
        this.match = match;
    }

    // 手番がなく、時間で進む戦闘型か
    public bool IsRealtime => Ruleset.Realtime;

    MatchClock Clock => match != null ? match.Clock : null;
    RulesetDurations Dur => Ruleset.Durations ?? new RulesetDurations();

    // ------------------------------------------------------------
    //  演出の方針（PresentationPolicy）
    // ------------------------------------------------------------

    // 全画面のカットイン（必殺技・魔法・時よ止まれ・超常魔法の解放・自動行動の小さなカットイン）を出すか
    public bool ShowsCutIns => Ruleset.CutIns;
    // 演出が終わってから次の行動に進むか（false なら演出は待たずに重ねて再生する）
    public bool AwaitsEffects => Ruleset.AwaitEffects;

    // ------------------------------------------------------------
    //  手番と判定
    // ------------------------------------------------------------

    // 手番があるか（2回行動・手番交代の前の自動行動・カメラの回転・手番開始時のエナジー）
    public bool HasTurns => !IsRealtime;
    // 詰みを判定するか（合戦将棋型は王将を取ったら勝ち。Prototype と同じく詰みはない）
    public bool JudgesCheckmate => !IsRealtime;
    // 千日手を判定するか（合戦将棋型は手番がないので局面の繰り返しを数えない）
    public bool JudgesRepetition => !IsRealtime;
    // 打ち歩詰めを禁止するか（詰みがないので禁止しない）
    public bool ForbidsUchifuzume => !IsRealtime;
    // 「王手」を表示するか（合戦将棋型は手番がないので出さない。Prototype と同じ）
    public bool AnnouncesCheck => !IsRealtime;

    // 行動するプレイヤー。本将棋型は手番のプレイヤー（owner があって手番と違えば null）、
    // 合戦将棋型は action.owner（省略したら current = 操作している席）
    public PlayerType? ActorOf(MatchAction action, PlayerType current)
    {
        if (action == null) return null;
        if (!IsRealtime) return action.owner < 0 || action.owner == (int)current ? current : (PlayerType?)null;
        int o = action.owner >= 0 ? action.owner : (int)current;
        return o == 0 || o == 1 ? (PlayerType)o : (PlayerType?)null;
    }

    // ------------------------------------------------------------
    //  時間の単位
    // ------------------------------------------------------------

    // 秒 → tick
    public static int SecToTicks(float sec)
    {
        return (int)System.Math.Round(sec * 1000.0 / MatchConfig.TickMs);
    }

    // tick → 秒
    public static float TicksToSec(int ticks)
    {
        return ticks * MatchConfig.TickMs / 1000f;
    }

    // ------------------------------------------------------------
    //  「N ターン」の規則の読み替え（DurationPolicy）
    // ------------------------------------------------------------

    // 「今」の時刻（世界の終わりの発動時刻・炎上の期限など。本将棋型：ターン数 ／ 合戦将棋型：Clock.Time）
    public int Now => IsRealtime ? (Clock != null ? Clock.Time : 0) : match.Turn.TurnNumber;

    // 敵陣に持ち駒を打てない期間か（本将棋型：MatchConfig.EnemyZoneDropLockTurns ターン以下 ／ 合戦将棋型：開始から DropLockSec 秒）
    public bool IsDropLocked
    {
        get
        {
            if (IsRealtime) return Now < SecToTicks(Dur.DropLockSec);
            return match.Turn.IsEnemyZoneDropLocked;
        }
    }

    // 合戦将棋型：ちょうど今、敵陣への打ち込みが解禁されたか（RealtimeTick が見る）
    public bool IsDropUnlockMoment => IsRealtime && Now == SecToTicks(Dur.DropLockSec);

    // 敵陣への打ち込み禁止の期間（ルール説明用。本将棋型：「第10ターンまで」／ 合戦将棋型：「開始から60秒」）
    public string DropLockLabel => IsRealtime ? $"開始から {Dur.DropLockSec:0} 秒" : $"第 {MatchConfig.EnemyZoneDropLockTurns} ターンまで";

    // 「turns ターン炎上」の期限（BoardCell.BurnUntil）。期限が Now 以下になったら鎮火する。
    // 本将棋型：発動したターンを 1 ターン目とした最後のターン（そのターンの終わりに鎮火）／ 合戦将棋型：期限の Clock.Time
    public int BurnUntilFor(int turns)
    {
        if (IsRealtime) return Now + turns * SecToTicks(Dur.BurnSecPerTurn);
        return match.Turn.TurnNumber + turns - 1;
    }

    // 炎上の残り（表示用。本将棋型：残りターン ／ 合戦将棋型：残り秒（切り上げ））
    public int BurnRemaining(BoardCell cell)
    {
        if (cell == null || !cell.IsBurning || !cell.BurnUntil.HasValue) return 0;
        if (IsRealtime) return System.Math.Max(0, (int)System.Math.Ceiling(TicksToSec(cell.BurnUntil.Value - Now) - 1e-4));
        return cell.BurnUntil.Value - match.Turn.TurnNumber + 1;
    }

    // 合戦将棋型：sec 秒ごとの時刻か（Clock.Time が sec 秒の倍数）
    public bool IsEvery(float sec)
    {
        int n = SecToTicks(sec);
        return IsRealtime && Clock != null && n > 0 && Clock.Time % n == 0;
    }

    public bool IsEveryWorldEnd => IsEvery(Dur.WorldEndIntervalSec);
    public bool IsEveryAutoAct => IsEvery(Dur.AutoActSec);
    public bool IsEveryUnlockCheck => IsEvery(Dur.UnlockCheckSec);
    public bool IsEverySecond => IsEvery(1f);

    // 1 秒ごとのエナジー（合戦将棋型だけ）
    public int EnergyPerSec => IsRealtime ? Ruleset.EnergyPerSec : 0;

    // ------------------------------------------------------------
    //  連打の間隔・停止世界・CPU
    // ------------------------------------------------------------

    // 動いた駒が次の行動をできるようになるまでの tick（合戦将棋型。0 なら制限なし）
    public int PieceCooldownTicks => IsRealtime ? SecToTicks(Ruleset.PieceCooldownSec) : 0;

    // 駒 p が連打の間隔のために、まだ行動できないか
    public bool IsCoolingDown(GamePiece p)
    {
        return IsRealtime && p != null && Clock != null && p.cooldownUntilTick > Clock.Tick;
    }

    // 停止世界が時間で解除されるまでの tick（合戦将棋型。本将棋型は 0 = 時間では解除しない）
    public int TimeStopTicks => IsRealtime ? SecToTicks(Dur.TimeStopSec) : 0;

    // 停止世界の残り秒（切り上げ。合戦将棋型の時間停止中でなければ -1）
    public int TimeStopSecLeft(TimeStopState ts)
    {
        if (ts == null || !IsRealtime) return -1;
        return System.Math.Max(0, (int)System.Math.Ceiling(TicksToSec(TimeStopTicks - ts.Elapsed) - 1e-4));
    }

    // CPU（Lv level）が行動する間隔の tick（合戦将棋型。設定がなければ 1 秒）
    public int CpuIntervalTicks(int level)
    {
        float[] list = Ruleset.CpuIntervalSec;
        if (list == null || list.Length == 0) return SecToTicks(1f);
        int i = System.Math.Max(0, System.Math.Min(level, list.Length - 1));
        return System.Math.Max(1, SecToTicks(list[i]));
    }

    // ------------------------------------------------------------
    //  表示
    // ------------------------------------------------------------

    // 画面上部の「第Nターン（第M手）」／「経過 m:ss」
    public string ProgressLabel
    {
        get
        {
            if (!IsRealtime) return match.Turn.Label;
            int sec = Clock != null ? (int)TicksToSec(Clock.Time) : 0;
            return $"{Ruleset.Name}　経過 {sec / 60}:{sec % 60:00}";
        }
    }
}
