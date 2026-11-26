using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

// 盤面のシミュレーション（BoardSnapshot・BoardSimulator・SnapshotRules・PieceMoves）・CPU・合戦将棋型・討伐任務の単体テストの EditMode 版。
// Unity の外で dotnet run（Tests/SnapshotTests）で動かしていたテストと同じもの（Prototype の棋譜の再生・今のルールとの照合・ランダムに指し続ける局など）。
//
// テストの中身は Assets/Editor/SnapshotTests/（SnapshotSuite。Assembly-CSharp-Editor）にある。ゲームのスクリプトは asmdef を持たない
// （Assembly-CSharp）ので、このテストの asmdef からは型を直接参照できず、MatchSnapshotTests と同じくリフレクションで呼ぶ。
// SnapshotSuite.SuiteNames の組（Patterns・RandomRules・Kassen・Boss など）が 1 つずつ 1 件のテストになる。
// 失敗すると、失敗した確認の一覧と途中経過が出る。成功したときの途中経過（何局指したか・何手一致したか）はテストの出力に出る。
// 組を足すときは SnapshotSuite.Suites に足す（ここは変えなくてよい）
public class SnapshotSuiteTests
{
    const string SuiteAssembly = "Assembly-CSharp-Editor";
    const string SuiteType = "SnapshotSuite";

    static System.Type FindSuite()
    {
        foreach (Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != SuiteAssembly) continue;
            System.Type t = asm.GetType(SuiteType);
            if (t != null) return t;
        }
        return null;
    }

    static IEnumerable<string> SuiteNames()
    {
        System.Type t = FindSuite();
        MethodInfo names = t?.GetMethod("SuiteNames", BindingFlags.Public | BindingFlags.Static);
        if (names == null) return Enumerable.Empty<string>();
        return (string[])names.Invoke(null, null);
    }

    [Test]
    public void SuitesAreFound()
    {
        Assert.IsNotNull(FindSuite(), $"{SuiteAssembly} に {SuiteType} が見つかりません");
        Assert.That(SuiteNames().Count(), Is.GreaterThan(0), "テストの組がありません");
    }

    [TestCaseSource(nameof(SuiteNames))]
    public void Suite(string name)
    {
        MethodInfo run = FindSuite().GetMethod("Run", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
        Assert.IsNotNull(run, $"{SuiteType}.Run(string) が見つかりません");
        try
        {
            string log = (string)run.Invoke(null, new object[] { name });
            TestContext.WriteLine(log);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            if (e.InnerException.GetType().Name == "SuiteFailedException") Assert.Fail(e.InnerException.Message);
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }
}
