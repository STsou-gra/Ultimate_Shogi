using System.Collections.Generic;
using UnityEngine;

// 時間停止（超常魔法「停止世界」。Prototype の src/core/timeStop.js・effects.js の defer〜・turn.js の resolveTimeStop / afterAction）。
// GameSystem の続き（partial）。状態の形は Match/TimeStopState.cs、見た目は TimeStopView（色の反転・予定の表示・残り回数と「解除」ボタン）
// と MagicFx.TimeStop.cs（時計・ガラスが割れる演出）。
//
//  ・発動すると TimeStopEffect が StartTimeStop を呼ぶ → 手番の終わり（NextState）で「時よ止まれ」のカットイン
//  ・時間停止中は 1 回行動するごとに手番を終えず、残り回数を減らす（TimeStopAfterAction。移動・打つ・魔法・必殺技・狙撃が各 1 回。
//    錬成は数えない）。回数を使い切るか「解除」（Q キー・ボタン）で、予定をまとめて処理する（ResolveTimeStop）
//  ・時間停止中は、行動による効果をその場では起こさず「予定」として記録する。振り分けは共通入口で行う：
//      CapturePiece（取り）→ DeferCapture   … 相手の駒を盤のデータから外し、灰色でその場に残す（以降はないものとして扱う）
//      VanishPiece（消失）→ DeferDoom      … 隕石・破壊砲などで消える駒。同じく灰色で残す
//      Explode（爆発）    → DeferExplode   … 火薬を外して残す（灰色にはしない）。火薬に入った駒はもう動かせない
//      EnterCell（炎上・落とし穴）→ DeferEnterVanish … 駒はそのマスに残るが、もう動かせない
//      成り（PromotePiece・必殺技の成り）→ DeferPromote … 1 つの駒につき 1 回。時間停止中は成る前の動きのまま
//      BreakCell / SetBurning / SetTrap → DeferCell … マスの変化（破壊・炎上・落とし穴の設置）
//    そのため移動・打つ・魔法・必殺技・狙撃は、通常と同じ処理のまま時間停止に対応する
//  ・時間停止中はエナジーが増えない（AddPoint）。手番が終わらないので、ファランクス・アンデッドの自動行動も止まり、
//    解除したあとの手番の終わりに通常どおり行う
//  ・解除：ガラスが割れる演出 → 予定を記録した順に処理する。取りは盾も有効で、防がれた駒はそのマスに残り、
//    そこにいる駒は来た道を押し戻される。爆発は、取られる予定で残っている駒も巻き込む。最後に勝敗を判定してから手番の流れに戻る
public partial class GameSystem
{
    // 時間停止の演出・ログの色（Prototype の time_stop.js の color）
    public static readonly Color TimeStopColor = new Color(1f, 0xb3 / 255f, 0f);
    public const string TimeStopTitle = "停止世界";
    // 解除のときに、予定の駒の居場所がなくなったときの消失の理由（念のため。通常は起きない）
    public const string ReasonTimeStop = "時空のひずみ";

    private TimeStopState timeStop;          // 時間停止中の状態（それ以外は null）
    private TimeStopState resolvingTimeStop; // 解除して予定を処理している間の状態（予定の駒を盤上の駒として倒せるようにする）

    public TimeStopState TimeStop => timeStop;
    public bool IsTimeStopped => timeStop != null;

    // 時間停止が始まった（色を反転し、残り回数を出す）
    public event System.Action<TimeStopState> TimeStopStarted;
    // 残り回数が減った・予定が増えた（表示を更新する。counted なら残り回数の表示を弾ませる）
    public event System.Action<TimeStopState, bool> TimeStopUpdated;
    // 時間が動き出す（ガラスが割れる演出。予定はこのあと処理する）
    public event System.Action<TimeStopState> TimeStopResolving;
    // 予定の表示を更新する（upto 番目までを処理済みとして描く。state が null なら消す）。解除の演出の時刻に合わせて呼ぶ
    public event System.Action<TimeStopState, int> TimeStopMarks;
    // 予定の駒として、その場に残す（held = true。gray なら灰色）／元の見た目に戻す（held = false）
    public event System.Action<GamePiece, bool, bool> PieceHeldChanged;

    // 消失・爆発の予定ができた駒か（その駒はもう動かせず、必殺技・狙撃も使えない）
    public bool IsFrozenInTimeStop(GamePiece piece) => timeStop != null && timeStop.IsFrozen(piece);

    // 「解除」できるか（時間停止中の、発動したプレイヤーの操作できるとき）。「解除」ボタンを押せるか
    public bool CanReleaseTimeStop =>
        timeStop != null && CanActNow && ReleaseBlockReason(CurrentSide) == null;

    // 時間停止中の案内文（Prototype の PlayerPanel.js の timeStopInfo）
    string TimeStopStatusText =>
        $"【時間停止中】 あと {timeStop.Remaining} 回行動できる（移動・打つ・魔法・必殺技・狙撃。錬成は数えない）\n" +
        "取り・爆発・成り・マスの変化は、時間が動き出してからまとめて起こる（解除: Q）";

    // ------------------------------------------------------------
    //  開始・回数・解除
    // ------------------------------------------------------------

    // 時間停止を始める（停止世界の効果。Prototype の play.js の startTimeStop）。発動した行動は回数に数えない
    public void StartTimeStop(PlayerType owner, int moves)
    {
        if (timeStop != null || gameBoard == null || moves <= 0) return;
        timeStop = new TimeStopState(owner, moves);
        gameBoard.TimeStop = timeStop;
        Debug.Log($"時が止まった！ {TurnEndAction.SideName(owner)}は {moves} 回まで続けて行動できる（取り・エナジー・爆発などは時間が動き出したときにまとめて起こる）");
        TimeStopStarted?.Invoke(timeStop);
    }

    // 行動が終わったとき（手番を使う行動は NextState、手番を使わない魔法は ExecuteSkill から）：時間停止中なら回数を数える。
    // 回数が残っていれば手番を続け、使い切ったら予定をまとめて処理する（endsTurn なら、そのあと手番の流れに戻る）。
    // 時間停止中でなければ false（呼び出し側が通常どおり続ける）
    bool TimeStopAfterAction(bool endsTurn)
    {
        TimeStopState ts = timeStop;
        if (ts == null) return false;

        if (ts.JustStarted)
        {
            // 停止世界を発動した行動（数えない）→「時よ止まれ」
            ts.JustStarted = false;
            AnnounceTimeStop(ts);
            return true;
        }

        if (!ts.Ending)
        {
            ts.Remaining--;
            if (ts.Remaining <= 0) ts.Ending = true;
            TimeStopUpdated?.Invoke(ts, true);
        }
        if (!ts.Ending)
        {
            ContinueTimeStop();
            return true;
        }
        ResolveTimeStop(endsTurn);
        return true;
    }

    // 「時よ止まれ」のカットイン（その間は操作できない）→ 時間停止中の操作へ
    async void AnnounceTimeStop(TimeStopState ts)
    {
        isResolvingAction = true;
        ResetSelectionState();
        try
        {
            string side = TurnEndAction.SideName(ts.Owner);
            await CutInEffect.Instance.PlayAsync("時よ止まれ", $"{side}・{ts.Total} 回続けて行動できる", TimeStopColor, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }
        isResolvingAction = false;
        if (!IsInTurn || timeStop != ts) return;
        ContinueTimeStop();
    }

    // 時間停止中、続けて次の行動を選べるようにする（手番は変わらない）
    void ContinueTimeStop()
    {
        isSelectingHand = false;
        isDroppingHand = false;
        UpdateActivePlayerSkillUI();
        if (HandUIManager.Instance != null)
        {
            HandUIManager.Instance.UpdateActiveHandDisplay(CurrentPlayer.PlayerType, CurrentPlayer.Hand);
            HandUIManager.Instance.SetStatusText(IdleStatusText);
        }
        TimeStopUpdated?.Invoke(timeStop, false);
    }

    // 「解除」（Q キー・ボタン）：「停止世界の解除」行動を Dispatch
    public void ReleaseTimeStop()
    {
        if (!CanReleaseTimeStop) return;
        Dispatch(MatchAction.ReleaseTimeStop());
    }

    // 解除の実行（Dispatch の ExecutePlan から。Prototype の play.js の releaseTimeStop）：回数が残っていても時間停止を終え、手番の流れに戻る
    void ExecuteReleaseTimeStop()
    {
        timeStop.Ending = true;
        Debug.Log($"{TurnEndAction.SideName(timeStop.Owner)}：時間停止を解除した（残り {timeStop.Remaining} 回）");
        ResolveTimeStop(true);
    }

    // ------------------------------------------------------------
    //  予定にする（Prototype の effects.js の defer〜）
    // ------------------------------------------------------------

    // 時間停止中の駒の 1 マスの移動を記録する（移動・必殺技の Step から。盾で押し戻すときに来た道をたどる）
    public void RecordTimeStopMove(GamePiece piece, int toX, int toY)
    {
        if (timeStop == null || piece == null) return;
        GamePiece target = gameBoard.GetPieceAt(toX, toY);
        timeStop.Moves.Add(new TimeStopMove
        {
            Piece = piece,
            From = new Vector2Int(piece.X, piece.Y),
            To = new Vector2Int(toX, toY),
            Captured = target != null && target != piece && target.player != piece.player && !PieceRules.IsExplosive(target.type),
        });
    }

    // 時間停止中に移動回数を数えた（盾で押し戻して移動を取り消すとき、数え直す）
    void MarkTimeStopMoveCounted(GamePiece piece)
    {
        if (timeStop == null) return;
        TimeStopMove m = timeStop.LastMove(piece);
        if (m != null) m.Counted = true;
    }

    // 成る（時間停止中は成りの予定にする）
    void PromotePiece(GamePiece piece)
    {
        if (timeStop != null) DeferPromote(piece);
        else
        {
            piece.Promote();
            if (basicMoveFx != null) basicMoveFx.Promoted(piece);
        }
    }

    void AddPending(TimeStopPending e, string label)
    {
        timeStop.Pending.Add(e);
        Debug.Log($"（時間停止中）({e.At.x}, {e.At.y})：{label}の予定");
        TimeStopUpdated?.Invoke(timeStop, false);
    }

    // 予定の駒として、盤のデータから外してその場に残す
    void HoldPiece(GamePiece q, bool gray)
    {
        gameBoard.DetachPiece(q);
        timeStop.Held.Add(q);
        PieceHeldChanged?.Invoke(q, true, gray);
    }

    // 取りの予定：相手の駒を盤のデータから外し、灰色でその場に残す（盾の判定・エナジー・持ち駒化は時間が動き出してから）
    void DeferCapture(GamePiece target, PlayerType by, GamePiece attacker)
    {
        Vector2Int at = new Vector2Int(target.X, target.Y);
        Vector2Int dir = ActiveSpecial != null ? ActiveSpecial.HitDirection
            : attacker != null ? new Vector2Int(System.Math.Sign(at.x - attacker.X), System.Math.Sign(at.y - attacker.Y)) : Vector2Int.zero;
        HoldPiece(target, true);
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Capture,
            Target = target,
            TargetType = target.type,
            ByOwner = by,
            Piece = attacker,
            Move = timeStop.LastMove(attacker, at),
            At = at,
            HitDirection = dir,
        }, $"{HandUIManager.GetJapaneseName(target.type)}を取る");
    }

    // 消失の予定（隕石・破壊砲などで直接消える駒）：盤のデータから外し、灰色でその場に残す（以降はないものとして扱う）
    void DeferDoom(GamePiece q, string reason)
    {
        Vector2Int at = new Vector2Int(q.X, q.Y);
        HoldPiece(q, true);
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Vanish,
            Target = q,
            TargetType = q.type,
            Reason = reason,
            At = at,
            HitDirection = ActiveSpecial != null ? ActiveSpecial.HitDirection : Vector2Int.zero,
        }, $"{HandUIManager.GetJapaneseName(q.type)}が{reason}で消失");
    }

    // 消失の予定（炎上・落とし穴のマスに入った駒。駒はそのマスに残るが、もう動かせない）
    void DeferEnterVanish(GamePiece piece, int x, int y, string reason)
    {
        Vector2Int at = new Vector2Int(x, y);
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Vanish,
            Piece = piece,
            Move = timeStop.LastMove(piece, at),
            Reason = reason,
            At = at,
        }, $"{HandUIManager.GetJapaneseName(piece.type)}が{reason}で消失");
    }

    // 爆発の予定：中心の火薬を盤のデータから外してその場に残す。火薬に入った駒（entering）はもう動かせない
    void DeferExplode(int cx, int cy, GamePiece entering)
    {
        Vector2Int at = new Vector2Int(cx, cy);
        GamePiece center = gameBoard.GetPieceAt(cx, cy);
        GamePiece bomb = center != null && center != entering && PieceRules.IsExplosive(center.type) ? center : null;
        if (bomb != null) HoldPiece(bomb, false);
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Explode,
            Target = bomb,
            TargetType = bomb != null ? bomb.type : PieceType.Gunpowder,
            Piece = entering,
            Move = timeStop.LastMove(entering, at),
            At = at,
        }, "爆発");
    }

    // 成りの予定（1 つの駒につき 1 回。時間停止中は成る前の動きのまま）
    void DeferPromote(GamePiece piece)
    {
        if (piece == null || timeStop.HasPendingPromote(piece)) return;
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Promote,
            Piece = piece,
            Move = timeStop.LastMove(piece),
            At = new Vector2Int(piece.X, piece.Y),
        }, $"{HandUIManager.GetJapaneseName(piece.type)}の成り");
    }

    // マスの状態の変化の予定（落とし穴の設置・破壊・炎上）
    void DeferCell(TimeStopCellEffect effect, int x, int y, PlayerType owner, int turns)
    {
        string label = effect == TimeStopCellEffect.Trap ? "落とし穴の設置" : effect == TimeStopCellEffect.Break ? "マスの破壊" : "炎上";
        AddPending(new TimeStopPending
        {
            Kind = TimeStopKind.Cell,
            CellEffect = effect,
            CellOwner = owner,
            Turns = turns,
            At = new Vector2Int(x, y),
        }, label);
    }

    // ------------------------------------------------------------
    //  解除：予定をまとめて処理する（Prototype の turn.js の resolveTimeStop・resolvePending）
    // ------------------------------------------------------------

    // ガラスが割れる演出 → 予定を記録した順に処理 → 演出を待つ → 勝敗の判定 →（endsTurn なら）手番の終わりの自動行動・手番交代
    async void ResolveTimeStop(bool endsTurn)
    {
        TimeStopState ts = timeStop;
        if (ts == null) return;
        isResolvingAction = true;
        ResetSelectionState();
        timeStop = null;
        if (gameBoard != null) gameBoard.TimeStop = null;
        resolvingTimeStop = ts;

        int count = ts.Pending.Count;
        string message = count > 0 ? $"止まっていた間の {count} 件の効果が一気に起こる！" : "";
        Debug.Log($"時は動き出す…… {message}");
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText($"【時は動き出す】 {message}");
        if (magicFx != null) magicFx.TimeStopShatter(0f);
        TimeStopResolving?.Invoke(ts);

        SpecialContext tl = new SpecialContext(this, gameBoard, ts.Owner, specialFx);
        tl.Wait(MagicFx.TimeStopResumeTime);
        pendingActionFxTime = 0f;
        ActiveSpecial = tl;
        try
        {
            RunTimeStopPending(ts, tl);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
            ActiveSpecial = null;
            resolvingTimeStop = null;
        }

        float wait = Mathf.Max(pendingActionFxTime, tl.Time + 0.3f);
        pendingActionFxTime = 0f;
        try
        {
            await Awaitable.WaitForSecondsAsync(wait, destroyCancellationToken);
        }
        catch (System.OperationCanceledException)
        {
            return; // シーンを離れた
        }

        isResolvingAction = false;
        UpdateActivePlayerSkillUI();
        if (ResolveKingLoss()) return; // 予定の取り・爆発などで王将がいなくなった
        if (!IsInTurn) return;
        if (endsTurn)
        {
            NextState(); // 2回行動の続き・ファランクス・アンデッドの自動行動 → 手番交代
            return;
        }

        // 手番を使わない魔法で回数を使い切った：手番は続く（Prototype の afterAction の endsTurn === false）
        isActivePlayerInCheck = CheckRules.IsInCheck(gameBoard, ts.Owner);
        if (HandUIManager.Instance != null) HandUIManager.Instance.SetStatusText(IdleStatusText);
        CheckTranscendentUnlock();
    }

    // 予定を記録した順に処理する。盤面はすぐ変わり、演出は tl の時刻どおりに再生される
    void RunTimeStopPending(TimeStopState ts, SpecialContext tl)
    {
        for (int i = 0; i < ts.Pending.Count; i++)
        {
            TimeStopPending e = ts.Pending[i];
            ResolveTimeStopPending(ts, e, tl);
            e.Done = true;
            int upto = i;
            tl.Schedule(() => TimeStopMarks?.Invoke(ts, upto));
            tl.Wait(0.08f);
        }

        // 念のため：処理しきれずに残った予定の駒は、マスが空いていれば元に戻す（ふさがっていれば消す）
        foreach (TimeStopPending e in ts.Pending)
        {
            GamePiece q = ts.HeldPiece(e);
            if (q == null) continue;
            PieceHeldChanged?.Invoke(q, false, false);
            if (gameBoard.HasPieceAt(q.X, q.Y))
            {
                tl.MarkHit(Vector2Int.zero);
                VanishPiece(q, ReasonTimeStop);
            }
            else
            {
                ts.Held.Remove(q);
                gameBoard.AttachPiece(q, q.X, q.Y);
            }
        }
        tl.Schedule(() => TimeStopMarks?.Invoke(null, -1));
    }

    // 予定を 1 つ処理する。それまでの処理の結果、意味がなくなった予定は飛ばす
    void ResolveTimeStopPending(TimeStopState ts, TimeStopPending e, SpecialContext tl)
    {
        if (TimeStopState.IsUndone(e)) return; // 盾で押し戻されて取り消された移動の予定
        GamePiece p = e.Piece;

        switch (e.Kind)
        {
            case TimeStopKind.Capture:
                ResolveTimeStopCapture(ts, e, tl);
                return;

            case TimeStopKind.Explode:
            {
                GamePiece bomb = e.Target;
                if (bomb != null && !IsOnBoard(bomb)) return; // 先にほかの効果で消えた
                if (bomb != null) PieceHeldChanged?.Invoke(bomb, false, false);
                // 範囲にいる駒（取られる予定で残っている駒・この火薬も含む）が消失する
                Explode(e.At.x, e.At.y, tl.Time, null, HeldPieces(ts, e, bomb));
                tl.Wait(0.6f);
                return;
            }

            case TimeStopKind.Cell:
                tl.MarkHit(Vector2Int.zero);
                if (e.CellEffect == TimeStopCellEffect.Trap)
                {
                    if (SetTrap(e.At.x, e.At.y, e.CellOwner)) Debug.Log($"({e.At.x}, {e.At.y}) に落とし穴が現れた");
                }
                else if (e.CellEffect == TimeStopCellEffect.Burn)
                {
                    SetBurning(e.At.x, e.At.y, e.Turns);
                }
                else if (BreakCell(e.At.x, e.At.y))
                {
                    // 予定の駒も穴に落ちる
                    foreach (GamePiece q in HeldPieces(ts, e, null))
                    {
                        if (IsOnBoard(q) && q.X == e.At.x && q.Y == e.At.y)
                        {
                            PieceHeldChanged?.Invoke(q, false, false);
                            VanishPiece(q, BoardCells.ReasonBroken);
                        }
                    }
                }
                tl.Wait(0.2f);
                return;

            case TimeStopKind.Vanish:
                if (e.Target != null)
                {
                    // 隕石・破壊砲などで消える予定の駒
                    GamePiece q = e.Target;
                    if (!IsOnBoard(q)) return;
                    PieceHeldChanged?.Invoke(q, false, false);
                    tl.MarkHit(e.HitDirection);
                    VanishPiece(q, e.Reason);
                    tl.Wait(0.15f);
                    return;
                }
                // 炎上・落とし穴に入った駒：そのマスから押し出されていなければ消失する
                if (p == null || gameBoard.GetPieceAt(e.At.x, e.At.y) != p) return;
                tl.MarkHit(Vector2Int.zero);
                EnterCell(p, e.At.x, e.At.y);
                tl.Wait(0.45f);
                return;

            case TimeStopKind.Promote:
                if (p == null || !IsOnBoard(p) || p.isPromoted) return;
                p.isPromoted = true;
                tl.Schedule(() => { if (p != null) p.UpdateVisual(); });
                Debug.Log($"({p.X}, {p.Y})：{TurnEndAction.SideName(p.player)}の{HandUIManager.GetJapaneseName(p.type)}が成った");
                tl.Wait(0.1f);
                return;
        }
    }

    // 取りの予定：取る（エナジー・持ち駒化）。盾に防がれたら、取られる予定だった駒はそのマスに残り、
    // そこにいる駒は来た道を押し戻す
    void ResolveTimeStopCapture(TimeStopState ts, TimeStopPending e, SpecialContext tl)
    {
        GamePiece q = e.Target;
        if (!IsOnBoard(q)) return; // 先に爆発などで消えた
        GamePiece attacker = e.Piece != null ? e.Piece : null;
        Vector2Int at = new Vector2Int(q.X, q.Y);
        string where = $"({at.x}, {at.y})";
        string name = HandUIManager.GetJapaneseName(q.type);
        string side = TurnEndAction.SideName(q.player);

        PieceHeldChanged?.Invoke(q, false, false);
        tl.MarkHit(e.HitDirection);
        if (CapturePiece(q, e.ByOwner, attacker))
        {
            Debug.Log($"{where}：{side}の{name}を取った！");
            tl.Wait(0.15f);
            return;
        }

        // 盾に防がれた：取られる予定だった駒はそのマスに残る。そこにいる駒は来た道を押し戻す
        if (e.Move != null) e.Move.Bounced = true;
        GamePiece o = gameBoard.GetPieceAt(at.x, at.y);
        if (o != null && !RewindPiece(ts, o, tl, new HashSet<GamePiece>()) && !DisplacePiece(ts, o, tl))
        {
            CapturePiece(q, e.ByOwner, attacker); // 盤が埋まっていて押し出せないときだけ（盾はもう割れている。実際には起こらない）
            return;
        }
        ts.Held.Remove(q);
        gameBoard.AttachPiece(q, at.x, at.y);
        Debug.Log($"{where}：{side}の{name}は盾で守られた！");
        tl.Wait(0.2f);
    }

    // まだ処理していない予定の駒（except を除く）と、extra
    List<GamePiece> HeldPieces(TimeStopState ts, TimeStopPending except, GamePiece extra)
    {
        List<GamePiece> list = new List<GamePiece>();
        foreach (TimeStopPending h in ts.Pending)
        {
            if (h.Done || h == except) continue;
            GamePiece q = ts.HeldPiece(h);
            if (q != null && !list.Contains(q)) list.Add(q);
        }
        if (extra != null && !list.Contains(extra)) list.Add(extra);
        return list;
    }

    // 駒 p を、時間停止中に通ってきたマスを逆にたどって押し戻す（たどった分の移動は取り消し）。
    // 戻り先にほかの自分の駒がいれば、その駒も同じように押し戻す。取りをした移動より前にはさかのぼらない
    // （盾に防がれた取りの移動はさかのぼれる）。戻れたかを返す
    bool RewindPiece(TimeStopState ts, GamePiece p, SpecialContext tl, HashSet<GamePiece> visiting)
    {
        visiting.Add(p);
        List<TimeStopMove> moves = ts.Moves.FindAll(m => m.Piece == p && !m.Undone);
        for (int k = moves.Count - 1; k >= 0; k--)
        {
            TimeStopMove m = moves[k];
            if (m.Captured && !m.Bounced) break;
            GamePiece o = gameBoard.GetPieceAt(m.From.x, m.From.y);
            if (o != null && (o.player != p.player || visiting.Contains(o) || !RewindPiece(ts, o, tl, visiting))) continue;
            UndoTimeStopMoves(ts, p, moves.GetRange(k, moves.Count - k), tl);
            MovePieceBack(p, m.From.x, m.From.y, tl);
            return true;
        }
        return false;
    }

    // 押し戻せないときの予備：駒 p を近くの空きマス（破壊・炎上・落とし穴・予定の駒がないマス）へ移す
    bool DisplacePiece(TimeStopState ts, GamePiece p, SpecialContext tl)
    {
        Vector2Int? best = null;
        int bestD = int.MaxValue;
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                BoardCell cell = Cells.At(x, y);
                if (gameBoard.HasPieceAt(x, y) || cell == null || cell.State != CellState.Normal || cell.HasTrap || ts.IsHeldSquare(x, y)) continue;
                int dx = Mathf.Abs(x - p.X), dy = Mathf.Abs(y - p.Y);
                int d = Mathf.Max(dx, dy) * 10 + dx + dy;
                if (d < bestD)
                {
                    bestD = d;
                    best = new Vector2Int(x, y);
                }
            }
        }
        if (!best.HasValue) return false;
        List<TimeStopMove> moves = ts.Moves.FindAll(m => m.Piece == p && !m.Undone);
        if (moves.Count > 0) UndoTimeStopMoves(ts, p, moves.GetRange(moves.Count - 1, 1), tl);
        MovePieceBack(p, best.Value.x, best.Value.y, tl);
        return true;
    }

    // 時間停止中の移動を取り消す（その移動の予定は飛ばされる。移動回数を数え直し、その移動で成っていたら元に戻す）
    void UndoTimeStopMoves(TimeStopState ts, GamePiece p, List<TimeStopMove> moves, SpecialContext tl)
    {
        foreach (TimeStopMove m in moves)
        {
            m.Undone = true;
            if (m.Counted) p.moveCount = Mathf.Max(0, p.moveCount - 1);
            if (p.isPromoted && ts.Pending.Exists(e => e.Kind == TimeStopKind.Promote && e.Done && e.Move == m))
            {
                p.isPromoted = false;
                tl.Schedule(() => { if (p != null) p.UpdateVisual(); });
            }
        }
        RefreshSpecialReady(p);
    }

    // 駒 p を (x, y) へ押し戻す（来た道を跳ね返る）
    void MovePieceBack(GamePiece p, int x, int y, SpecialContext tl)
    {
        Debug.Log($"({p.X}, {p.Y}) → ({x}, {y})：{TurnEndAction.SideName(p.player)}の{HandUIManager.GetJapaneseName(p.type)}が盾に押し戻された");
        gameBoard.UpdateBoardData(p.X, p.Y, x, y);
        p.X = x;
        p.Y = y;
        if (specialFx != null) specialFx.MovePiece(p, x, y, PieceMoveStyle.Dash, 0.25f, tl.Time);
        else p.MoveTo(GameHelper.CalcPanelLocation(x, y));
    }
}
