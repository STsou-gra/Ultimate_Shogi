using System.Collections.Generic;

// 戦闘型（ルールセット）の定義：「時間がどう進むか」（Prototype の src/defs/rulesets/）。
//  ・Id / Name / Description
//  ・Realtime    … true なら手番がなく、時間（MatchState.Clock）で進む（GameSystem.Realtime.cs の RealtimeTick）
//  ・CutIns      … 全画面のカットインを出すか（演出だけ。ルールには影響しない）
//  ・AwaitEffects… 演出が終わるのを待ってから次の行動に進むか
//  ・EnergyPerSec / Durations / PieceCooldownSec / CpuIntervalSec … Realtime の戦闘型だけ（Kassen）
//
// どの処理も戦闘型の Id を直接見ずに、MatchPolicy（Match/MatchPolicy.cs）の関数を呼ぶ。
// 戦闘型を足すには、下に定義を 1 つ作って All に 1 行足す（編成画面の戦闘型の選択肢に並ぶ）。
// MonoBehaviour に依存しない（棋譜の再生・CPU・サーバーからも同じ値を使える）
public class BattleRuleset
{
    public string Id;
    public string Name;
    public string Description;
    public bool Realtime;
    public bool CutIns = true;
    public bool AwaitEffects = true;

    // ---- Realtime の戦闘型だけ ----
    public int EnergyPerSec;               // 1 秒ごとのエナジー（本将棋型の「手番の始めに +1」の代わり）
    public RulesetDurations Durations;     // 「N ターン」で書かれた規則の読み替え（秒）
    public float PieceCooldownSec;         // 動いた駒が次の行動をできるまでの秒数（0 で制限なし）
    public float[] CpuIntervalSec;         // CPU（Lv0, Lv1, Lv2）が行動する間隔（秒）
}

// 「N ターン」の規則を秒に読み替えた値（Prototype の kassen.js の durations）
public class RulesetDurations
{
    public float DropLockSec;          // 敵陣への打ち込み禁止（本将棋型の 10 ターン）
    public float BurnSecPerTurn;       // 炎上 1 ターン = この秒数（隕石の 10 ターン → 20 秒）
    public float WorldEndIntervalSec;  // 世界の終わりの崩壊の間隔（本将棋型は 1 ターンごと）
    public float AutoActSec;           // ファランクス・アンデッドの自動行動の間隔
    public float TimeStopSec;          // 停止世界：回数（MatchConfig.TimeStopMoves）か、この秒数の早いほうで解除
    public float UnlockCheckSec;       // 超常魔法の解放の判定の間隔（行動のあとにも判定する）
}

// 戦闘型の一覧（Prototype の src/defs/rulesets/index.js の RULESETS）
public static class BattleRulesets
{
    // 本将棋型：今までのルール（手番制）
    public static readonly BattleRuleset Honshogi = new BattleRuleset
    {
        Id = "honshogi",
        Name = "本将棋型",
        Description = "先手・後手が交代で 1 回ずつ行動する手番制。手番の始めにエナジー +1。",
        Realtime = false,
        CutIns = true,
        AwaitEffects = true,
    };

    // 合戦将棋型：手番のないリアルタイム制（Prototype の src/defs/rulesets/kassen.js と同じ数値）
    public static readonly BattleRuleset Kassen = new BattleRuleset
    {
        Id = "kassen",
        Name = "合戦将棋型",
        Description = "手番がない。両者ともいつでも何度でも自分の駒を動かせる。エナジーは 1 秒ごとに +1。",
        Realtime = true,
        CutIns = false,       // 全画面のカットインは出さない（スキルごとのエフェクトは出す）
        AwaitEffects = false, // 演出を待たずに重ねて再生する（演出でゲームの進行を止めない）
        EnergyPerSec = 1,
        Durations = new RulesetDurations
        {
            DropLockSec = 60f,
            BurnSecPerTurn = 2f,
            WorldEndIntervalSec = 10f,
            AutoActSec = 3f,
            TimeStopSec = 10f,
            UnlockCheckSec = 1f,
        },
        PieceCooldownSec = 0.3f,
        CpuIntervalSec = new[] { 2.0f, 1.5f, 1.2f },
    };

    public static readonly IReadOnlyList<BattleRuleset> All = new List<BattleRuleset> { Honshogi, Kassen };

    // Id から戦闘型を引く（なければ null）
    public static BattleRuleset ById(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (BattleRuleset r in All)
        {
            if (r.Id == id) return r;
        }
        return null;
    }
}

// 次の対局の設定（編成画面で選んで、対局画面が読む。DeckTransferManager と同じ受け渡し）
public static class MatchSetup
{
    // 選んだ戦闘型の Id（未設定なら本将棋型）
    public static string RulesetId { get; set; } = BattleRulesets.Honshogi.Id;

    public static BattleRuleset Ruleset => BattleRulesets.ById(RulesetId) ?? BattleRulesets.Honshogi;

    // 対局の乱数のシード（null なら対局ごとに決める。棋譜の再生では記録したシードを入れて、同じ対局にする）
    public static uint? Seed { get; set; }
}
