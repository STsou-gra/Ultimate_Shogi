using System.Collections.Generic;

// 盤面のシミュレーションの続き：必殺技・魔法・手番交代の前の自動行動・超常魔法の解放・詰み（skillsKnown のスナップショットだけ）。
// GameObject の側の処理を、BoardSnapshot の上に移したもの。元のクラスを変えたら、ここも同じように変えること：
//  必殺技 … Special/SpecialKit.cs・Special/*Special.cs・SpecialContext（Step・Capture・Push）・GameSystem.ExecuteSpecial（移動回数・成り）
//  魔法   … Skill/SkillData/*Effect.cs・Skill/MagicKit.cs・GameSystem.SkillBlockReason・ExecuteSkill
//  自動行動 … Skill/PhalanxAdvance.cs・Skill/UndeadMarch.cs・Skill/WorldEndCollapse.cs・Skill/SkillData/WorldEndEffect.cs
//  解放・詰み … Match/TranscendentRules.cs・GameSystem.CheckTranscendentUnlock・IsCheckmated・Rules/GameRules.cs の CheckRules
// 乱数は GameSystem と同じ順に、スナップショットの GameRng（rngSeed・rngState）から引く（同じ状態から始めれば同じ結果になる）
public static partial class BoardSimulator
{
    static int Forward(PlayerType owner) => owner == PlayerType.Player1 ? 1 : -1;

    static GameRng RngOf(BoardSnapshot s) => new GameRng(s.rngSeed, s.rngState);

    // ------------------------------------------------------------
    //  必殺技
    // ------------------------------------------------------------

    static SpecialKind SpecialOf(BoardSnapshot s, SnapshotPiece p)
    {
        if (p == null || p.undead || s.specialKinds == null) return SpecialKind.None; // アンデッドは必殺技を使えない
        int i = (int)p.type;
        return i >= 0 && i < s.specialKinds.Length ? s.specialKinds[i] : SpecialKind.None;
    }

    // 駒 p で必殺技を使えない理由（GameSystem.SpecialBlockReason。使えるなら null。効果の対象があるかは見ない）
    static string SpecialBlockReason(BoardSnapshot s, SnapshotPiece p)
    {
        SpecialKind kind = SpecialOf(s, p);
        if (kind == SpecialKind.None) return "必殺技がありません";
        if (kind == SpecialKind.Unknown) return "シミュレーションできない必殺技です";
        if (!s.IsSpecialReady(p)) return "まだ必殺技を使えません";
        if (p.frozen) return "消失・爆発の予定がある駒は動かせません";
        if (s.online) return "オンライン対戦ではまだ使えません";
        return null;
    }

    // 対象を選ぶ技なら選べるマス、選ばない技なら null（SpecialMove.GetTargets）
    static List<MatchSquare> SpecialTargets(BoardSnapshot s, SnapshotPiece p)
    {
        if (SpecialOf(s, p) != SpecialKind.KnightLeap) return null;
        List<MatchSquare> result = new List<MatchSquare>();
        for (int dx = -2; dx <= 2; dx++)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                int x = p.x + dx, y = p.y + dy;
                if (CanLandOn(s, p, x, y)) result.Add(new MatchSquare(x, y));
            }
        }
        return result;
    }

    // SpecialMove.CanUse
    static bool SpecialCanUse(BoardSnapshot s, SnapshotPiece p)
    {
        switch (SpecialOf(s, p))
        {
            case SpecialKind.PawnCharge: return ChargePath(s, p, false).Count > 0;
            case SpecialKind.LancePierce: return ChargePath(s, p, true).Count > 0;
            case SpecialKind.KnightLeap: return SpecialTargets(s, p).Count > 0;
            case SpecialKind.GoldShock:
                foreach (Neighbor n in Neighbors(s, p))
                {
                    if (n.Piece.owner != p.owner) return true;
                }
                return false;
            case SpecialKind.BishopBeam: return AnyBeamHits(s, p, Diagonal);
            case SpecialKind.RookBeam: return AnyBeamHits(s, p, Orthogonal);
            default: return false;
        }
    }

    // 盤上の駒 p の必殺技の行動（GameSystem.AddPieceActions の必殺技の部分と同じ）
    static void AddSpecialActions(BoardSnapshot s, List<MatchAction> res, SnapshotPiece p)
    {
        if (SpecialBlockReason(s, p) != null || !SpecialCanUse(s, p)) return;
        MatchSquare from = new MatchSquare(p.x, p.y);
        List<MatchSquare> targets = SpecialTargets(s, p);
        if (targets == null)
        {
            res.Add(new MatchAction { type = MatchActionType.Special, from = from });
            return;
        }
        foreach (MatchSquare t in targets)
        {
            MatchAction a = new MatchAction { type = MatchActionType.Special, from = from };
            a.targets.Add(t);
            res.Add(a);
        }
    }

    // 必殺技を使えない理由（GameSystem.PlanSpecial）
    static string SpecialReason(BoardSnapshot s, MatchAction a, PlayerType me)
    {
        SnapshotPiece p = OwnPieceAt(s, a.from, me);
        if (p == null) return "必殺技を使う自分の駒がありません";
        string reason = SpecialBlockReason(s, p);
        if (reason != null) return reason;
        if (!SpecialCanUse(s, p)) return "今は効果の対象がありません";
        List<MatchSquare> targets = SpecialTargets(s, p);
        if (targets == null) return a.TargetCount > 0 ? "対象を選ばない技です" : null;
        if (a.TargetCount != 1 || !targets.Contains(a.Target)) return "そのマスは選べません";
        return null;
    }

    // 必殺技の効果（GameSystem.ExecuteSpecial）：効果 → 移動回数を 0 に → 敵陣に入る・出ると成る
    static void ApplySpecial(BoardSnapshot s, SnapshotPiece p, MatchSquare target)
    {
        int fromX = p.x;
        switch (SpecialOf(s, p))
        {
            case SpecialKind.PawnCharge: Charge(s, p, false); break;
            case SpecialKind.LancePierce: Charge(s, p, true); break;
            case SpecialKind.KnightLeap: Step(s, p, target.x, target.y); break;
            case SpecialKind.GoldShock:
                foreach (Neighbor n in Neighbors(s, p))
                {
                    if (n.Piece.owner == p.owner || !s.Contains(n.Piece)) continue;
                    Push(s, p.Owner, n.Piece, n.DX, n.DY);
                }
                break;
            case SpecialKind.BishopBeam: FireBeams(s, p, Diagonal); break;
            case SpecialKind.RookBeam: FireBeams(s, p, Orthogonal); break;
        }
        if (s.Contains(p))
        {
            p.moveCount = 0;
            if (CanPromote(p, fromX, p.x)) p.promoted = true;
        }
    }

    static readonly MatchSquare[] Orthogonal = { new MatchSquare(1, 0), new MatchSquare(-1, 0), new MatchSquare(0, 1), new MatchSquare(0, -1) };
    static readonly MatchSquare[] Diagonal = { new MatchSquare(1, 1), new MatchSquare(1, -1), new MatchSquare(-1, 1), new MatchSquare(-1, -1) };

    struct Neighbor
    {
        public SnapshotPiece Piece;
        public int DX, DY;
    }

    // 周囲 8 マスの駒（SpecialContext.Neighbors と同じ順）
    static List<Neighbor> Neighbors(BoardSnapshot s, SnapshotPiece p)
    {
        List<Neighbor> result = new List<Neighbor>();
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx == 0 && dy == 0) continue;
                SnapshotPiece q = s.At(p.x + dx, p.y + dy);
                if (q != null) result.Add(new Neighbor { Piece = q, DX = dx, DY = dy });
            }
        }
        return result;
    }

    // 駒 p が (x, y) に入れるか（SpecialContext.CanLandOn：破壊マス以外の 空き / 相手の駒 / 火薬）
    static bool CanLandOn(BoardSnapshot s, SnapshotPiece p, int x, int y)
    {
        if (!BoardCells.InBounds(x, y) || s.IsBroken(x, y)) return false;
        SnapshotPiece t = s.At(x, y);
        return t == null || t.owner != p.owner || PieceRules.IsExplosive(t.type);
    }

    // 前方への突進経路（SpecialKit.ChargePath）
    static List<MatchSquare> ChargePath(BoardSnapshot s, SnapshotPiece p, bool pierce)
    {
        List<MatchSquare> path = new List<MatchSquare>();
        int f = Forward(p.Owner);
        int y = p.y;
        for (int x = p.x + f; BoardCells.InBounds(x, y); x += f)
        {
            if (s.IsBroken(x, y)) break;
            SnapshotPiece q = s.At(x, y);
            if (q == null)
            {
                path.Add(new MatchSquare(x, y));
                if (s.IsHazard(x, y)) break;
                continue;
            }
            if (PieceRules.IsExplosive(q.type))
            {
                path.Add(new MatchSquare(x, y));
                break;
            }
            if (q.owner != p.owner)
            {
                path.Add(new MatchSquare(x, y));
                if (pierce) continue;
                break;
            }
            break;
        }
        return path;
    }

    // SpecialKit.Charge
    static void Charge(BoardSnapshot s, SnapshotPiece p, bool pierce)
    {
        foreach (MatchSquare sq in ChargePath(s, p, pierce))
        {
            if (Step(s, p, sq.x, sq.y) != true) return;
        }
    }

    struct Beam
    {
        public SnapshotPiece Hit;
        public MatchSquare Direction;
    }

    // SpecialKit.BeamPlan（当たる駒だけ）
    static List<Beam> BeamPlan(BoardSnapshot s, SnapshotPiece p, MatchSquare[] dirs)
    {
        List<Beam> plan = new List<Beam>();
        foreach (MatchSquare d in dirs)
        {
            Beam beam = new Beam { Direction = d };
            int x = p.x + d.x, y = p.y + d.y;
            while (BoardCells.InBounds(x, y))
            {
                if (s.IsBroken(x, y)) break;
                SnapshotPiece q = s.At(x, y);
                if (q != null)
                {
                    if (q.owner != p.owner || PieceRules.IsExplosive(q.type)) beam.Hit = q;
                    break;
                }
                x += d.x;
                y += d.y;
            }
            plan.Add(beam);
        }
        return plan;
    }

    static bool AnyBeamHits(BoardSnapshot s, SnapshotPiece p, MatchSquare[] dirs)
    {
        foreach (Beam b in BeamPlan(s, p, dirs))
        {
            if (b.Hit != null) return true;
        }
        return false;
    }

    // SpecialKit.FireBeams：各方向で最初に当たった相手の駒を取る（火薬なら爆発）
    static void FireBeams(BoardSnapshot s, SnapshotPiece p, MatchSquare[] dirs)
    {
        PlayerType owner = p.Owner;
        foreach (Beam b in BeamPlan(s, p, dirs))
        {
            if (b.Hit == null || !s.Contains(b.Hit)) continue;
            if (PieceRules.IsExplosive(b.Hit.type)) Explode(s, b.Hit.x, b.Hit.y, null);
            else Capture(s, b.Hit, owner);
        }
    }

    // SpecialContext.Step：駒 p を (tx, ty) へ動かす。入れたら true / 消失したら false / 取りが防がれて元のマスに残ったら null
    static bool? Step(BoardSnapshot s, SnapshotPiece p, int tx, int ty)
    {
        if (!s.Contains(p) || !BoardCells.InBounds(tx, ty)) return false;
        SnapshotPiece target = s.At(tx, ty);
        if (target != null && target != p)
        {
            if (PieceRules.IsExplosive(target.type))
            {
                Explode(s, tx, ty, p);
                if (!s.Contains(p)) return false;
                if (s.At(tx, ty) != null) return null;
                s.MovePiece(p, tx, ty);
                return EnterCell(s, p, tx, ty);
            }
            if (!Capture(s, target, p.Owner)) return null;
        }
        s.MovePiece(p, tx, ty);
        return EnterCell(s, p, tx, ty);
    }

    // SpecialContext.Push：駒 q を (dx, dy) へ 1 マス押し出す（盤外なら取り。by は押し出したプレイヤー）
    static void Push(BoardSnapshot s, PlayerType by, SnapshotPiece q, int dx, int dy)
    {
        if (!s.Contains(q)) return;
        int tx = q.x + dx, ty = q.y + dy;
        if (!BoardCells.InBounds(tx, ty))
        {
            Capture(s, q, by);
            return;
        }
        SnapshotPiece d = s.At(tx, ty);
        if (!s.IsBroken(tx, ty) && (d == null || PieceRules.IsExplosive(d.type))) Step(s, q, tx, ty);
    }

    // ------------------------------------------------------------
    //  魔法
    // ------------------------------------------------------------

    static SnapshotSkill FindSkill(BoardSnapshot s, PlayerType owner, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (SnapshotSkill k in s.Player(owner).skills)
        {
            if (k != null && k.id == id) return k;
        }
        return null;
    }

    // owner が編成した Id の魔法の種類（編成していなければ Unknown）
    public static MagicKind MagicKindOf(BoardSnapshot s, PlayerType owner, string id)
    {
        SnapshotSkill k = FindSkill(s, owner, id);
        return k != null ? k.kind : MagicKind.Unknown;
    }

    // GameSystem.SkillBlockReason（使えるなら null）
    static string SkillBlockReason(BoardSnapshot s, PlayerType owner, SnapshotSkill skill)
    {
        if (skill == null || skill.kind == MagicKind.Unknown) return "シミュレーションできない魔法です";
        SnapshotPlayer p = s.Player(owner);
        if (skill.transcendent)
        {
            if (p.transcendentUsed) return "使用済み：1対局に1回だけ";
            if (!p.transcendentUnlocked) return "未解放";
        }
        if (p.energy < skill.cost) return "エナジー不足";
        if (skill.endsTurn && s.online) return "オンライン対戦ではまだ使えません";
        if (s.timeStopped && skill.kind == MagicKind.DoubleTurn) return "時間停止中は使えません";
        if (!SkillCanUse(s, owner, skill)) return "今は効果の対象がありません";
        return null;
    }

    // SkillEffect.CanUse
    static bool SkillCanUse(BoardSnapshot s, PlayerType owner, SnapshotSkill skill)
    {
        switch (skill.kind)
        {
            case MagicKind.DoubleTurn:
            case MagicKind.TimeStop:
                return !s.timeStopped;
            case MagicKind.UndeadParadise:
                return RevivableFallen(s, owner).Count > 0 && OwnZoneEmptySquares(s, owner).Count > 0;
        }
        if (skill.TargetCount <= 0) return true;
        List<MatchSquare> none = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (IsValidTarget(s, owner, skill, none, x, y)) return true;
            }
        }
        return false;
    }

    // 対象選択で、次の対象として (x, y) を選べるか（GameSystem.IsSkillTargetSelectable：範囲が赤くなく、IsValidTarget）
    static bool IsTargetSelectable(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, List<MatchSquare> chosen, int x, int y)
    {
        if (!BoardCells.InBounds(x, y)) return false;
        if (IsAreaBlocked(s, owner, skill, x, y)) return false;
        return IsValidTarget(s, owner, skill, chosen, x, y);
    }

    // SkillEffect.IsValidTarget
    static bool IsValidTarget(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, List<MatchSquare> chosen, int x, int y)
    {
        SnapshotPiece p = s.At(x, y);
        switch (skill.kind)
        {
            case MagicKind.Shield: return IsOwnPiece(owner, p) && !p.shield;
            case MagicKind.Sword: return IsOwnPiece(owner, p) && FrontEnemies(s, p).Count > 0;
            case MagicKind.Pitfall: return IsEmptyNormal(s, x, y);
            case MagicKind.Swap: return !chosen.Contains(new MatchSquare(x, y)) && IsOwnPiece(owner, p);
            case MagicKind.Explosion: return true;
            case MagicKind.Phalanx: return IsOwnPiece(owner, p) && p.shield && !p.phalanx && p.type != PieceType.King;
            case MagicKind.Meteor:
            case MagicKind.Cannon:
            {
                // 範囲を選ぶ魔法（SkillEffect.IsAreaSelectable）：範囲が空でなく、選べない原因のマスがない
                List<MatchSquare> area = AreaCells(s, owner, skill, x, y);
                return area != null && area.Count > 0 && AreaBlockers(s, owner, skill, x, y, area).Count == 0;
            }
            default: return true;
        }
    }

    // カーソル (x, y) の範囲が選べない（赤い）か（GameSystem.IsSkillAreaBlocked）
    static bool IsAreaBlocked(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, int x, int y)
    {
        List<MatchSquare> area = AreaCells(s, owner, skill, x, y);
        if (area == null || area.Count == 0) return false;
        return AreaBlockers(s, owner, skill, x, y, area).Count > 0;
    }

    // SkillEffect.GetAreaCells（範囲を塗らない魔法は null）
    static List<MatchSquare> AreaCells(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, int x, int y)
    {
        switch (skill.kind)
        {
            case MagicKind.Sword:
            {
                SnapshotPiece p = s.At(x, y);
                return IsOwnPiece(owner, p) ? FrontSquares(s, p) : null;
            }
            case MagicKind.Explosion: return SquareArea(x, y, MatchConfig.ExplosionRadius);
            case MagicKind.Meteor: return SquareArea(x, y, 1);
            case MagicKind.Cannon: return Band(x, y, false);
            default: return null;
        }
    }

    // SkillEffect.GetAreaBlockers
    static List<MatchSquare> AreaBlockers(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, int x, int y, List<MatchSquare> area)
    {
        List<MatchSquare> blockers = new List<MatchSquare>();
        switch (skill.kind)
        {
            case MagicKind.Sword:
            {
                SnapshotPiece p = s.At(x, y);
                if (!(p != null && FrontEnemies(s, p).Count > 0)) blockers.AddRange(area);
                break;
            }
            case MagicKind.Meteor:
            {
                bool centerOk = (BoardZones.IsOwnZone(owner, x) || BoardZones.ZoneOf(x) == BoardZone.Neutral) && !s.IsBroken(x, y);
                if (!centerOk) blockers.Add(new MatchSquare(x, y));
                blockers.AddRange(KingSquares(s, area));
                break;
            }
            case MagicKind.Cannon:
                blockers.AddRange(KingSquares(s, area));
                break;
        }
        return blockers;
    }

    // 魔法の行動（GameSystem.AddMagicActions と同じ並び：編成の順 → 範囲の向き → 対象を選ぶ順）
    static void AddMagicActions(BoardSnapshot s, List<MatchAction> res, PlayerType player)
    {
        HashSet<string> seen = new HashSet<string>();
        foreach (SnapshotSkill skill in s.Player(player).skills)
        {
            if (skill == null || !seen.Add(skill.id)) continue;
            if (SkillBlockReason(s, player, skill) != null) continue;
            if (skill.TargetCount <= 0)
            {
                res.Add(new MatchAction { type = MatchActionType.Magic, skill = skill.id });
                continue;
            }
            for (int orient = 0; orient < System.Math.Max(1, skill.OrientCount); orient++)
            {
                AddMagicTargets(s, res, player, skill, new List<MatchSquare>(), orient);
            }
        }
    }

    static void AddMagicTargets(BoardSnapshot s, List<MatchAction> res, PlayerType player, SnapshotSkill skill, List<MatchSquare> chosen, int orient)
    {
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (!IsTargetSelectable(s, player, skill, chosen, x, y)) continue;
                chosen.Add(new MatchSquare(x, y));
                if (chosen.Count >= skill.TargetCount)
                {
                    res.Add(new MatchAction { type = MatchActionType.Magic, skill = skill.id, targets = new List<MatchSquare>(chosen), orient = orient });
                }
                else
                {
                    AddMagicTargets(s, res, player, skill, chosen, orient);
                }
                chosen.RemoveAt(chosen.Count - 1);
            }
        }
    }

    // 魔法を使えない理由（GameSystem.PlanMagic）
    static string MagicReason(BoardSnapshot s, MatchAction a, PlayerType me)
    {
        SnapshotSkill skill = FindSkill(s, me, a.skill);
        if (skill == null) return "編成していない魔法です";
        string reason = SkillBlockReason(s, me, skill);
        if (reason != null) return reason;
        if (skill.TargetCount <= 0) return a.TargetCount > 0 ? "対象を選ばない魔法です" : null;
        if (a.TargetCount != skill.TargetCount) return "対象の数が違います";
        if (a.orient < 0 || a.orient >= System.Math.Max(1, skill.OrientCount)) return "範囲の向きが違います";
        List<MatchSquare> chosen = new List<MatchSquare>();
        foreach (MatchSquare t in a.targets)
        {
            if (!IsTargetSelectable(s, me, skill, chosen, t.x, t.y)) return "そのマスは選べません";
            chosen.Add(t);
        }
        return null;
    }

    // 魔法の効果（GameSystem.ExecuteSkill のコストの支払い・超常魔法の使用 → SkillEffect.Execute）
    static void ApplyMagic(BoardSnapshot s, PlayerType owner, SnapshotSkill skill, MatchAction a)
    {
        SnapshotPlayer sp = s.Player(owner);
        sp.energy -= skill.cost;
        if (skill.transcendent) sp.transcendentUsed = true;
        MatchSquare t0 = a.TargetCount > 0 ? a.targets[0] : MatchSquare.None;

        switch (skill.kind)
        {
            case MagicKind.Shield:
            {
                SnapshotPiece p = s.At(t0.x, t0.y);
                if (p != null) p.shield = true;
                break;
            }
            case MagicKind.Phalanx:
            {
                SnapshotPiece p = s.At(t0.x, t0.y);
                if (p != null) p.phalanx = true;
                break;
            }
            case MagicKind.Sword:
            {
                SnapshotPiece caster = s.At(t0.x, t0.y);
                if (caster == null) break;
                List<SnapshotPiece> inRange = new List<SnapshotPiece>();
                foreach (MatchSquare sq in FrontSquares(s, caster))
                {
                    SnapshotPiece q = s.At(sq.x, sq.y);
                    if (q != null) inRange.Add(q);
                }
                foreach (SnapshotPiece q in inRange)
                {
                    if (!s.Contains(q) || q.Owner == owner || PieceRules.IsExplosive(q.type)) continue;
                    Capture(s, q, owner);
                }
                foreach (SnapshotPiece q in inRange)
                {
                    if (s.Contains(q) && PieceRules.IsExplosive(q.type)) Explode(s, q.x, q.y, null);
                }
                break;
            }
            case MagicKind.Pitfall:
                SetTrap(s, t0.x, t0.y, owner);
                break;
            case MagicKind.DoubleTurn:
                s.extraTurn = true;
                break;
            case MagicKind.Swap:
            {
                SnapshotPiece pa = s.At(a.targets[0].x, a.targets[0].y);
                SnapshotPiece pb = s.At(a.targets[1].x, a.targets[1].y);
                if (pa != null) s.Remove(pa);
                if (pb != null) s.Remove(pb);
                if (pa != null) { pa.x = a.targets[1].x; pa.y = a.targets[1].y; s.Place(pa); }
                if (pb != null) { pb.x = a.targets[0].x; pb.y = a.targets[0].y; s.Place(pb); }
                break;
            }
            case MagicKind.Explosion:
                Explode(s, t0.x, t0.y, null);
                break;
            case MagicKind.ReplicateKing:
            {
                int kx = owner == PlayerType.Player1 ? 0 : 8, ky = 4;
                SnapshotCell cell = s.Cell(kx, ky);
                if (s.At(kx, ky) == null && cell != null && !cell.IsBroken && !cell.IsHazard)
                {
                    s.Place(new SnapshotPiece { type = PieceType.King, owner = (int)owner, x = kx, y = ky });
                }
                break;
            }
            case MagicKind.Meteor:
            {
                List<MatchSquare> area = SquareArea(t0.x, t0.y, 1);
                foreach (MatchSquare sq in area)
                {
                    SnapshotPiece q = s.At(sq.x, sq.y);
                    if (q != null) Vanish(s, q);
                }
                foreach (MatchSquare sq in area) SetBurning(s, sq.x, sq.y, MeteorBurnTurns);
                break;
            }
            case MagicKind.Cannon:
            {
                List<MatchSquare> cells = Band(t0.x, t0.y, false);
                List<SnapshotPiece> victims = new List<SnapshotPiece>();
                foreach (MatchSquare sq in cells)
                {
                    SnapshotPiece q = s.At(sq.x, sq.y);
                    if (q != null) victims.Add(q);
                }
                foreach (SnapshotPiece q in victims) Vanish(s, q);
                foreach (MatchSquare sq in cells) BreakCell(s, sq.x, sq.y);
                break;
            }
            case MagicKind.Cataclysm:
                ShuffleCells(s);
                break;
            case MagicKind.WorldEnd:
                if (sp.worldEndSince < 0) sp.worldEndSince = s.turnNumber;
                WorldEndCollapse(s, MatchConfig.WorldEndCells);
                break;
            case MagicKind.UndeadParadise:
            {
                List<PieceType> fallen = RevivableFallen(s, owner);
                GameRng rng = RngOf(s);
                List<MatchSquare> squares = rng.Shuffle(OwnZoneEmptySquares(s, owner));
                s.rngState = rng.State;
                int n = System.Math.Min(fallen.Count, squares.Count);
                sp.fallenInitial.Clear();
                for (int i = 0; i < n; i++)
                {
                    if (s.At(squares[i].x, squares[i].y) != null) continue;
                    s.Place(new SnapshotPiece { type = fallen[i], owner = (int)owner, x = squares[i].x, y = squares[i].y, undead = true });
                }
                break;
            }
            case MagicKind.TimeStop:
                s.timeStopped = true;
                s.timeStopOwner = (int)owner;
                s.timeStopRemaining = MatchConfig.TimeStopMoves;
                s.timeStopJustStarted = true;
                s.timeStopReleasable = false;
                break;
        }
    }

    // MeteorEffect.BurnTurns
    const int MeteorBurnTurns = 10;

    // MagicKit.IsOwnPiece：自分の駒（アンデッドは選べない）
    static bool IsOwnPiece(PlayerType owner, SnapshotPiece p)
    {
        return p != null && p.Owner == owner && !p.undead;
    }

    // MagicKit.IsEmptyNormal：駒がいない通常状態（破壊・炎上・落とし穴がない）のマス（時間停止中、予定の駒が残るマスは除く）
    static bool IsEmptyNormal(BoardSnapshot s, int x, int y)
    {
        if (!BoardCells.InBounds(x, y) || s.At(x, y) != null) return false;
        if (s.timeStopped && s.heldSquares.Contains(new MatchSquare(x, y))) return false;
        SnapshotCell c = s.Cell(x, y);
        return c.state == CellState.Normal && !c.HasTrap;
    }

    // MagicKit.OwnZoneEmptySquares（x → y の順）
    static List<MatchSquare> OwnZoneEmptySquares(BoardSnapshot s, PlayerType owner)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            if (!BoardZones.IsOwnZone(owner, x)) continue;
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (IsEmptyNormal(s, x, y)) result.Add(new MatchSquare(x, y));
            }
        }
        return result;
    }

    // MagicKit.RevivableFallen（王将以外。倒された順）
    static List<PieceType> RevivableFallen(BoardSnapshot s, PlayerType owner)
    {
        List<PieceType> result = new List<PieceType>();
        foreach (PieceType t in s.Player(owner).fallenInitial)
        {
            if (t != PieceType.King) result.Add(t);
        }
        return result;
    }

    // MagicKit.FrontSquares：前方 1×3（盤外・破壊マスは除く）
    static List<MatchSquare> FrontSquares(BoardSnapshot s, SnapshotPiece p)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        if (p == null) return result;
        int x = p.x + Forward(p.Owner);
        for (int y = p.y - 1; y <= p.y + 1; y++)
        {
            if (!BoardCells.InBounds(x, y) || s.IsBroken(x, y)) continue;
            result.Add(new MatchSquare(x, y));
        }
        return result;
    }

    // MagicKit.FrontEnemies
    static List<SnapshotPiece> FrontEnemies(BoardSnapshot s, SnapshotPiece p)
    {
        List<SnapshotPiece> result = new List<SnapshotPiece>();
        foreach (MatchSquare sq in FrontSquares(s, p))
        {
            SnapshotPiece q = s.At(sq.x, sq.y);
            if (q != null && q.owner != p.owner) result.Add(q);
        }
        return result;
    }

    // MagicKit.SquareArea
    static List<MatchSquare> SquareArea(int cx, int cy, int r)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                if (BoardCells.InBounds(x, y)) result.Add(new MatchSquare(x, y));
            }
        }
        return result;
    }

    // MagicKit.Band
    static List<MatchSquare> Band(int x, int y, bool horizontal)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        int mx = System.Math.Max(1, System.Math.Min(7, x));
        int my = System.Math.Max(1, System.Math.Min(7, y));
        for (int a = 0; a < 9; a++)
        {
            for (int b = -1; b <= 1; b++)
            {
                result.Add(horizontal ? new MatchSquare(mx + b, a) : new MatchSquare(a, my + b));
            }
        }
        return result;
    }

    // MagicKit.KingSquares
    static List<MatchSquare> KingSquares(BoardSnapshot s, List<MatchSquare> cells)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        foreach (MatchSquare c in cells)
        {
            SnapshotPiece q = s.At(c.x, c.y);
            if (q != null && q.type == PieceType.King) result.Add(c);
        }
        return result;
    }

    // ------------------------------------------------------------
    //  マスの状態（GameSystem.BreakCell・SetBurning・SetTrap と BoardCells）
    // ------------------------------------------------------------

    static void BreakCell(BoardSnapshot s, int x, int y)
    {
        SnapshotCell c = s.Cell(x, y);
        if (c == null || c.IsBroken) return;
        SnapshotPiece q = s.At(x, y);
        c.state = CellState.Broken;
        c.burnUntil = -1;
        c.trap = -1;
        if (q != null) Vanish(s, q);
    }

    static void SetBurning(BoardSnapshot s, int x, int y, int turns)
    {
        SnapshotCell c = s.Cell(x, y);
        if (c == null || c.IsBroken || turns <= 0) return;
        int until = s.turnNumber + turns - 1;
        c.burnUntil = c.IsBurning && c.burnUntil >= 0 ? System.Math.Max(c.burnUntil, until) : until;
        c.state = CellState.Burning;
    }

    static void SetTrap(BoardSnapshot s, int x, int y, PlayerType owner)
    {
        SnapshotCell c = s.Cell(x, y);
        if (c == null || s.At(x, y) != null || c.state != CellState.Normal || c.HasTrap) return;
        c.trap = (int)owner;
    }

    // 天変地異（GameSystem.ShuffleCells）：破壊されていないマスを、駒・マスの状態ごと入れ替える
    static void ShuffleCells(BoardSnapshot s)
    {
        List<MatchSquare> squares = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (!s.IsBroken(x, y)) squares.Add(new MatchSquare(x, y));
            }
        }
        GameRng rng = RngOf(s);
        List<MatchSquare> from = rng.Shuffle(squares);
        s.rngState = rng.State;

        // squares[i] には from[i] にあった駒・マスの状態が入る
        SnapshotPiece[] pieceAt = new SnapshotPiece[squares.Count];
        SnapshotCell[] cellAt = new SnapshotCell[squares.Count];
        for (int i = 0; i < squares.Count; i++)
        {
            pieceAt[i] = s.At(from[i].x, from[i].y);
            cellAt[i] = s.Cell(from[i].x, from[i].y);
        }
        foreach (SnapshotPiece p in pieceAt)
        {
            if (p != null) s.Remove(p);
        }
        for (int i = 0; i < squares.Count; i++)
        {
            s.cells[GameHelper.CalcPanelNum(squares[i].x, squares[i].y)] = cellAt[i];
            SnapshotPiece p = pieceAt[i];
            if (p == null) continue;
            p.x = squares[i].x;
            p.y = squares[i].y;
            s.Place(p);
        }
    }

    // 世界の終わりの崩壊 1 回分（WorldEndEffect.Collapse・GameSystem.PickWorldEndCells）
    static void WorldEndCollapse(BoardSnapshot s, int n)
    {
        List<MatchSquare> pool = WorldEndCandidates(s);
        if (pool.Count == 0 || n <= 0) return;
        GameRng rng = RngOf(s);
        List<MatchSquare> shuffled = rng.Shuffle(pool);
        s.rngState = rng.State;
        for (int i = 0; i < System.Math.Min(n, shuffled.Count); i++) BreakCell(s, shuffled[i].x, shuffled[i].y);
    }

    // GameSystem.WorldEndCandidates：破壊されていない、王将がいないマス
    static List<MatchSquare> WorldEndCandidates(BoardSnapshot s)
    {
        List<MatchSquare> pool = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (s.IsBroken(x, y)) continue;
                SnapshotPiece q = s.At(x, y);
                if (q != null && q.type == PieceType.King) continue;
                pool.Add(new MatchSquare(x, y));
            }
        }
        return pool;
    }

    // MatchState.WorldEndCellsThisRound
    static int WorldEndCellsThisRound(BoardSnapshot s)
    {
        int n = 0;
        foreach (SnapshotPlayer p in s.players)
        {
            if (p.worldEndSince >= 0 && p.worldEndSince < s.turnNumber) n += MatchConfig.WorldEndCells;
        }
        return n;
    }

    // ------------------------------------------------------------
    //  手番交代の前の自動行動（GameSystem.RunTurnEndActions：ファランクス → アンデッド → 世界の終わりの崩壊）
    // ------------------------------------------------------------

    // 自動行動を行う。王将がいなくなって終局したら true
    static bool RunTurnEndActions(BoardSnapshot s)
    {
        PlayerType side = s.Current;
        RunPhalanx(s, side);
        if (ResolveKingLoss(s)) return true;
        RunUndead(s, side);
        if (ResolveKingLoss(s)) return true;
        if (side == s.SecondPlayer && WorldEndCellsThisRound(s) > 0) WorldEndCollapse(s, WorldEndCellsThisRound(s));
        return ResolveKingLoss(s);
    }

    // PhalanxAdvance.Run
    static void RunPhalanx(BoardSnapshot s, PlayerType owner)
    {
        List<SnapshotPiece> list = new List<SnapshotPiece>();
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p != null && p.Owner == owner && p.phalanx) list.Add(p);
        }
        if (list.Count == 0) return;
        int f = Forward(owner);
        list.Sort((a, b) =>
        {
            int byRank = (b.x - a.x) * f;
            return byRank != 0 ? byRank : a.y - b.y;
        });
        foreach (SnapshotPiece p in list)
        {
            if (!s.Contains(p)) continue;
            PhalanxAct(s, p);
            if (s.KingCount(PlayerType.Player1) == 0 || s.KingCount(PlayerType.Player2) == 0) return;
        }
    }

    static void PhalanxAct(BoardSnapshot s, SnapshotPiece p)
    {
        int f = Forward(p.Owner);
        int fromX = p.x, tx = p.x + f, ty = p.y;
        SnapshotPiece q = BoardCells.InBounds(tx, ty) ? s.At(tx, ty) : null;
        if (BoardCells.InBounds(tx, ty) && !s.IsBroken(tx, ty) && (q == null || PieceRules.IsExplosive(q.type)))
        {
            if (Step(s, p, tx, ty) != true) return;
            if (CanPromote(p, fromX, p.x)) p.promoted = true;
        }
        // 前方 1×3 の相手の駒を取る（盾は有効。火薬は爆発）
        List<SnapshotPiece> targets = FrontEnemies(s, p);
        foreach (SnapshotPiece t in targets)
        {
            if (!s.Contains(t) || PieceRules.IsExplosive(t.type)) continue;
            Capture(s, t, p.Owner);
        }
        foreach (SnapshotPiece t in targets)
        {
            if (s.Contains(t) && PieceRules.IsExplosive(t.type)) Explode(s, t.x, t.y, null);
        }
    }

    // UndeadMarch.Run
    static void RunUndead(BoardSnapshot s, PlayerType owner)
    {
        PlayerType enemy = BoardZones.Opponent(owner);
        if (s.KingCount(enemy) == 0) return;
        List<SnapshotPiece> order = new List<SnapshotPiece>();
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p != null && p.Owner == owner && p.undead) order.Add(p);
        }
        if (order.Count == 0) return;
        // UndeadMarch.HasWork：動ける駒が 1 体でもいれば動く
        bool any = false;
        foreach (SnapshotPiece p in order)
        {
            if (UndeadMoves(s, p).Count > 0) { any = true; break; }
        }
        if (!any) return;

        List<int> dist = order.ConvertAll(p => KingDistance(s, p, enemy));
        List<int> index = new List<int>();
        for (int i = 0; i < order.Count; i++) index.Add(i);
        index.Sort((a, b) => dist[a] != dist[b] ? dist[a] - dist[b] : a - b);
        foreach (int i in index)
        {
            SnapshotPiece p = order[i];
            if (s.KingCount(enemy) == 0 || s.KingCount(owner) == 0) break;
            if (!s.Contains(p)) continue;
            MatchSquare? m = ChooseUndeadMove(s, p);
            if (!m.HasValue) continue;
            int fromX = p.x;
            if (Step(s, p, m.Value.x, m.Value.y) != true) continue;
            p.moveCount++;
            if (CanPromote(p, fromX, p.x)) p.promoted = true;
        }
    }

    // UndeadMarch.Moves：通常の動きのうち、炎上・落とし穴のマスを除く
    static List<MatchSquare> UndeadMoves(BoardSnapshot s, SnapshotPiece p)
    {
        List<MatchSquare> result = new List<MatchSquare>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (s.IsHazard(x, y)) continue;
                if (CanMoveTo(s, p, x, y)) result.Add(new MatchSquare(x, y));
            }
        }
        return result;
    }

    // UndeadMarch.Choose（乱数は対局の GameRng。同じ距離で同じ優先度の手が複数あれば引く）
    static MatchSquare? ChooseUndeadMove(BoardSnapshot s, SnapshotPiece p)
    {
        List<MatchSquare> moves = UndeadMoves(s, p);
        if (moves.Count == 0) return null;
        PlayerType enemy = BoardZones.Opponent(p.Owner);
        SnapshotPiece king = NearestKing(s, p, enemy);
        if (king == null) return null;
        foreach (MatchSquare m in moves)
        {
            SnapshotPiece q = s.At(m.x, m.y);
            if (q != null && q.Owner == enemy && q.type == PieceType.King) return m;
        }
        int best = int.MaxValue;
        foreach (MatchSquare m in moves) best = System.Math.Min(best, Chebyshev(m.x, m.y, king.x, king.y));
        List<MatchSquare> closest = moves.FindAll(m => Chebyshev(m.x, m.y, king.x, king.y) == best);
        List<MatchSquare> takes = closest.FindAll(m =>
        {
            SnapshotPiece q = s.At(m.x, m.y);
            return q != null && q.owner != p.owner && !PieceRules.IsExplosive(q.type);
        });
        List<MatchSquare> pool = takes.Count > 0 ? takes : closest;
        GameRng rng = RngOf(s);
        MatchSquare pick = rng.Pick(pool);
        s.rngState = rng.State;
        return pick;
    }

    static SnapshotPiece NearestKing(BoardSnapshot s, SnapshotPiece p, PlayerType enemy)
    {
        SnapshotPiece best = null;
        int bestD = int.MaxValue;
        foreach (SnapshotPiece k in s.Grid)
        {
            if (k == null || k.Owner != enemy || k.type != PieceType.King) continue;
            int d = Chebyshev(p.x, p.y, k.x, k.y);
            if (d < bestD)
            {
                bestD = d;
                best = k;
            }
        }
        return best;
    }

    static int KingDistance(BoardSnapshot s, SnapshotPiece p, PlayerType enemy)
    {
        SnapshotPiece k = NearestKing(s, p, enemy);
        return k != null ? Chebyshev(p.x, p.y, k.x, k.y) : int.MaxValue;
    }

    static int Chebyshev(int ax, int ay, int bx, int by)
    {
        return System.Math.Max(System.Math.Abs(ax - bx), System.Math.Abs(ay - by));
    }

    // ------------------------------------------------------------
    //  超常魔法の解放（GameSystem.CheckTranscendentUnlock・TranscendentRules.Progress）と詰み（GameSystem.IsCheckmated）
    // ------------------------------------------------------------

    // 両プレイヤー分：相手の駒が自陣にいて、盤上の駒数（火薬を除く）が相手のほうが多ければ解放（一度解放したらそのまま）
    public static void CheckTranscendentUnlock(BoardSnapshot s)
    {
        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            SnapshotPlayer sp = s.Player(owner);
            if (sp.transcendentUnlocked) continue;
            bool invaded = false;
            int mine = 0, theirs = 0;
            foreach (SnapshotPiece p in s.pieces)
            {
                if (p.Owner != owner && BoardZones.IsOwnZone(owner, p.x)) invaded = true;
                if (PieceRules.IsExplosive(p.type)) continue;
                if (p.Owner == owner) mine++;
                else theirs++;
            }
            if (invaded && theirs > mine) sp.transcendentUnlocked = true;
        }
    }

    // 王手を回避できる手（駒の移動・狙撃・持ち駒を打つ）が 1 つもないか（GameSystem.IsCheckmated。魔法・必殺技・錬成は含めない）
    public static bool IsCheckmated(BoardSnapshot s, PlayerType side)
    {
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p == null || p.Owner != side || p.undead) continue;
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (CanMoveTo(s, p, x, y) && SnapshotRules.IsMoveSafe(s, p, x, y)) return false;
                }
            }
        }
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p == null || p.Owner != side || !PieceRules.IsSniper(p.type)) continue;
            foreach (MatchSquare t in SnipeTargets(s, p))
            {
                if (SnapshotRules.IsSnipeSafe(s, p, t.x, t.y)) return false;
            }
        }
        foreach (PieceType type in PieceRules.HandOrder)
        {
            if (s.HandCount(side, type) <= 0) continue;
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (CanDropTo(s, side, type, x, y) && SnapshotRules.IsDropSafe(s, side, x, y)) return false;
                }
            }
        }
        return true;
    }
}
