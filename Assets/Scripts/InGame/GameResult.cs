// 対局画面から結果画面へ試合結果を受け渡す
public static class GameResult
{
    public static PlayerType? Winner { get; set; }
    public static bool IsDraw { get; set; } // 千日手などによる引き分け
}
