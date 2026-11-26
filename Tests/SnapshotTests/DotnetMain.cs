// dotnet run の入口（Unity の外で動かす。Test Runner では Assets/Tests/EditMode/SnapshotSuiteTests.cs が同じ組を 1 つずつ動かす）。
// テストの中身は Assets/Editor/SnapshotTests/（SnapshotSuite）。全部の組を順に動かし、1 件でも失敗すれば終了コード 1。
//  ・TEST_ONLY=boss：討伐任務の組だけ（開発中の確認用）
//  ・TEST_ONLY=名前,名前：その組だけ（名前は SnapshotSuite.SuiteNames。Patterns・RandomRules・Kassen など）
using System;
using System.Linq;

static class DotnetMain
{
    static int Main()
    {
        SnapshotSuite.LogWritten += Console.WriteLine;
        string only = Environment.GetEnvironmentVariable("TEST_ONLY");
        string[] names = string.IsNullOrEmpty(only) ? SnapshotSuite.SuiteNames()
            : only == "boss" ? SnapshotSuite.BossSuiteNames
            : only.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        int checks = 0, fails = 0;
        foreach (string name in names)
        {
            int c, f;
            try
            {
                SnapshotSuite.Run(name, out c, out f);
            }
            catch (SnapshotSuite.SuiteFailedException)
            {
                // 失敗の文は Log で出ている。件数だけ数え直す
                c = SnapshotSuite.LastCheckCount;
                f = SnapshotSuite.LastFailCount;
            }
            checks += c;
            fails += f;
        }
        Console.WriteLine($"checks={checks} fails={fails}");
        return fails == 0 ? 0 : 1;
    }
}
