using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// 棋譜（記録・保存・再生）と局面の書き出し・読み込み（Prototype の src/game/kifu.js・src/controller/ReplayController.js・
// Game.js の exportKifu / replayKifu / exportState / importState）。GameSystem の続き（partial）。
//
//  ・記録：対局を始めると KifuSession.Current に棋譜（Match/Kifu.cs の KifuFile）を作り、実行した行動（RecordAction）を順に足す。
//    各行動のハッシュは「その行動の処理がすべて終わった時点」＝次に操作を受け付けられる状態（IsKifuSettled：
//    手番中で演出中でない、または対局が終わった）になったときの盤面のハッシュ（StateHash。MatchHash）。
//    演出の長さは関係しない（盤面が変わり終わるまで、次の行動は受け付けない）
//  ・再生：棋譜の編成・シードで Board シーンを読み込み直し（KifuSession の依頼）、行動を順に Dispatch する。
//    再生中はプレイヤーの操作を受け付けない。各行動のあと、盤面のハッシュを棋譜と照らし合わせ、ずれたらその手で止める。
//    最後まで再生した・ずれた・再現できない行動があった・止めたら、その局面から続けて指せる（続きも同じ棋譜に記録される）。
//    速さは Time.timeScale（等速・4 倍速）。演出の待ち時間はすべて Time.timeScale に従うので、結果は変わらない
//  ・局面の書き出し・読み込み（ファイル）：GameSystem.State.cs の ExportState / ImportState を使う（その場で盤を作り直して続きから指す）。
//    読み込んだ局面からは棋譜を記録しない（Prototype の importState と同じ）。演出中・再生中は書き出せない
//  ・オンライン対戦では記録しない（相手の端末の行動・魔法がまだ同期されていないため）
public partial class GameSystem
{
    // 再生の待ち時間（秒。Time.timeScale で縮む。Prototype の ReplayController の firstWait / gap）
    public const float ReplayFirstWait = 1.2f;
    public const float ReplayGap = 0.3f;
    public const float ReplayFastSpeed = 4f;

    // 再生中の状態（Prototype の ReplayController）
    public class ReplayProgress
    {
        public KifuFile Kifu;     // 再生している棋譜（読み込んだもの。記録は KifuSession.Current に別に作る）
        public int Played;        // 再生した行動の数
        public int Total;
        public float Speed;
        public float Wait;        // 次の行動までの残り秒（演出がない間だけ減る）
        public bool DebugEdited;  // デバッグキーで盤面を変えた棋譜
    }

    // 再生の結果（Prototype の onEnd の { finished, played, total, error }）
    public struct ReplayResult
    {
        public int Played;
        public int Total;
        public string Error;    // ずれた・再現できなかった・止めた理由（最後まで一致したら null）
        public bool Stopped;    // 「再生を止める」で止めた

        public bool Ok => Error == null;

        public string Message
        {
            get
            {
                if (Stopped) return $"棋譜の再生を {Played} 手目で止めた（全 {Total} 手）。この局面から続けて指せる";
                if (Error != null) return $"棋譜の再生を止めた：{Error}（{Played} / {Total} 手）。この局面から続けて指せる";
                return $"棋譜の再生が終わった：全 {Total} 手の盤面が棋譜と一致した。この局面から続けて指せる";
            }
        }
    }

    private ReplayProgress replay;
    private bool replayDispatching;   // 再生が Dispatch している間（再生中でも、この行動だけは通す）
    private SkillDeck[] requestedDecks;  // 棋譜の再生の編成（編成画面で選んだ編成の代わりに使う）

    public bool IsReplaying => replay != null;
    public ReplayProgress Replay => replay;

    // 再生の進み具合が変わった（HUD の表示）・再生が終わった
    public event System.Action<ReplayProgress> ReplayUpdated;
    public event System.Action<ReplayResult> ReplayEnded;

    // この対局の棋譜（記録していなければ null）
    public KifuFile Kifu => KifuSession.Current;

    // ------------------------------------------------------------
    //  対局の開始（Start から）
    // ------------------------------------------------------------

    // 対局のシードと編成を、棋譜の再生の依頼（KifuSession）から決める。依頼がなければ新しいシード
    uint BeginKifuMatch()
    {
        KifuSession.MatchRequest request = KifuSession.TakeRequest();
        uint seed = MatchSetup.Seed ?? GameRng.NewSeed();
        if (request != null)
        {
            seed = request.Seed;
            requestedDecks = request.Decks;
            if (request.Replay != null)
            {
                replay = new ReplayProgress
                {
                    Kifu = request.Replay,
                    Total = request.Replay.Count,
                    Speed = Mathf.Max(1f, request.Speed),
                    Wait = ReplayFirstWait,
                    DebugEdited = request.Replay.debugEdited,
                };
                Time.timeScale = replay.Speed;
                Debug.Log($"[Kifu] 棋譜を再生する（全 {replay.Total} 手・{replay.Speed:0} 倍速・シード {seed}）");
                if (replay.DebugEdited) Debug.LogWarning("[Kifu] デバッグキーでマスを変えた対局の棋譜なので、途中で盤面がずれることがあります");
            }
        }
        KifuSession.LastReplayResult = null;
        return seed;
    }

    // 棋譜の再生の編成を使う（編成画面で選んだ編成（DeckTransferManager）を入れたあとに呼ぶ）
    void ApplyRequestedDecks()
    {
        if (requestedDecks == null || requestedDecks.Length != 2) return;
        if (player1 != null && requestedDecks[0] != null) player1.SetSkillDeck(requestedDecks[0]);
        if (player2 != null && requestedDecks[1] != null) player2.SetSkillDeck(requestedDecks[1]);
        requestedDecks = null;
    }

    // 記録を始める（シードと編成が決まったあと）
    void BeginKifuRecording(uint seed)
    {
        if (IsOnlineMatch)
        {
            KifuSession.SetCurrent(null, "オンライン対戦は、棋譜を記録しません");
            return;
        }
        if (IsRealtime)
        {
            // 合戦将棋型の行動は時刻（MatchAction.t）で決まるが、再生（StepReplay）は手番ごとに出すので、まだ記録しない
            KifuSession.SetCurrent(null, "合戦将棋型は、棋譜をまだ記録しません");
            return;
        }
        KifuSession.SetCurrent(KifuFile.Create(seed, GetSkillDeck(PlayerType.Player1), GetSkillDeck(PlayerType.Player2)), null);
    }

    // シーンを離れる（GameSystem.OnDestroy から）
    void OnDestroyKifu()
    {
        // 再生中にシーンを離れた（タイトルへ・結果画面へ）：速さを元に戻す
        if (replay != null) Time.timeScale = 1f;
    }

    // ------------------------------------------------------------
    //  記録
    // ------------------------------------------------------------

    // 盤面が変わり終わって、次の行動を受け付けられる状態か（棋譜のハッシュを取る時点・再生が次の行動を出す時点）
    bool IsKifuSettled => currentState == GameState.GameOver || (IsInTurn && !IsBusy);

    // 実行する行動を棋譜に足す（RecordAction から。行動を実行する直前）
    void RecordKifuAction(MatchAction record)
    {
        KifuFile k = KifuSession.Current;
        if (k == null) return;
        if (IsOnlineMatch)
        {
            KifuSession.SetCurrent(null, "オンライン対戦は、棋譜を記録しません");
            return;
        }
        FinalizeKifuHash(); // 前の行動のハッシュ（この行動の直前の盤面。もう取っていれば何もしない）
        k.actions.Add(KifuEntry.From(record));
    }

    // 最後の行動のハッシュがまだなら、今の盤面で付ける（盤面が変わり終わっているときに呼ぶ）
    void FinalizeKifuHash()
    {
        KifuFile k = KifuSession.Current;
        if (k == null || k.Count == 0) return;
        KifuEntry last = k.actions[k.Count - 1];
        if (!string.IsNullOrEmpty(last.hash)) return;
        FlushScheduledFx();
        last.hash = StateHash(); // MatchHash（GameSystem.State.cs。ActionHistory の hash と同じ計算）
    }

    // 演出の時刻に合わせて行う盤面の変更（SpecialContext.Schedule）を、まだなら今すぐ済ませる。
    // 演出を待ち終わって勝敗の判定・手番の交代をする前に呼ぶ（フレームの長さ・再生の速さで順番が前後しないように）
    void FlushScheduledFx()
    {
        if (specialFx != null) specialFx.FlushScheduled();
    }

    // デバッグキー（CellDebugTool）で盤面を変えた（その棋譜は再生しても同じ盤面にならない）
    public void MarkKifuDebugEdited()
    {
        KifuFile k = KifuSession.Current;
        if (k != null && !k.debugEdited)
        {
            k.debugEdited = true;
            Debug.LogWarning("[Kifu] デバッグキーでマスを変えたので、この対局の棋譜は再生しても同じ盤面になりません");
        }
    }

    // 毎フレーム（Update の最初）：盤面が変わり終わったら最後の行動のハッシュを付け、再生中なら次の行動を出す
    void UpdateKifu()
    {
        if (!IsKifuSettled) return;
        FlushScheduledFx();
        FinalizeKifuHash();
        if (replay != null) StepReplay();
    }

    // ------------------------------------------------------------
    //  保存（KifuMenuView・結果画面から）
    // ------------------------------------------------------------

    // 棋譜を保存できるか（記録していて、1 手以上ある。演出中・再生中でない）
    public bool CanSaveKifu(out string reason)
    {
        reason = null;
        KifuFile k = KifuSession.Current;
        if (k == null) reason = KifuSession.NoKifuReason ?? "棋譜を記録していません";
        else if (k.Count == 0) reason = "まだ 1 手も指していません";
        else if (IsReplaying) reason = "再生中は保存できません";
        else if (!IsKifuSettled) reason = "演出が終わってから保存してください";
        return reason == null;
    }

    // 棋譜を保存する（ファイルの保存ダイアログ）。戻り値：画面に出すメッセージ
    public string SaveKifu()
    {
        string reason;
        if (!CanSaveKifu(out reason)) return reason;
        FinalizeKifuHash();
        return KifuSession.SaveCurrent();
    }

    // ------------------------------------------------------------
    //  再生（Prototype の ReplayController）
    // ------------------------------------------------------------

    // 棋譜ファイルを選んで再生する（speed 倍速）。対局を始め直す。戻り値：始められなかった理由（始めたら null）
    public string LoadKifuAndReplay(float speed)
    {
        if (IsReplaying) return "再生中です";
        if (IsBusy) return "演出が終わってから読み込んでください";
        string error;
        string text = KifuFileDialog.OpenJson("棋譜を読み込んで再生", out error);
        if (text == null) return error; // キャンセル（error も null）・読めなかった
        try
        {
            return ReplayKifu(KifuFile.Parse(text), speed);
        }
        catch (KifuFormatException e)
        {
            return $"棋譜を読み込めません：{e.Message}";
        }
    }

    // 棋譜を最初から再生する（Prototype の replayKifu）：棋譜の編成・シードで Board シーンを読み込み直し、行動を順に実行する
    public string ReplayKifu(KifuFile kifu, float speed)
    {
        if (kifu == null) return "棋譜がありません";
        string error;
        SkillDeck[] decks = KifuSession.ResolveDecks(kifu.loadouts, KnownDecks(), out error);
        if (decks == null) return $"棋譜を再生できません：{error}";
        KifuSession.RequestReplay(kifu, decks, speed);
        ReloadMatchScene();
        return null;
    }

    // 再生を途中で止める（「再生を止める」ボタン。その局面から続けて指せる）
    public void StopReplay()
    {
        if (replay == null) return;
        EndReplay(new ReplayResult { Played = replay.Played, Total = replay.Total, Error = "途中で止めた", Stopped = true });
    }

    // 盤面が変わり終わったとき：直前の行動のハッシュを照らし合わせ、次の行動を出す（Prototype の chooseAction・onAction）
    void StepReplay()
    {
        ReplayProgress r = replay;
        KifuFile mine = KifuSession.Current;

        // 直前の行動のあとの盤面を、棋譜のハッシュと照らし合わせる
        if (r.Played > 0 && mine != null && mine.Count >= r.Played)
        {
            string recorded = r.Kifu.actions[r.Played - 1].hash;
            string actual = mine.actions[r.Played - 1].hash;
            if (!string.IsNullOrEmpty(recorded) && actual != recorded)
            {
                Debug.LogWarning($"[Kifu] {r.Played} 手目のあとの盤面のハッシュが違う：棋譜 {recorded} ／ 再生 {actual}");
                EndReplay(new ReplayResult { Played = r.Played, Total = r.Total, Error = $"{r.Played} 手目のあとの盤面が棋譜と一致しません" });
                return;
            }
        }

        if (currentState == GameState.GameOver)
        {
            string error = r.Played < r.Total ? $"{r.Played} 手目で対局が終わった（棋譜にはあと {r.Total - r.Played} 手ある）" : null;
            EndReplay(new ReplayResult { Played = r.Played, Total = r.Total, Error = error });
            return;
        }
        if (r.Played >= r.Total)
        {
            EndReplay(new ReplayResult { Played = r.Played, Total = r.Total });
            return;
        }

        // ルール説明・棋譜メニューを開いている間は進めない
        if (RulesOverlayView.IsBlockingInput || KifuMenuView.IsBlockingInput) return;
        r.Wait -= Time.deltaTime;
        if (r.Wait > 0f) return;

        MatchAction action = r.Kifu.actions[r.Played].ToAction();
        string reason;
        replayDispatching = true;
        try
        {
            if (!CanDispatch(action, out reason))
            {
                EndReplay(new ReplayResult { Played = r.Played, Total = r.Total, Error = $"{r.Played + 1} 手目の行動（{action}）を再現できない：{reason}" });
                return;
            }
            r.Played++;
            r.Wait = ReplayGap;
            Dispatch(action);
        }
        finally
        {
            replayDispatching = false;
        }
        ReplayUpdated?.Invoke(r);
    }

    void EndReplay(ReplayResult result)
    {
        if (replay == null) return;
        replay = null;
        Time.timeScale = 1f;
        KifuSession.LastReplayResult = result.Message;
        if (result.Ok) Debug.Log($"[Kifu] {result.Message}");
        else Debug.LogWarning($"[Kifu] {result.Message}");
        ReplayEnded?.Invoke(result);
        if (IsInTurn && !IsBusy)
        {
            UpdateActivePlayerSkillUI();
            if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
        }
    }

    // ------------------------------------------------------------
    //  局面の書き出し・読み込み（ファイル）。中身は GameSystem.State.cs の ExportState / ImportState
    //  （MatchSnapshot・MatchSnapshotIO・MatchHash）を使う。読み込みはシーンを読み込み直さず、その場で盤を作り直す
    // ------------------------------------------------------------

    // 今の局面を書き出せるか（時間停止中も書き出せる）
    public bool CanExportState(out string reason)
    {
        reason = null;
        if (!IsInTurn) reason = "対局中ではありません";
        else if (IsBusy || isRunningTurnEnd) reason = "演出が終わってから書き出してください";
        else if (IsReplaying) reason = "再生中は書き出せません";
        else if (IsOnlineMatch) reason = "オンライン対戦では使えません";
        else if (IsRealtime) reason = "合戦将棋型ではまだ使えません（対局の時間・連打の間隔を局面に入れていないため）";
        return reason == null;
    }

    // 今の局面をファイルに書き出す（保存ダイアログ）。戻り値：画面に出すメッセージ
    public string SaveState()
    {
        string reason;
        if (!CanExportState(out reason)) return reason;
        FlushScheduledFx();
        string json = ExportStateJson(true);
        if (json == null) return "局面を書き出せません";
        string fileName = $"ultimate-shogi-state-{System.DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture)}.json";
        string error;
        string path = KifuFileDialog.SaveJson("局面を書き出す", fileName, json, out error);
        if (path == null) return error;
        return $"局面を書き出した：{System.IO.Path.GetFileName(path)}";
    }

    // 局面のファイルを選んで読み込む（その場で盤を作り直して、その局面から続きを指す）。
    // 戻り値：画面に出すメッセージ（読み込んだら、成功のメッセージは StateImported を受けた側が出すので null）
    public string LoadState()
    {
        if (IsReplaying) return "再生中は読み込めません";
        string blocked = ImportBlockReason;
        if (blocked != null) return blocked;
        string error;
        string text = KifuFileDialog.OpenJson("局面を読み込む", out error);
        if (text == null) return error; // キャンセル（error も null）・読めなかった
        MatchSnapshot s = MatchSnapshot.FromJson(text);
        if (s == null) return "局面を読み込めません：JSON として読めません";
        string reason;
        if (!ImportState(s, out reason)) return $"局面を読み込めません：{reason}";
        // 読み込んだ局面からは棋譜を記録しない（Prototype の importState と同じ）
        KifuSession.SetCurrent(null, "局面を読み込んだ対局は、棋譜を記録しません");
        return null;
    }

    // ------------------------------------------------------------
    //  共通
    // ------------------------------------------------------------

    // 編成の魔法を Id から探すときに見る編成（今の対局の編成・編成画面で選んだ編成）
    IEnumerable<SkillDeck> KnownDecks()
    {
        if (player1 != null) yield return player1.SkillDeck;
        if (player2 != null) yield return player2.SkillDeck;
        yield return DeckTransferManager.Player1SelectedDeck;
        yield return DeckTransferManager.Player2SelectedDeck;
    }

    // 対局のシーン（Board）を読み込み直す（棋譜の再生の依頼は KifuSession に入れてから）
    void ReloadMatchScene()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }
}
