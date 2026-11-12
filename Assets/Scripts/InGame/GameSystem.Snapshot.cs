using System.Collections.Generic;
using UnityEngine;

// 盤面のスナップショット（Match/BoardSnapshot.cs）を今の対局から作る。GameSystem の続き（partial）。
//
//  ・CaptureSnapshot：GameObject の盤面（GameBoard・Player・MatchState・時間停止）を、GameObject に依存しない形に写す。
//    必殺技・魔法の合法手は、この局面で LegalActions が出したものを入れておく（BoardSimulator は必殺技・魔法の効果を再現しないため）
//  ・シミュレーションの照合（エディタと Development Build のみ）：移動・打つ・狙撃・錬成を実行するたびに、
//    実行前のスナップショットに BoardSimulator で同じ行動を当てはめた「予測」と、行動が終わって次に操作できるようになったときの
//    実際の盤面を比べ、Console に一致／不一致を出す。自動行動（ファランクス・アンデッド・世界の終わり）が起きうる局面・時間停止中・
//    オンライン対戦では照合しない。照合で例外が起きても対局は止めない
public partial class GameSystem
{
    // false にするとシミュレーションの照合をやめる（エディタと Development Build でだけ動く）
    public static bool VerifySimulation = true;

    private BoardSnapshot simulationPrediction; // 照合待ちの予測
    private MatchAction simulationAction;       // 予測した行動

    bool IsSimulationCheckEnabled => VerifySimulation && (Application.isEditor || Debug.isDebugBuild);

    // 今の対局のスナップショットを作る。includeSkillActions なら必殺技・魔法の合法手も入れる（手番中で操作できるときだけ分かる）
    public BoardSnapshot CaptureSnapshot(bool includeSkillActions = true)
    {
        BoardSnapshot s = new BoardSnapshot();
        if (match == null || gameBoard == null) return s;

        s.firstPlayer = (int)match.Turn.FirstPlayer;
        s.current = (int)match.Turn.Current;
        s.turnNumber = match.Turn.TurnNumber;
        s.moveNumber = match.Turn.MoveNumber;
        s.extraTurn = hasExtraTurn;

        // 盤上の駒（時間停止の予定で盤のデータから外している駒は入れない。合法手の判定と同じく「ないもの」として扱う）
        GamePiece[] board = gameBoard.GetBoardData();
        for (int i = 0; i < board.Length; i++)
        {
            GamePiece p = board[i];
            if (p == null) continue;
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);
            s.Place(new SnapshotPiece
            {
                type = p.type,
                owner = (int)p.player,
                x = x,
                y = y,
                promoted = p.isPromoted,
                moveCount = p.moveCount,
                shield = p.hasShield,
                phalanx = p.hasPhalanx,
                undead = p.isUndead,
                initial = p.IsInitial,
                frozen = IsFrozenInTimeStop(p),
                pendingPromote = timeStop != null && timeStop.HasPendingPromote(p),
            });
        }

        // マスの状態
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                BoardCell c = Cells.At(x, y);
                SnapshotCell sc = s.Cell(x, y);
                sc.state = c.State;
                sc.burnUntil = c.IsBurning && c.BurnUntil.HasValue ? c.BurnUntil.Value : -1;
                sc.trap = c.Trap.HasValue ? (int)c.Trap.Value : -1;
            }
        }

        // プレイヤーごと（エナジー・持ち駒・倒された初期配置の駒・超常魔法）
        foreach (PlayerType side in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            SnapshotPlayer sp = s.Player(side);
            sp.energy = match.Energy.Get(side);
            Player p = PlayerOf(side);
            for (int h = 0; h < PieceRules.HandOrder.Length; h++)
            {
                int count = 0;
                if (p != null) p.Hand.TryGetValue(PieceRules.HandOrder[h], out count);
                sp.hand[h] = count;
            }
            foreach (FallenPieceRecord f in match.FallenInitial(side)) sp.fallenInitial.Add(f.Type);
            sp.transcendentUnlocked = match.IsTranscendentUnlocked(side);
            sp.transcendentUsed = match.IsTranscendentUsed(side);
            int? since = match.WorldEndSince(side);
            sp.worldEndSince = since.HasValue ? since.Value : -1;
        }

        // 対局の決まり
        for (int t = 0; t < BoardSnapshot.PieceTypeCount; t++)
        {
            SpecialMove move = SpecialMoves != null ? SpecialMoves.For((PieceType)t) : null;
            s.specialMoves[t] = move != null ? move.requiredMoves : 0;
        }
        foreach (ForgeRecipe r in ForgeRecipes)
        {
            if (r != null) s.recipes.Add(new SnapshotRecipe { piece = r.pieceType, cost = r.cost });
        }
        s.online = IsOnlineMatch;

        // 必殺技・魔法をシミュレーションするための種類（BoardSimulator.Skills.cs）と、対局の乱数の状態
        s.skillsKnown = true;
        for (int t = 0; t < BoardSnapshot.PieceTypeCount; t++)
        {
            SpecialMove move = SpecialMoves != null ? SpecialMoves.For((PieceType)t) : null;
            s.specialKinds[t] = SpecialKindOf(move);
        }
        foreach (PlayerType side in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            Player p = PlayerOf(side);
            if (p == null || p.SkillDeck == null || p.SkillDeck.skills == null) continue;
            foreach (SkillDefinition skill in p.SkillDeck.skills)
            {
                if (skill == null) continue;
                s.Player(side).skills.Add(new SnapshotSkill
                {
                    id = skill.Id,
                    kind = MagicKindOf(skill.effect),
                    cost = skill.cost,
                    transcendent = skill.rank == SkillRank.Transcendent,
                    endsTurn = skill.EndsTurn,
                });
            }
        }
        s.rngSeed = match.Rng.Seed;
        s.rngState = match.Rng.State;

        // 時間停止
        if (timeStop != null)
        {
            s.timeStopped = true;
            s.timeStopOwner = (int)timeStop.Owner;
            s.timeStopReleasable = !timeStop.JustStarted && !timeStop.Ending;
            s.timeStopRemaining = timeStop.Remaining;
            s.timeStopJustStarted = timeStop.JustStarted;
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (timeStop.IsHeldSquare(x, y)) s.heldSquares.Add(new MatchSquare(x, y));
                }
            }
        }

        // 勝敗
        if (currentState == GameState.GameOver)
        {
            s.gameOver = true;
            s.winner = isDraw ? BoardSnapshot.Draw : (int)winner;
        }

        // 必殺技・魔法の合法手（LegalActions が分かるときだけ）
        if (includeSkillActions && IsInTurn && !IsBusy && !(IsOnlineMatch && CurrentSide != NetworkManager.Instance.MyPlayerType))
        {
            s.skillActionsKnown = true;
            foreach (MatchAction a in LegalActions(CurrentSide))
            {
                if (a.type == MatchActionType.Special || a.type == MatchActionType.Magic) s.skillActions.Add(a);
            }
        }
        return s;
    }

    // 必殺技のクラスから、シミュレーションでの種類を決める（知らないクラスは Unknown。BoardSimulator.Skills.cs に足すこと）
    public static SpecialKind SpecialKindOf(SpecialMove move)
    {
        switch (move)
        {
            case null: return SpecialKind.None;
            case PawnChargeSpecial _: return SpecialKind.PawnCharge;
            case LancePierceSpecial _: return SpecialKind.LancePierce;
            case KnightLeapSpecial _: return SpecialKind.KnightLeap;
            case GoldShockSpecial _: return SpecialKind.GoldShock;
            case BishopBeamSpecial _: return SpecialKind.BishopBeam;
            case RookBeamSpecial _: return SpecialKind.RookBeam;
            default: return SpecialKind.Unknown;
        }
    }

    // 魔法の効果のクラスから、シミュレーションでの種類を決める（知らないクラスは Unknown：シミュレーションでは使えない魔法になる）
    public static MagicKind MagicKindOf(SkillEffect effect)
    {
        switch (effect)
        {
            case ShieldEffect _: return MagicKind.Shield;
            case SwordEffect _: return MagicKind.Sword;
            case PitfallEffect _: return MagicKind.Pitfall;
            case DoubleTurnEffect _: return MagicKind.DoubleTurn;
            case SwapPieceEffect _: return MagicKind.Swap;
            case ExplosionEffect _: return MagicKind.Explosion;
            case ReplicateKingEffect _: return MagicKind.ReplicateKing;
            case PhalanxEffect _: return MagicKind.Phalanx;
            case MeteorEffect _: return MagicKind.Meteor;
            case CannonEffect _: return MagicKind.Cannon;
            case CataclysmEffect _: return MagicKind.Cataclysm;
            case WorldEndEffect _: return MagicKind.WorldEnd;
            case UndeadParadiseEffect _: return MagicKind.UndeadParadise;
            case TimeStopEffect _: return MagicKind.TimeStop;
            default: return MagicKind.Unknown;
        }
    }

    // ------------------------------------------------------------
    //  シミュレーションの照合（エディタと Development Build のみ）
    // ------------------------------------------------------------

    // 行動を実行する直前（ExecutePlan から）：予測を作っておく
    void BeginSimulationCheck(MatchAction a)
    {
        if (!IsSimulationCheckEnabled) return;
        try
        {
            if (simulationPrediction != null) CompleteSimulationCheck(); // 前の行動の照合がまだなら先に済ませる（錬成を続けたときなど）
            if (IsOnlineMatch || timeStop != null || a.type == MatchActionType.ReleaseTimeStop) return;

            BoardSnapshot before = CaptureSnapshot(false);
            if (!BoardSimulator.CanSimulate(before, a.type) || BoardSimulator.HasUnsimulatedTurnEnd(before, CurrentSide)) return;

            BoardSnapshot predicted = before.Clone();
            if (!BoardSimulator.Apply(predicted, a, out string reason))
            {
                Debug.LogWarning($"[Snapshot] GameSystem では実行できる行動が、シミュレーションでは実行できない：{a}（{reason}）");
                return;
            }
            simulationPrediction = predicted;
            simulationAction = a.Clone();
        }
        catch (System.Exception e)
        {
            simulationPrediction = null;
            Debug.LogException(e);
        }
    }

    // 毎フレーム（Update の頭）：行動が終わって操作できるようになったら（手番が替わった・2回行動で続く・終局）照合する
    void UpdateSimulationCheck()
    {
        if (simulationPrediction == null || IsBusy) return;
        CompleteSimulationCheck();
    }

    void CompleteSimulationCheck()
    {
        BoardSnapshot predicted = simulationPrediction;
        MatchAction a = simulationAction;
        simulationPrediction = null;
        simulationAction = null;
        if (predicted == null) return;
        try
        {
            BoardSnapshot actual = CaptureSnapshot(false);
            string note = "";
            if (!predicted.gameOver && actual.gameOver)
            {
                // 詰み・千日手はシミュレーションしないので、勝敗は比べない
                actual.gameOver = false;
                actual.winner = -1;
                note = "（詰み・千日手で終局。勝敗はシミュレーションしないので比べていない）";
            }
            List<string> diff = predicted.Diff(actual);
            if (diff.Count == 0)
            {
                Debug.Log($"[Snapshot] シミュレーションの予測と一致：{a}{note}");
            }
            else
            {
                Debug.LogWarning($"[Snapshot] シミュレーションの予測と実際の盤面が違う（{diff.Count} 件。左が予測・右が実際）：{a}{note}\n" + string.Join("\n", diff));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }
}
