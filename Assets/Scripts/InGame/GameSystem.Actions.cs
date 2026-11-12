using System.Collections.Generic;
using UnityEngine;

// 行動（Action）の入口（Prototype の src/core/actions.js の planAction・applyAction と Game.dispatch）。GameSystem の続き（partial）。
//
//  ・盤のクリック・キー・ボタン（HandleTurnUpdate など）は、選び終わったら MatchAction を組み立てて Dispatch を呼ぶだけ。
//    実行（移動・打つ・必殺技・狙撃・魔法・錬成・停止世界の解除）はすべて Dispatch → ExecutePlan を通る
//  ・CanDispatch は「今その行動を実行できるか」。判定は、盤のクリック・ボタンで同じ行動を選べる条件と同じにしている
//    （手番・演出中でないか・オンライン対戦で使えるか・ルール）。駒や持ち駒を選んでいる途中かどうか（UI の状態）は見ない
//  ・オンライン対戦中の移動・打つは、Dispatch がサーバーへ送り、サーバーからの通知（OnServerMovePiece など）で
//    ExecuteRemote が実行する（サーバーが認めた行動なので、ここでは判定し直さない。今までと同じ）
//  ・実行した行動は ActionHistory に順に残す（棋譜の元。オンラインではサーバーの通知で実行した行動も残る）。
//    行動の処理（演出・手番の終わりの自動行動・手番交代まで）が終わったら、その時点の盤面のハッシュを hash に入れる
public partial class GameSystem
{
    // 実行した行動（実行した順。棋譜の元）。各行動の hash には、その行動の処理が終わった時点の盤面のハッシュが入る
    // （処理が終わるまでは ""。GameSystem.State.cs の UpdateActionHash）
    private readonly List<MatchAction> actionHistory = new List<MatchAction>();
    public IReadOnlyList<MatchAction> ActionHistory => actionHistory;

    // 行動を実行する直前（ActionHistory に足したあと）。CPU・棋譜・照合用
    public event System.Action<MatchAction> ActionExecuting;

    // Dispatch の判定の結果（Prototype の planAction が返す「計画」）。実行に必要なものを解決して持つ
    private class ActionPlan
    {
        public MatchAction Action;
        public PlayerType Owner;
        public GamePiece Piece;          // Move・Special・Snipe：行動する駒
        public SpecialMove Special;      // Special：技
        public Vector2Int? Target;       // Special・Snipe：対象のマス
        public SkillDefinition Skill;    // Magic：魔法
        public ForgeRecipe Recipe;       // Forge：レシピ
    }

    // 手番のプレイヤー（手番中のみ意味がある）
    PlayerType CurrentSide => IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

    // ------------------------------------------------------------
    //  判定
    // ------------------------------------------------------------

    // 今その行動を実行できるか
    public bool CanDispatch(MatchAction action)
    {
        return PlanAction(action, out _) != null;
    }

    // 今その行動を実行できるか。できなければ reason に理由が入る
    public bool CanDispatch(MatchAction action, out string reason)
    {
        return PlanAction(action, out reason) != null;
    }

    // 行動を実行する（オンライン対戦の移動・打つはサーバーへ送る）。実行できない行動なら何もせずに false
    public bool Dispatch(MatchAction action)
    {
        // CPU の手番には、人の入力（盤のクリック・キー・ボタン）からの行動は実行しない。
        // CanDispatch・LegalActions はこれを見ない（CPU が同じ判定で手を選べるように）
        if (IsCpuTurn && !dispatchingCpu) return false;
        ActionPlan plan = PlanAction(action, out _);
        if (plan == null) return false;

        if (IsOnlineMatch && SendToServer(plan)) return true;
        ExecutePlan(plan);
        return true;
    }

    // 行動を今の局面で実行できるか判定し、実行の計画を返す（できなければ null と理由）
    ActionPlan PlanAction(MatchAction a, out string reason)
    {
        reason = null;
        if (a == null)
        {
            reason = "行動がありません";
            return null;
        }
        if (!IsInTurn || match == null || gameBoard == null)
        {
            reason = "対局中の手番ではありません";
            return null;
        }
        if (IsBusy)
        {
            reason = "演出中です";
            return null;
        }
        PlayerType me = CurrentSide;
        if (a.owner >= 0 && a.owner != (int)me)
        {
            reason = "手番のプレイヤーの行動ではありません";
            return null;
        }
        if (IsOnlineMatch && me != NetworkManager.Instance.MyPlayerType)
        {
            reason = "相手の手番です";
            return null;
        }

        ActionPlan plan = new ActionPlan { Action = a, Owner = me };
        switch (a.type)
        {
            case MatchActionType.Move: reason = PlanMove(plan); break;
            case MatchActionType.Drop: reason = PlanDrop(plan); break;
            case MatchActionType.Special: reason = PlanSpecial(plan); break;
            case MatchActionType.Snipe: reason = PlanSnipe(plan); break;
            case MatchActionType.Magic: reason = PlanMagic(plan); break;
            case MatchActionType.Forge: reason = PlanForge(plan); break;
            case MatchActionType.ReleaseTimeStop: reason = ReleaseBlockReason(me); break;
            default: reason = "知らない行動です"; break;
        }
        return reason == null ? plan : null;
    }

    // 盤上の自分の駒（なければ null）
    GamePiece OwnPieceAt(MatchSquare sq, PlayerType me)
    {
        if (!sq.IsOnBoard) return null;
        GamePiece p = gameBoard.GetPieceAt(sq.x, sq.y);
        return p != null && p.player == me ? p : null;
    }

    // 移動（HandlePieceSelection で選べる駒・マスと同じ。成りは成りの確認と同じ選び方）
    string PlanMove(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        GamePiece p = OwnPieceAt(a.from, plan.Owner);
        if (p == null) return "動かす自分の駒がありません";
        if (p.isUndead) return "アンデッドは操作できません";
        if (IsFrozenInTimeStop(p)) return "消失・爆発の予定がある駒は動かせません";
        if (!a.to.IsOnBoard || !CanPieceMoveTo(p, a.to.x, a.to.y)) return "そこには移動できません";
        if (!IsPromoteChoiceAllowed(p, a.to.x, a.to.y, a.promote)) return a.promote ? "成れません" : "成らないと行き所がなくなります";
        plan.Piece = p;
        return null;
    }

    // その移動で、成る（promote）／成らないを選べるか（HandlePieceSelection の成りの判定と同じ）。
    //  成れない（成れる駒でない・敵陣に入らない・火薬のマスに入って吹き飛ぶ）… 成らないだけ
    //  行き所がなくなる（歩・香車の最奥・桂馬の奥 2 段）… 成るだけ
    //  それ以外 … どちらも選べる
    bool IsPromoteChoiceAllowed(GamePiece p, int toX, int toY, bool promote)
    {
        bool canChoose = CanPromote(p, p.X, toX) && !WillBeDestroyedEntering(p, toX, toY);
        if (!canChoose) return !promote;
        if (MustPromote(p, toX)) return promote;
        return true;
    }

    // 持ち駒を打つ（持ち駒にあり、打てるマス）
    string PlanDrop(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        Player owner = PlayerOf(plan.Owner);
        if (owner == null) return "プレイヤーがいません";
        int count;
        if (!owner.Hand.TryGetValue(a.piece, out count) || count <= 0) return "その駒は持ち駒にありません";
        if (!a.to.IsOnBoard || !CanDropPieceTo(plan.Owner, a.piece, a.to.x, a.to.y)) return "そこには配置できません";
        return null;
    }

    // 必殺技（CanBeginSpecial・BeginSpecial と同じ）
    string PlanSpecial(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        GamePiece p = OwnPieceAt(a.from, plan.Owner);
        if (p == null) return "必殺技を使う自分の駒がありません";
        string reason = SpecialBlockReason(p);
        if (reason != null) return reason;

        SpecialMove move = GetSpecialMove(p);
        SpecialContext probe = new SpecialContext(this, gameBoard, p, specialFx);
        if (!move.CanUse(probe)) return "今は効果の対象がありません";
        List<Vector2Int> targets = move.GetTargets(probe);
        if (targets == null)
        {
            if (a.TargetCount > 0) return "対象を選ばない技です";
            plan.Target = null;
        }
        else
        {
            if (a.TargetCount != 1 || !targets.Contains(a.Target)) return "そのマスは選べません";
            plan.Target = a.Target;
        }
        plan.Piece = p;
        plan.Special = move;
        return null;
    }

    // 駒 p で必殺技を使えない理由（使えるなら null。効果の対象があるかは見ない）
    string SpecialBlockReason(GamePiece p)
    {
        SpecialMove move = GetSpecialMove(p); // アンデッドは null
        if (move == null) return "必殺技がありません";
        if (!p.isSpecialReady) return "まだ必殺技を使えません";
        if (IsFrozenInTimeStop(p)) return "消失・爆発の予定がある駒は動かせません";
        if (IsOnlineMatch) return "オンライン対戦ではまだ使えません";
        return null;
    }

    // 狙撃（CanBeginSnipe・HandleSnipeTargetSelection と同じ）
    string PlanSnipe(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        GamePiece p = OwnPieceAt(a.from, plan.Owner);
        if (p == null || !PieceRules.IsSniper(p.type)) return "狙撃する自分の狙撃手がありません";
        string reason = SnipeBlockReason(p);
        if (reason != null) return reason;
        if (a.TargetCount != 1 || !GetSnipeTargets(p).Contains(a.Target)) return "そこは狙撃できません";
        plan.Piece = p;
        plan.Target = a.Target;
        return null;
    }

    // 狙撃手 p で狙撃できない理由（できるなら null。撃てる駒があるかは見ない）
    string SnipeBlockReason(GamePiece p)
    {
        if (p.isUndead) return "アンデッドは操作できません";
        if (IsFrozenInTimeStop(p)) return "消失・爆発の予定がある駒は動かせません";
        if (IsOnlineMatch) return "オンライン対戦ではまだ使えません";
        return null;
    }

    // 魔法（UseSkill・対象選択（HandleTargetSelection）と同じ）
    string PlanMagic(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        SkillDefinition skill = FindDeckSkill(plan.Owner, a.skill);
        if (skill == null) return "編成していない魔法です";
        string reason = SkillBlockReason(plan.Owner, skill);
        if (reason != null) return reason;

        SkillEffect effect = skill.effect;
        if (effect.TargetCount <= 0)
        {
            if (a.TargetCount > 0) return "対象を選ばない魔法です";
        }
        else
        {
            if (a.TargetCount != effect.TargetCount) return "対象の数が違います";
            if (a.orient < 0 || a.orient >= Mathf.Max(1, effect.OrientCount)) return "範囲の向きが違います";
            SkillContext context = new SkillContext(this, gameBoard, plan.Owner) { Orient = a.orient };
            foreach (MatchSquare t in a.targets)
            {
                if (!IsSkillTargetSelectable(effect, context, t)) return effect.UsesArea ? effect.BlockedText : "そのマスは選べません";
                context.Targets.Add(t);
            }
        }
        plan.Skill = skill;
        return null;
    }

    // 対象選択で、次の対象として pos を選べるか（HandleTargetSelection と同じ：範囲が赤くなく、IsValidTarget）。
    // context.Targets には選び終わった対象が入っていること
    static bool IsSkillTargetSelectable(SkillEffect effect, SkillContext context, Vector2Int pos)
    {
        if (!BoardCells.InBounds(pos.x, pos.y)) return false;
        if (IsSkillAreaBlocked(effect, context, pos)) return false;
        return effect.IsValidTarget(context, pos);
    }

    // カーソル pos の範囲が選べない（赤い）か。SkillEffect.GetPreview の IsBlocked と同じ判定（光らせるマスは作らない）
    static bool IsSkillAreaBlocked(SkillEffect effect, SkillContext context, Vector2Int pos)
    {
        List<Vector2Int> area = effect.GetAreaCells(context, pos);
        if (area == null || area.Count == 0) return false;
        List<Vector2Int> blockers = effect.GetAreaBlockers(context, pos, area);
        return blockers != null && blockers.Count > 0;
    }

    // owner の編成から Id の魔法を探す（なければ null）
    SkillDefinition FindDeckSkill(PlayerType owner, string id)
    {
        Player p = PlayerOf(owner);
        if (p == null || p.SkillDeck == null || p.SkillDeck.skills == null || string.IsNullOrEmpty(id)) return null;
        foreach (SkillDefinition skill in p.SkillDeck.skills)
        {
            if (skill != null && skill.Id == id) return skill;
        }
        return null;
    }

    // 錬成（CanForge のうち、ルールの分）
    string PlanForge(ActionPlan plan)
    {
        ForgeRecipe recipe = FindRecipe(plan.Action.piece);
        if (recipe == null) return "錬成できない駒です";
        string reason = ForgeBlockReason(plan.Owner, recipe);
        if (reason != null) return reason;
        plan.Recipe = recipe;
        return null;
    }

    // 駒の種類から錬成のレシピを探す（なければ null）
    ForgeRecipe FindRecipe(PieceType type)
    {
        foreach (ForgeRecipe r in ForgeRecipes)
        {
            if (r != null && r.pieceType == type) return r;
        }
        return null;
    }

    // owner が recipe を錬成できない理由（できるなら null）
    string ForgeBlockReason(PlayerType owner, ForgeRecipe recipe)
    {
        if (IsOnlineMatch) return "オンライン対戦ではまだ使えません";
        Player p = PlayerOf(owner);
        if (p == null) return "プレイヤーがいません";
        if (!p.HasEnoughPoint(recipe.cost)) return $"エナジー不足：あと {recipe.cost - p.Energy}";
        return null;
    }

    // 停止世界を解除できない理由（できるなら null。CanReleaseTimeStop のうち、ルールの分）
    string ReleaseBlockReason(PlayerType me)
    {
        if (timeStop == null) return "時間停止中ではありません";
        if (timeStop.JustStarted || timeStop.Ending) return "今は解除できません";
        if (timeStop.Owner != me) return "時間停止を発動したプレイヤーではありません";
        return null;
    }

    // ------------------------------------------------------------
    //  実行
    // ------------------------------------------------------------

    // オンライン対戦中：サーバーに送る行動なら送って true（実行はサーバーからの通知で ExecuteRemote が行う）。
    // 移動・打つ以外（手番を使わない魔法など）は false（今までどおりこの端末で実行する）
    bool SendToServer(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        switch (a.type)
        {
            case MatchActionType.Move:
                NetworkManager.Instance.SendMoveRequest(a.from.x, a.from.y, a.to.x, a.to.y, a.promote);
                selectedPiece = null;
                gameBoard.AllDeactivePanel();
                ClearBoardHighlight();
                SetDarkOverlayActive(false);
                currentMovablePositions.Clear();
                return true;
            case MatchActionType.Drop:
                NetworkManager.Instance.SendDropRequest(a.piece, a.to.x, a.to.y);
                isDroppingHand = false;
                isSelectingHand = false;
                gameBoard.AllDeactivePanel();
                return true;
            default:
                return false;
        }
    }

    // 計画を実行する（Prototype の applyAction）
    void ExecutePlan(ActionPlan plan)
    {
        MatchAction a = plan.Action;
        BeginSimulationCheck(a); // エディタと Development Build：盤面のシミュレーションの予測を作っておき、行動が終わったら照合する
        RecordAction(a, plan.Owner);

        switch (a.type)
        {
            case MatchActionType.Move:
                ExecuteLocalMove(plan.Piece, a.to.x, a.to.y, a.promote);
                break;
            case MatchActionType.Drop:
                DropPiece(plan.Owner, a.piece, a.to.x, a.to.y);
                if (ResolveKingLoss()) return;
                EndActionTurn();
                break;
            case MatchActionType.Special:
                StartSpecial(plan.Piece, plan.Special, plan.Target);
                break;
            case MatchActionType.Snipe:
                ExecuteSnipe(plan.Piece, plan.Target.Value.x, plan.Target.Value.y);
                break;
            case MatchActionType.Magic:
            {
                SkillContext context = new SkillContext(this, gameBoard, plan.Owner) { Orient = a.orient };
                foreach (MatchSquare t in a.targets) context.Targets.Add(t);
                ExecuteSkill(plan.Skill, context);
                break;
            }
            case MatchActionType.Forge:
                ExecuteForge(plan.Recipe);
                break;
            case MatchActionType.ReleaseTimeStop:
                ExecuteReleaseTimeStop();
                break;
        }
    }

    // サーバーからの通知の行動を実行する（オンライン対戦の移動・打つ。判定はサーバーが済ませているので、ここでは見ない）
    void ExecuteRemote(MatchAction a, PlayerType activePlayer)
    {
        ActionPlan plan = new ActionPlan { Action = a, Owner = activePlayer };
        if (a.type == MatchActionType.Move)
        {
            plan.Piece = a.from.IsOnBoard ? gameBoard.GetPieceAt(a.from.x, a.from.y) : null;
            if (plan.Piece == null) return;
        }
        else if (a.type != MatchActionType.Drop)
        {
            Debug.LogWarning($"[Action] サーバーからの行動はまだ移動・打つだけです: {a}");
            return;
        }
        ExecutePlan(plan);
    }

    // ------------------------------------------------------------
    //  合法手（Prototype の legalActions）
    // ------------------------------------------------------------

    // player が今選べる行動をすべて返す。CanDispatch で実行できる行動と同じになるように、PlanAction と同じ判定から作る
    // （手番でない・演出中・オンライン対戦の相手の手番なら空）。形は盤のクリック・ボタンで作る行動と同じ（owner は -1 のまま）。
    // 並び：盤上の駒（盤の添字 GameHelper.CalcPanelNum の順）ごとに 移動（成らない → 成る）・狙撃・必殺技
    //       → 持ち駒（PieceRules.HandOrder の順）→ 魔法（編成の順。範囲の向き → 対象を選ぶ順）→ 錬成（レシピの順）→ 解除
    public List<MatchAction> LegalActions(PlayerType player)
    {
        List<MatchAction> res = new List<MatchAction>();
        if (!IsInTurn || match == null || gameBoard == null || IsBusy) return res;
        if (player != CurrentSide) return res;
        if (IsOnlineMatch && player != NetworkManager.Instance.MyPlayerType) return res;

        // 盤上の駒
        GamePiece[] board = gameBoard.GetBoardData();
        for (int i = 0; i < board.Length; i++)
        {
            GamePiece p = board[i];
            if (p == null || p.player != player) continue;
            AddPieceActions(res, p);
        }

        // 持ち駒
        Player owner = PlayerOf(player);
        if (owner != null)
        {
            foreach (PieceType type in PieceRules.HandOrder)
            {
                int count;
                if (!owner.Hand.TryGetValue(type, out count) || count <= 0) continue;
                for (int x = 0; x < BoardCells.Size; x++)
                {
                    for (int y = 0; y < BoardCells.Size; y++)
                    {
                        if (CanDropPieceTo(player, type, x, y)) res.Add(MatchAction.Drop(type, new Vector2Int(x, y)));
                    }
                }
            }
        }

        // 魔法（同じ Id の魔法は 1 回だけ。Dispatch は Id で編成から探すので、同じ行動になる）
        if (owner != null && owner.SkillDeck != null && owner.SkillDeck.skills != null)
        {
            HashSet<string> seen = new HashSet<string>();
            foreach (SkillDefinition skill in owner.SkillDeck.skills)
            {
                if (skill == null || !seen.Add(skill.Id)) continue;
                if (FindDeckSkill(player, skill.Id) != skill || SkillBlockReason(player, skill) != null) continue;
                AddMagicActions(res, player, skill);
            }
        }

        // 錬成（同じ駒のレシピは 1 つ目だけ。Dispatch は駒の種類でレシピを探す）
        foreach (ForgeRecipe recipe in ForgeRecipes)
        {
            if (recipe == null || FindRecipe(recipe.pieceType) != recipe) continue;
            if (ForgeBlockReason(player, recipe) == null) res.Add(MatchAction.Forge(recipe.pieceType));
        }

        // 停止世界の解除
        if (ReleaseBlockReason(player) == null) res.Add(MatchAction.ReleaseTimeStop());
        return res;
    }

    // 盤上の自分の駒 p の行動（移動・狙撃・必殺技）
    void AddPieceActions(List<MatchAction> res, GamePiece p)
    {
        Vector2Int from = new Vector2Int(p.X, p.Y);

        // 移動（PlanMove と同じ）
        if (!p.isUndead && !IsFrozenInTimeStop(p))
        {
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (!CanPieceMoveTo(p, x, y)) continue;
                    Vector2Int to = new Vector2Int(x, y);
                    if (IsPromoteChoiceAllowed(p, x, y, false)) res.Add(MatchAction.Move(from, to, false));
                    if (IsPromoteChoiceAllowed(p, x, y, true)) res.Add(MatchAction.Move(from, to, true));
                }
            }
        }

        // 狙撃（PlanSnipe と同じ）
        if (PieceRules.IsSniper(p.type) && SnipeBlockReason(p) == null)
        {
            foreach (Vector2Int t in GetSnipeTargets(p)) res.Add(MatchAction.Snipe(from, t));
        }

        // 必殺技（PlanSpecial と同じ）
        if (SpecialBlockReason(p) == null)
        {
            SpecialMove move = GetSpecialMove(p);
            SpecialContext probe = new SpecialContext(this, gameBoard, p, specialFx);
            if (move.CanUse(probe))
            {
                List<Vector2Int> targets = move.GetTargets(probe);
                if (targets == null) res.Add(MatchAction.Special(from));
                else
                {
                    foreach (Vector2Int t in targets) res.Add(MatchAction.Special(from, t));
                }
            }
        }
    }

    // 魔法 skill の行動（PlanMagic と同じ。対象を選ぶ魔法は、範囲の向きごとに、対象選択で選べる順に並べる）
    void AddMagicActions(List<MatchAction> res, PlayerType player, SkillDefinition skill)
    {
        SkillEffect effect = skill.effect;
        if (effect.TargetCount <= 0)
        {
            res.Add(MatchAction.Magic(skill.Id));
            return;
        }
        int orients = Mathf.Max(1, effect.OrientCount);
        for (int orient = 0; orient < orients; orient++)
        {
            SkillContext context = new SkillContext(this, gameBoard, player) { Orient = orient };
            AddMagicTargets(res, skill, context);
        }
    }

    // 対象を 1 つずつ選んでいく（context.Targets に選び終わった対象。TargetCount 個そろったら行動にする）
    void AddMagicTargets(List<MatchAction> res, SkillDefinition skill, SkillContext context)
    {
        SkillEffect effect = skill.effect;
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                Vector2Int pos = new Vector2Int(x, y);
                if (!IsSkillTargetSelectable(effect, context, pos)) continue;
                context.Targets.Add(pos);
                if (context.Targets.Count >= effect.TargetCount) res.Add(MatchAction.Magic(skill.Id, context.Targets, context.Orient));
                else AddMagicTargets(res, skill, context);
                context.Targets.RemoveAt(context.Targets.Count - 1);
            }
        }
    }

    void RecordAction(MatchAction a, PlayerType owner)
    {
        FinishActionHash(); // 前の行動のハッシュがまだなら、今の局面（前の行動の処理が終わった局面）で残す
        MatchAction record = a.Clone();
        record.Owner = owner; // 棋譜には行動したプレイヤーも残す
        record.hash = "";     // 処理が終わったら入れる（UpdateActionHash）
        actionHistory.Add(record);
        Debug.Log($"[Action] 第{match.Turn.MoveNumber}手 {record}  {record.ToJson()}");
        hashPendingAction = record;
        ActionExecuting?.Invoke(record);
    }
}
