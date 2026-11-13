// 対戦モードの設定（Prototype の src/scenes/ModeScene.js が Match に渡す設定）。タイトル → 対戦モードの画面（ModeSelectView）で決め、
// 編成画面（DeckSelectController）と対局（GameSystem・CpuPlayer）が読む。シーンをまたいで残すので static に持つ。
// Board シーンを直接開いたときは既定（2人で対戦）
public enum MatchMode
{
    TwoPlayers, // 2人で対戦（1 台の画面で交代に操作）
    VsCpu,      // CPU と対戦
}

public static class MatchSetup
{
    public static MatchMode Mode = MatchMode.TwoPlayers;

    // 戦闘型（編成画面で選ぶ。BattleRulesetPicker）：選んだ戦闘型の Id（未設定なら本将棋型）
    public static string RulesetId { get; set; } = BattleRulesets.Honshogi.Id;

    public static BattleRuleset Ruleset => BattleRulesets.ById(RulesetId) ?? BattleRulesets.Honshogi;

    // 対局の乱数のシード（null なら対局ごとに決める。棋譜の再生では記録したシードを入れて、同じ対局にする）
    public static uint? Seed { get; set; }

    // CPU と対戦：人が指す側（先手 = Player1・盤の手前）と CPU の強さ（CpuLevels）
    public static PlayerType HumanSide = PlayerType.Player1;
    public static int CpuLevel = 1;

    public static bool IsVsCpu => Mode == MatchMode.VsCpu;
    public static PlayerType CpuSide => HumanSide == PlayerType.Player1 ? PlayerType.Player2 : PlayerType.Player1;

    // side を CPU が指すか
    public static bool IsCpu(PlayerType side) => IsVsCpu && side == CpuSide;

    public static void SetTwoPlayers()
    {
        Mode = MatchMode.TwoPlayers;
    }

    public static void SetVsCpu(PlayerType humanSide, int level)
    {
        Mode = MatchMode.VsCpu;
        HumanSide = humanSide;
        CpuLevel = CpuLevels.Clamp(level);
    }

    // 画面に出す名前（「先手（CPU Lv1）」など）
    public static string SideLabel(PlayerType side)
    {
        string name = side == PlayerType.Player1 ? "先手" : "後手";
        if (!IsVsCpu) return name;
        return IsCpu(side) ? $"{name}（CPU Lv{CpuLevel}）" : $"{name}（あなた）";
    }
}

// CPU の強さ（Prototype の src/controller/CpuController.js の CPU_LEVELS）
public static class CpuLevels
{
    public const int Min = 0;
    public const int Max = 2;

    public static int Clamp(int level) => level < Min ? Min : level > Max ? Max : level;

    public static string Label(int level)
    {
        switch (level)
        {
            case 0: return "Lv0 ランダム";
            case 2: return "Lv2 つよい";
            default: return "Lv1 ふつう";
        }
    }

    public static string Note(int level)
    {
        switch (level)
        {
            case 0: return "合法手からでたらめに選ぶ（動作確認用）";
            case 2: return "相手の応手まで読んで選ぶ（少し考える時間が長い）";
            default: return "1 手先を全部試して、いちばん良い手を選ぶ";
        }
    }
}
