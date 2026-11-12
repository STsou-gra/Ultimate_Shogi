using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

// CPU のプレイヤー（Prototype の src/controller/CpuController.js と src/cpu/workerClient.js）。GameSystem が CPU と対戦のときに足す。
//
//  CPU の手番で操作できるようになったら（GameSystem.CanCpuAct）：
//   1. 今の局面のスナップショット（CaptureSnapshot）と合法手（GameSystem.LegalActions。CanDispatch と同じ判定）を取る
//   2. CpuSearch で考える。画面が止まらないように、別スレッドで考える（WebGL などスレッドが使えないときは、
//      1 フレームに FrameBudgetMs ミリ秒ずつ考える）
//   3. MinThinkSeconds 秒たって（すぐ指すと何が起きたか分かりにくいので）考え終わったら、CanDispatch で確かめて DispatchFromCpu
//  2回行動・手番を使わない魔法・停止世界で手番が続くときは、操作できるようになったらまた考える。
//  考えている間に局面が変わったら（ありえないはずだが）、その答えは捨てて考え直す
public class CpuPlayer : MonoBehaviour
{
    public PlayerType Side { get; private set; }
    public int Level { get; private set; }

    [Tooltip("1 手に最低限かける秒数（すぐ指すと、何が起きたか分かりにくいので）")]
    public float MinThinkSeconds = 0.6f;

    [Tooltip("考える時間の上限（ミリ秒。Prototype と同じ 1500）")]
    public double TimeLimitMs = 1500;

    [Tooltip("スレッドが使えないとき、1 フレームに考える時間（ミリ秒）")]
    public double FrameBudgetMs = 6;

    // 考え中か（状況の表示用）
    public bool IsThinking => thinking;

    GameSystem game;
    bool thinking;
    float startedAt;
    int startedMove;
    int startedHistory;
    List<MatchAction> legal;

    // 別スレッド
    Task<MatchAction> task;
    // フレームごとに区切る
    CpuSearch sliced;
    System.Diagnostics.Stopwatch slicedClock;

    int settleFrames;     // 行動を実行したあと、局面が落ち着くまで待つフレーム数
    bool reportedStuck;

    static bool CanUseThreads => Application.platform != RuntimePlatform.WebGLPlayer;

    public void Init(GameSystem system, PlayerType side, int level)
    {
        game = system;
        Side = side;
        Level = CpuLevels.Clamp(level);
    }

    void Update()
    {
        if (game == null) return;
        if (settleFrames > 0)
        {
            settleFrames--;
            return;
        }
        if (!game.CanCpuAct)
        {
            if (thinking && !game.IsCpuTurn) Abandon(); // 終局・シーン移動など
            return;
        }
        if (!thinking)
        {
            Begin();
            return;
        }
        Poll();
    }

    void Begin()
    {
        legal = game.LegalActions(Side);
        if (!legal.Exists(a => a.type != MatchActionType.Forge))
        {
            if (!reportedStuck) Debug.LogWarning($"[CPU] {SideName}：指せる手がありません");
            reportedStuck = true;
            return;
        }
        reportedStuck = false;

        BoardSnapshot snapshot = game.CaptureSnapshot(false);
        CpuSearch.Options options = new CpuSearch.Options { Level = Level, TimeLimitMs = TimeLimitMs, Seed = SeedFromHash(game.StateHash()) };
        List<MatchAction> rootLegal = legal.ConvertAll(a => a.Clone());
        thinking = true;
        startedAt = Time.unscaledTime;
        startedMove = game.CurrentMoveNumber;
        startedHistory = game.ActionHistory.Count;
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【CPU】 {SideName}（{CpuLevels.Label(Level)}）が考えています……");

        if (CanUseThreads)
        {
            task = Task.Run(() => CpuSearch.Choose(snapshot, options, rootLegal));
        }
        else
        {
            // 考えている時間だけを測る（フレームの残りの時間は数えない）
            slicedClock = new System.Diagnostics.Stopwatch();
            System.Diagnostics.Stopwatch clock = slicedClock;
            options.Now = () => clock.Elapsed.TotalMilliseconds;
            sliced = new CpuSearch(snapshot, options, rootLegal);
        }
    }

    void Poll()
    {
        MatchAction result = null;
        bool done = false;
        if (task != null)
        {
            if (!task.IsCompleted) return;
            if (task.IsFaulted) Debug.LogException(task.Exception);
            else result = task.Result;
            done = true;
        }
        else if (sliced != null)
        {
            double until = slicedClock.Elapsed.TotalMilliseconds + FrameBudgetMs;
            slicedClock.Start();
            try
            {
                while (slicedClock.Elapsed.TotalMilliseconds < until && sliced.Step()) { }
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                sliced = null;
                done = true;
            }
            slicedClock.Stop();
            if (sliced != null && sliced.Done)
            {
                result = sliced.Result;
                done = true;
            }
        }
        else
        {
            done = true;
        }
        if (!done) return;
        if (Time.unscaledTime - startedAt < MinThinkSeconds) return; // 答えは出ているが、少し待つ

        task = null;
        sliced = null;
        thinking = false;

        if (game.CurrentMoveNumber != startedMove || game.ActionHistory.Count != startedHistory)
        {
            Debug.LogWarning("[CPU] 考えている間に局面が変わったので、考え直します");
            return;
        }
        Play(result);
    }

    void Play(MatchAction action)
    {
        string reason = null;
        if (action == null || !game.CanDispatch(action, out reason))
        {
            // 先読みの盤面（BoardSimulator）と GameSystem の判定が食い違った：合法手から選び直す（CanDispatch と同じ判定）
            if (action != null) Debug.LogWarning($"[CPU] 選んだ行動が実行できない（{reason}）：{action}。合法手から選び直します");
            action = legal.Find(a => a.type != MatchActionType.Forge && game.CanDispatch(a));
            if (action == null) return;
        }
        Debug.Log($"[CPU] {SideName}（{CpuLevels.Label(Level)}）：{action}");
        if (game.DispatchFromCpu(action)) settleFrames = 1;
    }

    void Abandon()
    {
        thinking = false;
        task = null; // 別スレッドの答えは捨てる（スナップショットの写しを読むだけなので、放っておいてよい）
        sliced = null;
    }

    string SideName => Side == PlayerType.Player1 ? "先手" : "後手";

    // GameSystem.StateHash（16 進 8 桁）を CPU の乱数のシードにする（Prototype の parseInt(hashState(state), 16)）
    static uint? SeedFromHash(string hash)
    {
        if (uint.TryParse(hash, System.Globalization.NumberStyles.HexNumber, null, out uint v)) return v;
        return null;
    }
}
