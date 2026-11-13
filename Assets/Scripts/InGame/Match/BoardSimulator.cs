using System.Collections.Generic;

// 盤面のスナップショット（BoardSnapshot）に行動（MatchAction）を当てはめるシミュレーション（演出なし。Prototype の applyTurn の一部）。
// GameObject に依存しないので、CPU の先読み・棋譜の再生・照合に使える。判定・処理は GameSystem と同じにしている：
//
//  合法手（LegalActions・CanApply）
//    移動 … BaseMoveRule（駒の動き・破壊マス・通り道の駒／破壊・炎上・落とし穴・自分の駒）と成りの選び方（IsPromoteChoiceAllowed）
//    打つ … 持ち駒を打つルール（空きマス・破壊／落とし穴・行き所のない駒・二歩・序盤の敵陣・打ち歩詰め・時間停止の予定のマス）
//    狙撃 … 王将以外の相手の駒／錬成 … エナジー／解除 … 時間停止を発動したプレイヤー
//    必殺技・魔法 … スナップショットを作った局面で GameSystem が出した合法手（skillActions）だけ。行動を当てはめたあとは分からない
//  当てはめる（Apply）：移動・打つ・狙撃・錬成
//    取り（取られた側が駒の価値の全額、取った側が半分のエナジー・持ち駒になる）、盾（取りを 1 回防いで割れる。移動した駒は元のマスに残る）、
//    アンデッド（取られると成仏＝消失）、火薬（入ると 3×3 が爆発。王将は残る）、マスの状態（炎上・落とし穴に入ると消失・落とし穴は消える）、
//    成り、移動回数（必殺技のチャージ）、倒された初期配置の駒の記録、王将が全部いなくなったら終局、
//    手番の交代（2回行動なら続く。後手の手番の終わりに期限の来た炎上を鎮火、手番の始まりにエナジー +1）
//
//
// スナップショットが skillsKnown（GameSystem.CaptureSnapshot が必殺技・魔法の種類を入れたもの）なら、さらに（BoardSimulator.Skills.cs）：
//    必殺技・魔法の合法手と効果（6 つの必殺技・14 の魔法）、手番交代の前の自動行動（ファランクス・アンデッド・世界の終わり）、
//    超常魔法の解放、手番の始まりの詰み。乱数（天変地異・世界の終わり・アンデッド）はスナップショットの rngSeed・rngState の GameRng
//
// シミュレーションしないこと：千日手（局面の記録がないので）。
// 時間停止中の行動は、Apply では当てはめない。CPU の先読み（ApplyForSearch）だけ、効果を予定にせずすぐ起こす近似で当てはめる
public static partial class BoardSimulator
{
    // 当てはめられる行動の種類か（必殺技・魔法・解除は skillsKnown のスナップショットだけ）
    public static bool CanSimulate(MatchActionType type)
    {
        return type == MatchActionType.Move || type == MatchActionType.Drop || type == MatchActionType.Snipe || type == MatchActionType.Forge;
    }

    public static bool CanSimulate(BoardSnapshot s, MatchActionType type)
    {
        return CanSimulate(type) || (s.skillsKnown && type != MatchActionType.ReleaseTimeStop);
    }

    // side の手番の終わりに、シミュレーションしない自動行動が起きるかもしれないか
    // （skillsKnown のスナップショットはファランクス・アンデッド・世界の終わりもシミュレーションするので false）
    public static bool HasUnsimulatedTurnEnd(BoardSnapshot s, PlayerType side)
    {
        if (s.skillsKnown) return false;
        foreach (SnapshotPiece p in s.pieces)
        {
            if (p.Owner == side && (p.phalanx || p.undead)) return true;
        }
        foreach (SnapshotPlayer p in s.players)
        {
            if (p.worldEndSince >= 0) return true;
        }
        return false;
    }

    // ------------------------------------------------------------
    //  合法手
    // ------------------------------------------------------------

    // player が選べる行動をすべて返す（GameSystem.LegalActions と同じ並び。手番でない・終局なら空）
    public static List<MatchAction> LegalActions(BoardSnapshot s, PlayerType player)
    {
        List<MatchAction> res = new List<MatchAction>();
        if (s.gameOver || player != s.Current) return res;

        // 盤上の駒（盤の添字の順）
        SnapshotPiece[] grid = s.Grid;
        for (int i = 0; i < grid.Length; i++)
        {
            SnapshotPiece p = grid[i];
            if (p == null || p.Owner != player) continue;
            MatchSquare from = new MatchSquare(p.x, p.y);

            if (!p.undead && !p.frozen)
            {
                for (int x = 0; x < BoardCells.Size; x++)
                {
                    for (int y = 0; y < BoardCells.Size; y++)
                    {
                        if (!CanMoveTo(s, p, x, y)) continue;
                        if (IsPromoteChoiceAllowed(s, p, x, y, false)) res.Add(Move(from, x, y, false));
                        if (IsPromoteChoiceAllowed(s, p, x, y, true)) res.Add(Move(from, x, y, true));
                    }
                }
            }
            if (PieceRules.IsSniper(p.type) && CanSnipeWith(s, p))
            {
                foreach (MatchSquare t in SnipeTargets(s, p)) res.Add(Snipe(from, t));
            }
            if (s.skillsKnown) AddSpecialActions(s, res, p);
            else if (s.skillActionsKnown)
            {
                foreach (MatchAction a in s.skillActions)
                {
                    if (a.type == MatchActionType.Special && a.from == from) res.Add(a.Clone());
                }
            }
        }

        // 持ち駒
        foreach (PieceType type in PieceRules.HandOrder)
        {
            if (s.HandCount(player, type) <= 0) continue;
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (CanDropTo(s, player, type, x, y)) res.Add(MatchAction.Drop(type, new UnityEngine.Vector2Int(x, y)));
                }
            }
        }

        // 魔法
        if (s.skillsKnown) AddMagicActions(s, res, player);
        else if (s.skillActionsKnown)
        {
            foreach (MatchAction a in s.skillActions)
            {
                if (a.type == MatchActionType.Magic) res.Add(a.Clone());
            }
        }

        // 錬成（同じ駒のレシピは 1 つ目だけ）
        HashSet<PieceType> seen = new HashSet<PieceType>();
        foreach (SnapshotRecipe r in s.recipes)
        {
            if (r == null || !seen.Add(r.piece)) continue;
            if (CanForge(s, player, r)) res.Add(MatchAction.Forge(r.piece));
        }

        // 停止世界の解除
        if (CanRelease(s, player)) res.Add(MatchAction.ReleaseTimeStop());
        return res;
    }

    static MatchAction Move(MatchSquare from, int x, int y, bool promote)
    {
        return new MatchAction { type = MatchActionType.Move, from = from, to = new MatchSquare(x, y), promote = promote };
    }

    static MatchAction Snipe(MatchSquare from, MatchSquare target)
    {
        MatchAction a = new MatchAction { type = MatchActionType.Snipe, from = from };
        a.targets.Add(target);
        return a;
    }

    // その行動を当てはめられる（合法な）か。必殺技・魔法は skillActions にあるか
    public static bool CanApply(BoardSnapshot s, MatchAction a)
    {
        return CanApply(s, a, out _);
    }

    public static bool CanApply(BoardSnapshot s, MatchAction a, out string reason)
    {
        reason = null;
        if (a == null) { reason = "行動がありません"; return false; }
        if (s.gameOver) { reason = "対局は終わっています"; return false; }
        PlayerType me = s.Current;
        if (a.owner >= 0 && a.owner != (int)me) { reason = "手番のプレイヤーの行動ではありません"; return false; }

        switch (a.type)
        {
            case MatchActionType.Move:
            {
                SnapshotPiece p = OwnPieceAt(s, a.from, me);
                if (p == null) reason = "動かす自分の駒がありません";
                else if (p.undead) reason = "アンデッドは操作できません";
                else if (p.frozen) reason = "消失・爆発の予定がある駒は動かせません";
                else if (!a.to.IsOnBoard || !CanMoveTo(s, p, a.to.x, a.to.y)) reason = "そこには移動できません";
                else if (!IsPromoteChoiceAllowed(s, p, a.to.x, a.to.y, a.promote)) reason = a.promote ? "成れません" : "成らないと行き所がなくなります";
                break;
            }
            case MatchActionType.Drop:
                if (s.HandCount(me, a.piece) <= 0) reason = "その駒は持ち駒にありません";
                else if (!a.to.IsOnBoard || !CanDropTo(s, me, a.piece, a.to.x, a.to.y)) reason = "そこには配置できません";
                break;
            case MatchActionType.Snipe:
            {
                SnapshotPiece p = OwnPieceAt(s, a.from, me);
                if (p == null || !PieceRules.IsSniper(p.type)) reason = "狙撃する自分の狙撃手がありません";
                else if (!CanSnipeWith(s, p)) reason = "今は狙撃できません";
                else if (a.TargetCount != 1 || !SnipeTargets(s, p).Contains(a.Target)) reason = "そこは狙撃できません";
                break;
            }
            case MatchActionType.Special:
                if (s.skillsKnown) reason = SpecialReason(s, a, me);
                else if (!s.skillActionsKnown) reason = "この局面の必殺技・魔法の合法手は分かりません（スナップショットを作った局面だけ）";
                else if (!ContainsKey(s.skillActions, a.Key)) reason = "今は使えません";
                break;
            case MatchActionType.Magic:
                if (s.skillsKnown) reason = MagicReason(s, a, me);
                else if (!s.skillActionsKnown) reason = "この局面の必殺技・魔法の合法手は分かりません（スナップショットを作った局面だけ）";
                else if (!ContainsKey(s.skillActions, a.Key)) reason = "今は使えません";
                break;
            case MatchActionType.Forge:
            {
                SnapshotRecipe r = FindRecipe(s, a.piece);
                if (r == null) reason = "錬成できない駒です";
                else if (!CanForge(s, me, r)) reason = s.online ? "オンライン対戦ではまだ使えません" : "エナジー不足";
                break;
            }
            case MatchActionType.ReleaseTimeStop:
                if (!CanRelease(s, me)) reason = "今は解除できません";
                break;
            default:
                reason = "知らない行動です";
                break;
        }
        return reason == null;
    }

    static bool ContainsKey(List<MatchAction> list, string key)
    {
        foreach (MatchAction a in list)
        {
            if (a.Key == key) return true;
        }
        return false;
    }

    static SnapshotPiece OwnPieceAt(BoardSnapshot s, MatchSquare sq, PlayerType me)
    {
        if (!sq.IsOnBoard) return null;
        SnapshotPiece p = s.At(sq.x, sq.y);
        return p != null && p.Owner == me ? p : null;
    }

    // 駒 p が (x, y) へ動けるか（BaseMoveRule と同じ）
    public static bool CanMoveTo(BoardSnapshot s, SnapshotPiece p, int x, int y)
    {
        if (!BoardCells.InBounds(x, y)) return false;
        // 駒の動き（GamePiece.CanMove：破壊マス・壁には行けない。翼状態の駒は破壊マスには行ける）
        if (s.IsBlockedFor(p, x, y)) return false;
        if (!PieceMoves.CanMoveByPattern(p.type, p.Owner, p.promoted, x - p.x, y - p.y)) return false;
        // 通り道（桂馬以外）：途中の駒・破壊マス・壁・炎上・落とし穴（翼の駒は壁以外と火薬を通り抜ける）
        if (p.type != PieceType.Knight && IsPathBlocked(s.Grid, s, p, p.x, p.y, x, y)) return false;
        // 移動先：空き・相手の駒・火薬（自分の火薬にも入れる。鳥は味方の火薬には入れない。PieceRules.CanLandOn）
        SnapshotPiece target = s.At(x, y);
        if (target == p) return false;
        return target == null || target.owner != p.owner || BoardSnapshot.Detonates(p, target);
    }

    // 走り駒の通り道（GameHelper.IsPathBlocked と同じ）：移動先の手前までに駒・破壊マス・炎上・落とし穴があれば通れない。
    // 駒は grid（SnapshotRules では動かしたあとの配列）、マスの状態は s から見る
    public static bool IsPathBlocked(SnapshotPiece[] grid, BoardSnapshot s, int fromX, int fromY, int toX, int toY)
    {
        return IsPathBlocked(grid, s, null, fromX, fromY, toX, toY);
    }

    // mover が翼状態なら、破壊・炎上・落とし穴のマスと火薬の上を通り抜ける（壁は通れない。GameHelper.IsPathBlocked の mover と同じ）
    public static bool IsPathBlocked(SnapshotPiece[] grid, BoardSnapshot s, SnapshotPiece mover, int fromX, int fromY, int toX, int toY)
    {
        bool wing = mover != null && mover.HasWing;
        int dx = toX - fromX;
        int dy = toY - fromY;
        if (System.Math.Abs(dx) <= 1 && System.Math.Abs(dy) <= 1) return false;

        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        int checkX = fromX + stepX;
        int checkY = fromY + stepY;
        int safetyCount = 0;
        while ((checkX != toX || checkY != toY) && safetyCount < 10)
        {
            safetyCount++;
            if (checkX < 0 || checkX > 8 || checkY < 0 || checkY > 8) break;
            int index = GameHelper.CalcPanelNum(checkX, checkY);
            SnapshotPiece q = index != -1 ? grid[index] : null;
            if (q != null && !(wing && PieceRules.IsExplosive(q.type))) return true;
            if (s.IsBlockedFor(mover, checkX, checkY) || s.IsHazardFor(mover, checkX, checkY)) return true;
            checkX += stepX;
            checkY += stepY;
        }
        return false;
    }

    // owner が持ち駒 type を (x, y) に打てるか（GameSystem の gameRules と CanDropPieceTo と同じ）
    public static bool CanDropTo(BoardSnapshot s, PlayerType owner, PieceType type, int x, int y)
    {
        if (!BoardCells.InBounds(x, y)) return false;
        // 時間停止中、予定の駒が残っているマス
        if (s.heldSquares.Contains(new MatchSquare(x, y))) return false;
        // EmptyCellDropRule
        if (s.At(x, y) != null) return false;
        // CellStateDropRule：破壊マス・落とし穴・壁には打てない（炎上には打てる。鳥は破壊マス・落とし穴にも打てる）
        SnapshotCell cell = s.Cell(x, y);
        if (s.IsBlockedForType(type, x, y)) return false;
        if (cell.HasTrap && !PieceRules.IsWingType(type)) return false;
        // NoLegalMoveDropRule：行き所のない駒（鳥は歩と同じ）
        if (type == PieceType.Pawn || type == PieceType.Lance || type == PieceType.Bird)
        {
            if (owner == PlayerType.Player1 && x == 8) return false;
            if (owner == PlayerType.Player2 && x == 0) return false;
        }
        else if (type == PieceType.Knight)
        {
            if (owner == PlayerType.Player1 && x >= 7) return false;
            if (owner == PlayerType.Player2 && x <= 1) return false;
        }
        // NifuDropRule：同じ筋に自分の歩（NifuDropRule と同じく、成った歩も数える）
        if (type == PieceType.Pawn)
        {
            for (int rank = 0; rank < BoardCells.Size; rank++)
            {
                SnapshotPiece q = s.At(rank, y);
                if (q != null && q.Owner == owner && q.type == PieceType.Pawn) return false;
            }
        }
        // EnemyZoneDropLockRule：序盤は敵陣に打てない
        if (s.turnNumber <= MatchConfig.EnemyZoneDropLockTurns && BoardZones.IsEnemyZone(owner, x)) return false;
        // UchifuzumeRule：打ち歩詰め
        if (type == PieceType.Pawn && SnapshotRules.IsUchifuzume(s, owner, x, y)) return false;
        return true;
    }

    // 成れるか（GameSystem.CanPromote と同じ）：成れる駒で、まだ成っていない・時間停止中の成りの予定がない・敵陣に入る／出る
    public static bool CanPromote(SnapshotPiece p, int fromX, int toX)
    {
        if (p.promoted || !PieceRules.CanPromote(p.type) || p.pendingPromote) return false;
        return BoardZones.IsEnemyZone(p.Owner, fromX) || BoardZones.IsEnemyZone(p.Owner, toX);
    }

    // 成らないと行き所がなくなるか（GameSystem.MustPromote と同じ）
    public static bool MustPromote(SnapshotPiece p, int toX)
    {
        if (p.type == PieceType.Pawn || p.type == PieceType.Lance)
        {
            return (p.Owner == PlayerType.Player1 && toX == 8) || (p.Owner == PlayerType.Player2 && toX == 0);
        }
        if (p.type == PieceType.Knight)
        {
            return (p.Owner == PlayerType.Player1 && toX >= 7) || (p.Owner == PlayerType.Player2 && toX <= 1);
        }
        return false;
    }

    // 駒 p が (x, y) に入ると、火薬の爆発で吹き飛ぶか
    public static bool WillBeDestroyedEntering(BoardSnapshot s, SnapshotPiece p, int x, int y)
    {
        SnapshotPiece target = s.At(x, y);
        return target != null && target != p && BoardSnapshot.Detonates(p, target) && PieceRules.IsDestroyedByExplosion(p.type);
    }

    // 成る（promote）／成らないを選べるか（GameSystem.IsPromoteChoiceAllowed と同じ）
    public static bool IsPromoteChoiceAllowed(BoardSnapshot s, SnapshotPiece p, int toX, int toY, bool promote)
    {
        bool canChoose = CanPromote(p, p.x, toX) && !WillBeDestroyedEntering(s, p, toX, toY);
        if (!canChoose) return !promote;
        if (MustPromote(p, toX)) return promote;
        return true;
    }

    // 狙撃手 p で狙撃できるか（撃てる駒があるかは見ない）
    static bool CanSnipeWith(BoardSnapshot s, SnapshotPiece p)
    {
        return !p.undead && !p.frozen && !s.online;
    }

    // 狙撃手 p が撃てるマス（GameSystem.GetSnipeTargets と同じ：王将以外の相手の駒。盤の x → y の順）
    public static List<MatchSquare> SnipeTargets(BoardSnapshot s, SnapshotPiece p)
    {
        List<MatchSquare> targets = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                SnapshotPiece q = s.At(x, y);
                if (q != null && q.owner != p.owner && q.type != PieceType.King) targets.Add(new MatchSquare(x, y));
            }
        }
        return targets;
    }

    static SnapshotRecipe FindRecipe(BoardSnapshot s, PieceType piece)
    {
        foreach (SnapshotRecipe r in s.recipes)
        {
            if (r != null && r.piece == piece) return r;
        }
        return null;
    }

    static bool CanForge(BoardSnapshot s, PlayerType owner, SnapshotRecipe r)
    {
        return !s.online && s.Player(owner).energy >= r.cost;
    }

    static bool CanRelease(BoardSnapshot s, PlayerType me)
    {
        return s.timeStopped && s.timeStopReleasable && s.timeStopOwner == (int)me;
    }

    // ------------------------------------------------------------
    //  当てはめる
    // ------------------------------------------------------------

    // 行動を当てはめる（s を書き換える）。当てはめたら true。合法でない・シミュレーションしない行動なら何もせずに false
    public static bool Apply(BoardSnapshot s, MatchAction a)
    {
        return Apply(s, a, out _);
    }

    public static bool Apply(BoardSnapshot s, MatchAction a, out string reason)
    {
        return Apply(s, a, false, out reason);
    }

    // CPU の先読み用：Apply と同じだが、時間停止中の行動も当てはめる（効果を予定にせず、その場で起こす近似。
    // 本当の対局では、取り・爆発・成りなどは解除したときにまとめて起こり、盾で押し戻されることもある）
    public static bool ApplyForSearch(BoardSnapshot s, MatchAction a)
    {
        return Apply(s, a, true, out _);
    }

    static bool Apply(BoardSnapshot s, MatchAction a, bool approximate, out string reason)
    {
        if (!CanApply(s, a, out reason)) return false;
        if (!CanSimulate(s, a.type) && !(approximate && a.type == MatchActionType.ReleaseTimeStop))
        {
            reason = "必殺技・魔法・停止世界の解除の効果はシミュレーションしません";
            return false;
        }
        if (s.timeStopped && !approximate)
        {
            reason = "時間停止中の行動はシミュレーションしません";
            return false;
        }

        PlayerType me = s.Current;
        switch (a.type)
        {
            case MatchActionType.Move:
                ApplyMove(s, s.At(a.from.x, a.from.y), a.to.x, a.to.y, a.promote);
                AfterAction(s, true);
                break;
            case MatchActionType.Drop:
                ApplyDrop(s, me, a.piece, a.to.x, a.to.y);
                AfterAction(s, true);
                break;
            case MatchActionType.Snipe:
                ApplySnipe(s, s.At(a.from.x, a.from.y), a.Target.x, a.Target.y);
                AfterAction(s, true);
                break;
            case MatchActionType.Special:
                ApplySpecial(s, s.At(a.from.x, a.from.y), a.TargetCount > 0 ? a.Target : MatchSquare.None);
                AfterAction(s, true);
                break;
            case MatchActionType.Magic:
            {
                SnapshotSkill skill = FindSkill(s, me, a.skill);
                ApplyMagic(s, me, skill, a);
                AfterAction(s, skill.endsTurn);
                break;
            }
            case MatchActionType.Forge:
            {
                SnapshotRecipe r = FindRecipe(s, a.piece);
                s.Player(me).energy -= r.cost;
                AddToHand(s, me, r.piece, true);
                if (MatchConfig.ForgeEndsTurn) AfterAction(s, true);
                break;
            }
            case MatchActionType.ReleaseTimeStop:
                EndTimeStop(s, true);
                break;
        }

        // 盤面・エナジーが変わったので、記録していた必殺技・魔法の合法手はもう使えない（skillsKnown なら作り直せる）
        s.skillActionsKnown = false;
        s.skillActions.Clear();
        return true;
    }

    // 移動（GameSystem.ApplyMove と同じ）
    static void ApplyMove(BoardSnapshot s, SnapshotPiece p, int x, int y, bool promote)
    {
        SnapshotPiece target = s.At(x, y);
        // 火薬のマスに入ったら爆発する（鳥は爆発させずに火薬を取る：下の取りの処理へ）
        if (target != null && target != p && BoardSnapshot.Detonates(p, target))
        {
            EnterGunpowder(s, p, x, y, promote);
            return;
        }
        if (target != null && target.owner != p.owner)
        {
            // 取りが防がれた（盾）：元のマスへ跳ね返る。移動回数は数えない（手番は使う）
            if (!Capture(s, target, p.Owner)) return;
        }
        s.MovePiece(p, x, y);
        if (!EnterCell(s, p, x, y)) return; // 炎上・落とし穴のマスに入って消失した（成りも起きない）
        if (promote) p.promoted = true;
        p.moveCount++;
    }

    // 火薬のマスに入る（GameSystem.EnterGunpowder と同じ）：爆発する。入った駒も吹き飛ぶ（王将は残ってそのマスへ進む）
    static void EnterGunpowder(BoardSnapshot s, SnapshotPiece p, int x, int y, bool promote)
    {
        Explode(s, x, y, p);
        if (s.Contains(p) && s.At(x, y) == null)
        {
            s.MovePiece(p, x, y);
            if (EnterCell(s, p, x, y))
            {
                if (promote) p.promoted = true;
                p.moveCount++;
            }
        }
    }

    // 打つ（GameSystem.DropPiece と同じ）
    static void ApplyDrop(BoardSnapshot s, PlayerType owner, PieceType type, int x, int y)
    {
        int i = BoardSnapshot.HandIndex(type);
        if (i >= 0 && s.Player(owner).hand[i] > 0) s.Player(owner).hand[i]--;
        SnapshotPiece p = new SnapshotPiece { type = type, owner = (int)owner, x = x, y = y };
        s.Place(p);
        EnterCell(s, p, x, y); // 炎上のマスに打ったら消失する
    }

    // 狙撃（GameSystem.ExecuteSnipe と同じ）：火薬なら爆発、それ以外は取り（盾なら防がれる）
    static void ApplySnipe(BoardSnapshot s, SnapshotPiece sniper, int x, int y)
    {
        SnapshotPiece target = s.At(x, y);
        if (target == null) return;
        if (PieceRules.IsExplosive(target.type)) Explode(s, x, y, null);
        else Capture(s, target, sniper.Owner);
    }

    // 取り（GameSystem.CapturePiece と同じ）。取れたら true（盾で防がれたら false）
    static bool Capture(BoardSnapshot s, SnapshotPiece target, PlayerType by)
    {
        bool guarded = target.shield && target.Owner != by;
        // アンデッドは取られると成仏する（持ち駒にもエナジーにもならない）
        if (target.undead && !guarded) return Vanish(s, target);
        // 盾：相手の取りを 1 回だけ防いで割れる
        if (guarded)
        {
            target.shield = false;
            return false;
        }
        AddEnergy(s, target.Owner, PieceValues.CapturedSideGain(target.type));
        AddEnergy(s, by, PieceValues.CapturerGain(target.type));
        Vanish(s, target);
        AddToHand(s, by, target.type); // 成った駒も元の駒として持ち駒になる（王将・火薬はならない）
        return true;
    }

    // 盤から取り除く（取り・消失の共通。初期配置の駒なら記録する）
    static bool Vanish(BoardSnapshot s, SnapshotPiece p)
    {
        if (!s.Contains(p)) return false;
        if (p.initial) s.Player(p.Owner).fallenInitial.Add(p.type);
        s.Remove(p);
        return true;
    }

    // 爆発（GameSystem.Explode と同じ）：中心の 3×3 の駒と、入った駒（entering）を消失させる。王将は残る
    static void Explode(BoardSnapshot s, int cx, int cy, SnapshotPiece entering)
    {
        List<SnapshotPiece> victims = new List<SnapshotPiece>();
        int r = MatchConfig.ExplosionRadius;
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                SnapshotPiece q = s.At(x, y);
                if (q != null) victims.Add(q);
            }
        }
        if (entering != null && !victims.Contains(entering)) victims.Add(entering);

        List<MatchSquare> chain = new List<MatchSquare>();
        foreach (SnapshotPiece q in victims)
        {
            if (!s.Contains(q) || !PieceRules.IsDestroyedByExplosion(q.type)) continue;
            bool isOtherBomb = PieceRules.IsExplosive(q.type) && (q.x != cx || q.y != cy);
            MatchSquare at = new MatchSquare(q.x, q.y);
            if (Vanish(s, q) && MatchConfig.ChainExplosion && isOtherBomb) chain.Add(at);
        }
        foreach (MatchSquare c in chain) Explode(s, c.x, c.y, null);
    }

    // 駒が (x, y) に入ったときのマスの効果（GameSystem.EnterCell と同じ）。そのマスに残れたら true
    static bool EnterCell(BoardSnapshot s, SnapshotPiece p, int x, int y)
    {
        SnapshotCell cell = s.Cell(x, y);
        if (cell == null) return true;
        if (p.HasWing) return true; // 翼状態の駒はマスの効果を受けない（落とし穴も残る）
        if (cell.HasTrap) cell.trap = -1; // 落とし穴：敵味方に関係なく落ちて、穴は消える
        else if (!cell.IsBurning && !cell.IsBroken) return true;
        Vanish(s, p);
        return s.Contains(p);
    }

    // 持ち駒に加える。取った駒は PieceRules.GoesToHand の駒だけ（GameSystem.AddPieceToHand）、
    // 錬成した駒（forged）は王将以外すべて（Player.AddPieceToHand。火薬も持ち駒になる）
    static void AddToHand(BoardSnapshot s, PlayerType owner, PieceType type, bool forged = false)
    {
        if (forged ? type == PieceType.King : !PieceRules.GoesToHand(type)) return;
        int i = BoardSnapshot.HandIndex(type);
        if (i >= 0) s.Player(owner).hand[i]++;
    }

    // エナジーを加える（GameSystem.AddPoint・EnergyState.Gain と同じ。上限は MatchConfig.EnergyMax）
    static void AddEnergy(BoardSnapshot s, PlayerType owner, int amount)
    {
        if (amount <= 0) return;
        SnapshotPlayer p = s.Player(owner);
        int after = p.energy + amount;
        if (MatchConfig.EnergyMax > 0 && after > MatchConfig.EnergyMax) after = System.Math.Max(p.energy, MatchConfig.EnergyMax);
        p.energy = after;
    }

    // 行動のあと（GameSystem の ResolveKingLoss → EndActionTurn → NextState。手番を使わない魔法は ExecuteSkill の続き）
    static void AfterAction(BoardSnapshot s, bool endsTurn)
    {
        if (ResolveKingLoss(s)) return;
        // 時間停止中（TimeStopAfterAction）：発動した行動は数えない。回数を使い切ったら時間を動かす
        if (s.timeStopped)
        {
            if (s.timeStopJustStarted)
            {
                s.timeStopJustStarted = false;
                s.timeStopReleasable = true;
                return;
            }
            s.timeStopRemaining--;
            if (s.timeStopRemaining <= 0) EndTimeStop(s, endsTurn);
            return;
        }
        if (!endsTurn)
        {
            if (s.skillsKnown) CheckTranscendentUnlock(s); // 手番を続ける行動のあとも解放を判定する
            return;
        }
        NextState(s);
    }

    // 時間停止を終える（GameSystem.ResolveTimeStop。予定はシミュレーションでは近似ですでに起こしてある）
    static void EndTimeStop(BoardSnapshot s, bool endsTurn)
    {
        s.timeStopped = false;
        s.timeStopOwner = -1;
        s.timeStopReleasable = false;
        s.timeStopJustStarted = false;
        s.timeStopRemaining = 0;
        s.heldSquares.Clear();
        foreach (SnapshotPiece p in s.pieces)
        {
            p.frozen = false;
            p.pendingPromote = false;
        }
        if (ResolveKingLoss(s)) return;
        if (endsTurn) NextState(s);
        else if (s.skillsKnown) CheckTranscendentUnlock(s);
    }

    // 手番を使う行動のあと（GameSystem.NextState → RunTurnEndActions → PassTurn → OnTurnStarted）
    static void NextState(BoardSnapshot s)
    {
        if (s.extraTurn)
        {
            s.extraTurn = false; // 2回行動：同じプレイヤーの手番が続く
            if (s.skillsKnown) CheckTranscendentUnlock(s);
            return;
        }
        // 手番交代の前の自動行動（ファランクス → アンデッド → 世界の終わりの崩壊）。skillsKnown のときだけ
        if (s.skillsKnown && RunTurnEndActions(s)) return;

        // 手番の終わり（TurnState.EndTurn）：後手の手番の終わりなら、期限の来た炎上を鎮火する
        if (s.Current == s.SecondPlayer) ExtinguishExpired(s, s.turnNumber);
        s.current = (int)BoardZones.Opponent(s.Current);
        s.moveNumber++;
        // 手番の始まり（TurnState.BeginTurn・OnTurnStarted）：先手の番ならターン数を進め、エナジー +1
        if (s.Current == s.FirstPlayer) s.turnNumber++;
        AddEnergy(s, s.Current, MatchConfig.PointsPerTurn);
        if (!s.skillsKnown) return; // 今までのシミュレーション（詰み・解放は見ない）

        // 詰み（王将が 1 枚のときのみ。GameSystem.IsCheckmated）。千日手は局面の記録がないので見ない
        if (SnapshotRules.IsInCheck(s, s.Current) && IsCheckmated(s, s.Current))
        {
            s.gameOver = true;
            s.winner = (int)BoardZones.Opponent(s.Current);
            return;
        }
        CheckTranscendentUnlock(s);
    }

    // 王将が全部いなくなったプレイヤーがいれば終局（両方なら引き分け）。終わったら true
    static bool ResolveKingLoss(BoardSnapshot s)
    {
        bool p1 = s.KingCount(PlayerType.Player1) > 0;
        bool p2 = s.KingCount(PlayerType.Player2) > 0;
        if (p1 && p2) return false;
        s.gameOver = true;
        s.winner = !p1 && !p2 ? BoardSnapshot.Draw : (int)(p1 ? PlayerType.Player1 : PlayerType.Player2);
        return true;
    }

    // BoardCells.ExtinguishExpired と同じ：炎上の期限（BurnUntil）が turnNumber 以下なら通常に戻す
    static void ExtinguishExpired(BoardSnapshot s, int turnNumber)
    {
        foreach (SnapshotCell c in s.cells)
        {
            if (!c.IsBurning || (c.burnUntil >= 0 && c.burnUntil > turnNumber)) continue;
            c.state = CellState.Normal;
            c.burnUntil = -1;
        }
    }
}
