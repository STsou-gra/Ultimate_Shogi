using System.Collections.Generic;
using UnityEngine;

// 局面の書き出し・読み込み（Prototype の Game.exportState / importState）と、行動ごとの盤面のハッシュ。GameSystem の続き（partial）。
//
//  ・ExportState … 今の局面を MatchSnapshot にする（JSON は ExportStateJson）。いつでも書き出せるが、
//                  演出の途中（IsResolvingAction）に書き出すと、行動の処理の途中の局面になる
//  ・ImportState … MatchSnapshot を読み込み、盤・駒の GameObject・マス・持ち駒・エナジー・魔法の編成・時間停止の表示・UI を作り直す。
//                  その局面から続けて指せる。対局中の手番で、演出中でないときだけ読み込める（オンライン対戦中は読み込まない）。
//                  操作の途中（駒・持ち駒・対象の選択、成りの確認）は取り消す。棋譜（ActionHistory）は読み込んだ局面から記録し直す
//  ・StateHash   … 今の盤面のハッシュ（MatchHash）。行動ごとに、その行動の処理が終わった時点のハッシュを MatchAction.hash に残す
//  ・エディターでは GameSystem のコンポーネントの右クリックメニュー（「局面を書き出す」「局面を読み込む」）から、
//    クリップボードを通して書き出し・読み込みを試せる（遊び方には関係しない）
public partial class GameSystem
{
    // 局面を読み込んで、盤・駒・データを作り直した（見た目を合わせ直す。BoardCellView はマスの BoardCells.Reloaded を見る）
    public event System.Action StateImported;

    // 行動の処理が終わって、その時点の盤面のハッシュを hash に入れた（棋譜の照合用）
    public event System.Action<MatchAction> ActionHashed;

    // ハッシュをまだ入れていない、処理中の行動（ActionHistory の最後の行動）
    private MatchAction hashPendingAction;

    // ------------------------------------------------------------
    //  書き出し
    // ------------------------------------------------------------

    // 今の局面（対局の準備ができる前なら null）
    public MatchSnapshot ExportState()
    {
        if (match == null || gameBoard == null) return null;
        MatchSnapshot s = MatchSnapshotIO.Capture(match, gameBoard.Cells, gameBoard.GetBoardData(), timeStop,
            new[] { HandOf(PlayerType.Player1), HandOf(PlayerType.Player2) },
            new[] { GetSkillDeck(PlayerType.Player1), GetSkillDeck(PlayerType.Player2) });
        s.extraTurn = hasExtraTurn;
        s.gameOver = currentState == GameState.GameOver;
        s.draw = s.gameOver && isDraw;
        s.winner = s.gameOver && !isDraw ? (int)winner : -1;
        foreach (PositionRecord r in positionHistory)
        {
            s.positions.Add(new PositionSnapshot { key = r.key, side = (int)r.sideToMove, inCheck = r.inCheck });
        }
        return s;
    }

    public string ExportStateJson(bool prettyPrint = false)
    {
        MatchSnapshot s = ExportState();
        return s != null ? s.ToJson(prettyPrint) : null;
    }

    // 今の盤面のハッシュ（MatchHash。対局の準備ができる前なら ""）
    public string StateHash()
    {
        return MatchHash.Compute(ExportState());
    }

    Dictionary<PieceType, int> HandOf(PlayerType owner)
    {
        Player p = PlayerOf(owner);
        return p != null ? p.Hand : null;
    }

    // ------------------------------------------------------------
    //  読み込み
    // ------------------------------------------------------------

    // 今読み込めるか（読み込めないなら理由）
    public string ImportBlockReason
    {
        get
        {
            if (match == null || gameBoard == null) return "対局の準備ができていません";
            if (!IsInTurn) return "対局中の手番ではありません";
            if (IsBusy || isRunningTurnEnd) return "演出中です";
            if (IsOnlineMatch) return "オンライン対戦中は読み込めません";
            return null;
        }
    }

    public bool ImportState(MatchSnapshot snapshot)
    {
        return ImportState(snapshot, out _);
    }

    // 局面を読み込む。読み込めなければ何も変えずに false と理由
    public bool ImportState(MatchSnapshot snapshot, out string reason)
    {
        reason = ImportBlockReason ?? MatchSnapshotIO.Validate(snapshot);
        if (reason != null)
        {
            Debug.LogWarning($"局面を読み込めません：{reason}");
            return false;
        }
        snapshot = snapshot.Clone(); // 渡された局面は書き換えない

        // 操作の途中をやめる
        if (isWaitingForPromoteChoice) CancelPromotionChoice();
        if (targetSelection != null) EndTargetSelection();
        ResetSelectionState();

        // 今の駒を消す（盤上の駒と、時間停止の予定の駒）
        if (timeStop != null)
        {
            foreach (GamePiece q in timeStop.Held) gameBoard.DestroyDetached(q);
        }
        gameBoard.RemoveAllPieces();
        timeStop = null;
        resolvingTimeStop = null;
        gameBoard.TimeStop = null;

        // データを戻して、駒を作り直す
        MatchSnapshotIO.Restore(snapshot, match, gameBoard.Cells, SpawnSnapshotPiece, out TimeStopState ts, out _);
        timeStop = ts;
        gameBoard.TimeStop = ts;
        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            Player p = PlayerOf(owner);
            PlayerSnapshot ps = snapshot.Player(owner);
            if (p == null) continue;
            MatchSnapshotIO.RestoreHand(ps, p.Hand);
            p.SetSkillDeck(MatchSnapshotIO.BuildDeck(ps.loadout, FindSkillById));
            p.BindEnergy(match.Energy); // エナジーの表示を合わせ直す
        }

        hasExtraTurn = snapshot.extraTurn;
        positionHistory.Clear();
        positionCounts.Clear();
        foreach (PositionSnapshot r in snapshot.positions)
        {
            positionHistory.Add(new PositionRecord { key = r.key ?? "", sideToMove = (PlayerType)r.side, inCheck = r.inCheck });
            positionCounts.TryGetValue(r.key ?? "", out int count);
            positionCounts[r.key ?? ""] = count + 1;
        }
        unlockQueue.Clear();
        pendingActionFxTime = 0f;
        captureFxDelay = 0f;
        enterCellFxDelay = 0f;
        turnNotice = "";

        // 棋譜は読み込んだ局面から記録し直す（Prototype と同じ）
        actionHistory.Clear();
        hashPendingAction = null;

        // 手番・表示
        PlayerType side = match.Turn.Current;
        currentState = side == PlayerType.Player1 ? GameState.Player1Turn : GameState.Player2Turn;
        if (gameCamera != null) gameCamera.RotateToPlayer(side);
        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, side);
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.SetTurnInfo(match.Turn.Label);
            HandUIManager.Instance.UpdateActiveHandDisplay(side, CurrentPlayer.Hand);
        }
        UpdateActivePlayerSkillUI();
        StateImported?.Invoke();
        if (ts != null)
        {
            foreach (GamePiece q in ts.Held) PieceHeldChanged?.Invoke(q, true, MatchSnapshotIO.IsGrayHeld(ts, q));
            TimeStopUpdated?.Invoke(ts, false);
        }
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
        Debug.Log($"局面を読み込みました（{match.Turn.Label}・{TurnEndAction.SideName(side)}の手番・盤面のハッシュ {StateHash()}）");

        // 勝敗が決まった局面なら、結果画面へ
        if (snapshot.gameOver)
        {
            isDraw = snapshot.draw;
            if (!isDraw) winner = (PlayerType)snapshot.winner;
            currentState = GameState.GameOver;
        }
        return true;
    }

    public bool ImportStateJson(string json)
    {
        MatchSnapshot s = MatchSnapshot.FromJson(json);
        if (s == null)
        {
            Debug.LogWarning("局面を読み込めません：JSON として読めません");
            return false;
        }
        return ImportState(s);
    }

    // 書き出した駒を作って盤に置く（予定の駒は盤のデータに入れず、その場に置く）
    GamePiece SpawnSnapshotPiece(PieceSnapshot ps)
    {
        GamePiece piece = gameBoard.SpawnPiece(ps.type, (PlayerType)ps.owner, ps.x, ps.y, !ps.held);
        if (piece == null) return null;
        MatchSnapshotIO.ApplyPiece(ps, piece);
        RefreshSpecialReady(piece);
        return piece;
    }

    // 魔法を Id で探す（今の編成 → 編成の候補 Resources/SkillDatabase）
    SkillDefinition FindSkillById(string id)
    {
        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            SkillDefinition skill = FindDeckSkill(owner, id);
            if (skill != null) return skill;
        }
        SkillDatabase db = SkillDatabase.LoadDefault();
        if (db == null || db.allSkills == null) return null;
        foreach (SkillDefinition skill in db.allSkills)
        {
            if (skill != null && skill.Id == id) return skill;
        }
        return null;
    }

    // ------------------------------------------------------------
    //  行動ごとの盤面のハッシュ（Prototype の Match.play の entry.hash）
    // ------------------------------------------------------------

    // 行動の処理が終わったか（演出・手番の終わりの自動行動・手番交代・超常魔法の解放の演出まで済んで、次の操作を待っている／対局が終わった）
    bool IsActionSettled => currentState == GameState.GameOver || (IsInTurn && !IsBusy && !isRunningTurnEnd);

    // 毎フレーム（Update の最初）：処理中の行動が終わっていれば、ハッシュを残す
    void UpdateActionHash()
    {
        if (hashPendingAction != null && IsActionSettled) FinishActionHash();
    }

    // 処理中の行動に、今の盤面のハッシュを入れる（次の行動を記録する前にも呼ぶ）
    void FinishActionHash()
    {
        MatchAction a = hashPendingAction;
        if (a == null) return;
        hashPendingAction = null;
        a.hash = StateHash();
        Debug.Log($"[Action] {a} のあとの盤面のハッシュ: {a.hash}");
        ActionHashed?.Invoke(a);
    }

    // ------------------------------------------------------------
    //  エディターで試す（GameSystem の右クリックメニュー。クリップボードを通す）
    // ------------------------------------------------------------

    [ContextMenu("局面を書き出す（クリップボードへ）")]
    void ExportStateToClipboard()
    {
        string json = ExportStateJson(true);
        if (json == null)
        {
            Debug.LogWarning("対局の準備ができていないので、局面を書き出せません");
            return;
        }
        GUIUtility.systemCopyBuffer = json;
        Debug.Log($"局面を書き出しました（クリップボードにコピー済み・盤面のハッシュ {StateHash()}）\n{json}");
    }

    [ContextMenu("局面を読み込む（クリップボードから）")]
    void ImportStateFromClipboard()
    {
        ImportStateJson(GUIUtility.systemCopyBuffer);
    }
}
