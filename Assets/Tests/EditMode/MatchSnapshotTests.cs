using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using NUnit.Framework;

// 局面の書き出し・読み込み（書き出し → 読み込み → 書き出しで同じ JSON になるか）と盤面のハッシュの EditMode テスト。
//
// ゲームのスクリプトは asmdef を持たない（Assembly-CSharp）ので、このテストの asmdef からは型を直接参照できない。
// 確認の中身は Assets/Editor/MatchSnapshotChecks.cs（Assembly-CSharp-Editor）にあり、ここではその Check〜 のメソッドを
// リフレクションで 1 つずつ呼ぶ（メソッドごとに 1 つのテストになる）。確認を足すときは MatchSnapshotChecks に Check〜 を足す
public class MatchSnapshotTests
{
    const string ChecksAssembly = "Assembly-CSharp-Editor";
    const string ChecksType = "MatchSnapshotChecks";

    static System.Type FindChecks()
    {
        foreach (Assembly asm in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            if (asm.GetName().Name != ChecksAssembly) continue;
            System.Type t = asm.GetType(ChecksType);
            if (t != null) return t;
        }
        return null;
    }

    static IEnumerable<MethodInfo> CheckMethods()
    {
        System.Type t = FindChecks();
        if (t == null) return Enumerable.Empty<MethodInfo>();
        return t.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.Name.StartsWith("Check") && m.GetParameters().Length == 0)
            .OrderBy(m => m.Name);
    }

    static IEnumerable<string> CheckNames() => CheckMethods().Select(m => m.Name);

    [Test]
    public void ChecksAreFound()
    {
        Assert.IsNotNull(FindChecks(), $"{ChecksAssembly} に {ChecksType} が見つかりません");
        Assert.That(CheckMethods().Count(), Is.GreaterThan(0), "Check〜 のメソッドがありません");
    }

    [TestCaseSource(nameof(CheckNames))]
    public void Check(string name)
    {
        MethodInfo m = CheckMethods().First(x => x.Name == name);
        try
        {
            m.Invoke(null, null);
        }
        catch (TargetInvocationException e) when (e.InnerException != null)
        {
            if (e.InnerException.GetType().Name == "CheckFailedException") Assert.Fail(e.InnerException.Message);
            ExceptionDispatchInfo.Capture(e.InnerException).Throw();
        }
    }
}
