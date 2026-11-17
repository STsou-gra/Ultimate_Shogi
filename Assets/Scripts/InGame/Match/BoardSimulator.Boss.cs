using System.Collections.Generic;

// 盤面のシミュレーションの続き：討伐任務（ボス。BoardSnapshot.mode = "bossRaid"・boss・raid）。
// GameObject の側の GameSystem.Boss.cs を、BoardSnapshot の上に移したもの（Prototype の src/core/boss/ と、play.js・effects.js・turn.js・clock.js の
// ボスの分）。ボスの AI・攻撃・手下の動きは対局と同じ Match/BossEngine.cs を使う（ここは盤の読み書きだけ：SnapshotBossHost）。
// GameSystem.Boss.cs のダメージの当て方・勝敗・行動のあとの処理を変えたら、ここも同じように変えること。
//
//  ・後手の席はボス：行動できない（合法手は空）。時間のエナジーは先手だけ。盤上の駒の移動・必殺技ごとに +2 エナジー
//  ・ボスのマスへの移動は攻撃：ダメージを与えて元のマスへ跳ね返る（移動として数える）。走り駒・突進・光線はボスで止まる。持ち駒は打てない
//  ・必殺技・魔法は 1 回の効果につき 1 回だけボスに当たる（BeginBossEffect 〜 EndBossEffect）。爆発・ファランクスの槍・狙撃も当たる
//  ・勝敗：ボスの HP 0 → 討伐成功（先手の勝ち）／ 王将が倒れた・王将の被弾がボスの上限 → 討伐失敗（後手の勝ち）
//  ・毎 tick：手下 → ボスの AI（停止世界の間は進まない）
public static partial class BoardSimulator
{
    // ------------------------------------------------------------
    //  対局の準備（Prototype の setupRaid）
    // ------------------------------------------------------------

    // 討伐任務の局面にする：盤の駒を取り除いて編成を自陣に並べ（初期配置の駒）、ボスを置く。錬成できる駒は編成から決まる。
    // 超常魔法は最初から解放済み（先手だけ）。後手は魔法を持たない。編成が正しくなければ理由つきの ArgumentException
    public static void SetupRaid(BoardSnapshot s, BossDef def, RaidFormation formation)
    {
        List<string> bad = BossDefs.Validate(def);
        if (bad.Count > 0) throw new System.ArgumentException(string.Join(" / ", bad));
        List<string> errors = RaidRules.Validate(formation, def, out _);
        if (errors.Count > 0) throw new System.ArgumentException("編成が正しくありません：" + string.Join(" / ", errors));

        foreach (SnapshotPiece p in new List<SnapshotPiece>(s.pieces)) s.Remove(p);
        s.mode = GameModes.BossRaid.Id;
        s.ruleset = def.RulesetId;
        s.raid = new RaidState { active = true }; // 駒の番号（serial）は、ここから作った順に 1 から振る（編成 → ボスの席の初期配置）
        foreach (RaidPlacement f in formation.pieces)
        {
            s.Place(new SnapshotPiece { type = f.type, owner = (int)PlayerType.Player1, x = f.x, y = f.y, initial = true });
        }
        s.boss = BossState.Create(def);
        // ボスの席の初期配置の駒（大将軍の陣）。軍銃兵は置いたときに照準を選ぶ（対局の乱数）
        BossEngine<SnapshotPiece>.PlaceEnemies(new SnapshotBossHost(s), def);
        s.recipes.Clear();
        foreach (RaidForgeOption r in RaidRules.ForgeRecipes(formation)) s.recipes.Add(new SnapshotRecipe { piece = r.Type, cost = r.Cost });
        if (s.Mode.TranscendentUnlockedFromStart) s.players[(int)PlayerType.Player1].transcendentUnlocked = true;
        s.players[(int)PlayerType.Player2].skills.Clear();
    }

    // ------------------------------------------------------------
    //  盤の読み書き（BossEngine へ渡す）
    // ------------------------------------------------------------

    sealed class SnapshotBossHost : IBossHost<SnapshotPiece>
    {
        readonly BoardSnapshot s;

        public SnapshotBossHost(BoardSnapshot s)
        {
            this.s = s;
        }

        public BossState Boss => s.boss;
        public RaidState Raid => s.raid;
        public int Now => s.clockTime;
        public bool IsOver => s.gameOver;

        public double NextRandom()
        {
            GameRng rng = RngOf(s);
            double v = rng.Next();
            s.rngState = rng.State;
            return v;
        }

        public bool IsBlocked(int x, int y)
        {
            SnapshotCell c = s.Cell(x, y);
            return c != null && c.IsBlocked;
        }

        public bool IsHazard(int x, int y) => s.IsHazard(x, y);

        public bool IsPlainCell(int x, int y)
        {
            SnapshotCell c = s.Cell(x, y);
            return c != null && c.state == CellState.Normal && !c.HasTrap;
        }

        public SnapshotPiece PieceAt(int x, int y) => s.At(x, y);
        public bool IsAlive(SnapshotPiece p) => s.Contains(p);
        public PieceType TypeOf(SnapshotPiece p) => p.type;
        public PlayerType OwnerOf(SnapshotPiece p) => p.Owner;
        public int XOf(SnapshotPiece p) => p.x;
        public int YOf(SnapshotPiece p) => p.y;
        public bool HasShield(SnapshotPiece p) => p.shield;
        public bool IsUndead(SnapshotPiece p) => p.undead;
        public int HpOf(SnapshotPiece p) => p.hp;
        public int SerialOf(SnapshotPiece p) => p.serial;
        public MinionData GetMinion(SnapshotPiece p) => p.Minion;
        public void SetMinion(SnapshotPiece p, MinionData d) => p.Minion = d;

        public void Explode(SnapshotPiece bomb) => BoardSimulator.Explode(s, bomb.x, bomb.y, null);
        public void BreakShield(SnapshotPiece p) => p.shield = false;
        public void VanishUndead(SnapshotPiece p) => Vanish(s, p);
        public bool DamageGiant(SnapshotPiece p, string by) => HitGiant(p);

        public void Smash(SnapshotPiece p)
        {
            PlayerType owner = p.Owner;
            PieceType type = p.type;
            if (Kill(s, p)) AddEnergy(s, owner, PieceValues.Of(type));
        }

        public void Destroy(SnapshotPiece p) => Kill(s, p);

        public void Relocate(SnapshotPiece p, int x, int y)
        {
            s.MovePiece(p, x, y);
            EnterCell(s, p, x, y);
        }

        public SnapshotPiece SpawnMinion(PieceType type, int x, int y)
        {
            SnapshotPiece p = new SnapshotPiece { type = type, owner = (int)PlayerType.Player2, x = x, y = y };
            if (p.IsWallPiece) p.hp = WallRules.MaxHp;
            s.Place(p);
            return p;
        }

        public void Emit(BossEvent e)
        {
            if (s.bossEvents != null) s.bossEvents.Add(e);
        }
    }

    // ボスの AI・手下・攻撃を、盤面のスナップショットの上で動かす入口（テスト・照合用にも公開）
    public static IBossHost<SnapshotPiece> BossHost(BoardSnapshot s) => new SnapshotBossHost(s);

    // ボスの AI と手下を 1 tick 進める（Tick から。停止世界の間は呼ばれない）
    static void TickBoss(BoardSnapshot s)
    {
        if (!s.HasBoss || s.timeStopped) return;
        BossEngine<SnapshotPiece>.Tick(new SnapshotBossHost(s));
    }

    // ------------------------------------------------------------
    //  ボスへのダメージ
    // ------------------------------------------------------------

    // ボスにダメージを与える。与えたダメージを返す（時間停止中の近似では、予定にせずすぐ与える）
    static int DamageBoss(BoardSnapshot s, BossHit hit, MatchSquare at)
    {
        if (!s.HasBoss) return 0;
        return BossEngine<SnapshotPiece>.Damage(new SnapshotBossHost(s), hit, at);
    }

    // マスの一覧がボスに重なれば、1 回だけダメージを与える（爆発・ファランクスの槍。Prototype の hitBossCells）。与えたダメージを返す
    static int HitBossCells(BoardSnapshot s, List<MatchSquare> cells, BossHit hit)
    {
        if (!s.HasBoss) return 0;
        foreach (MatchSquare c in cells)
        {
            if (s.BossAt(c.x, c.y)) return DamageBoss(s, hit, c);
        }
        return 0;
    }

    // 必殺技・魔法の効果の始まり・終わり：この間の EffectHitBoss は、その技・魔法のダメージで 1 回だけ当たる（Prototype の ctx.source・ctx.bossHit）
    static void BeginBossEffect(BoardSnapshot s, BossHit hit)
    {
        s.bossEffectActive = true;
        s.bossEffectHit = hit;
        s.bossEffectDone = false;
    }

    static void EndBossEffect(BoardSnapshot s)
    {
        s.bossEffectActive = false;
        s.bossEffectDone = false;
    }

    // 今の必殺技・魔法の範囲 cells がボスに重なれば、この効果で 1 回だけダメージを与える（Prototype の ctx.hitBoss）。当てたら true
    static bool EffectHitBoss(BoardSnapshot s, List<MatchSquare> cells)
    {
        if (!s.bossEffectActive || s.bossEffectDone || !s.HasBoss) return false;
        if (HitBossCells(s, cells, s.bossEffectHit) <= 0) return false;
        s.bossEffectDone = true;
        return true;
    }

    // 駒 p の前方 1×3 マスにボスがいるか（MagicKit.FrontHasBoss）
    static bool FrontHasBoss(BoardSnapshot s, SnapshotPiece p)
    {
        if (!s.HasBoss || p == null) return false;
        foreach (MatchSquare c in FrontSquares(s, p))
        {
            if (s.BossAt(c.x, c.y)) return true;
        }
        return false;
    }

    // 周囲 8 マス（盤の外も含む）
    static List<MatchSquare> Around(SnapshotPiece p)
    {
        List<MatchSquare> res = new List<MatchSquare>(8);
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                if (dx != 0 || dy != 0) res.Add(new MatchSquare(p.x + dx, p.y + dy));
            }
        }
        return res;
    }

    static bool AroundHasBoss(BoardSnapshot s, SnapshotPiece p)
    {
        if (!s.HasBoss) return false;
        foreach (MatchSquare c in Around(p))
        {
            if (s.BossAt(c.x, c.y)) return true;
        }
        return false;
    }

    // ------------------------------------------------------------
    //  勝敗
    // ------------------------------------------------------------

    // 討伐任務の勝敗（RaidResults.Of）
    public static RaidOutcome RaidOutcomeOf(BoardSnapshot s)
    {
        int left = s.HasBoss ? BossEngine<SnapshotPiece>.RequiredMinionsLeft(new SnapshotBossHost(s)) : 0;
        return RaidResults.Of(s.boss, s.raid, s.KingCount(PlayerType.Player1) > 0, left);
    }

    // 討伐任務の勝敗が決まっていれば終局にする（先手の勝ち＝討伐成功 ／ 後手の勝ち＝討伐失敗）。終わったら true
    static bool ResolveRaid(BoardSnapshot s)
    {
        RaidOutcome o = RaidOutcomeOf(s);
        if (o == RaidOutcome.None) return false;
        s.gameOver = true;
        s.winner = (int)(o == RaidOutcome.Cleared ? PlayerType.Player1 : PlayerType.Player2);
        return true;
    }

    // 勝敗が決まる状態か（盤面は変えない。自動行動の途中でやめるかの判定。Prototype の checkGameOver）
    static bool IsDecided(BoardSnapshot s)
    {
        if (s.IsRaid) return RaidOutcomeOf(s) != RaidOutcome.None;
        return s.KingCount(PlayerType.Player1) == 0 || s.KingCount(PlayerType.Player2) == 0;
    }

    // ------------------------------------------------------------
    //  アンデッド（討伐任務：相手の王将の代わりにボスへ進む。Prototype の undeadGoals・runUndeadPhase）
    // ------------------------------------------------------------

    // owner のアンデッドが目指すマス：ボスの占めるマス（ボスが倒れたあとは、残っている相手の駒のマス）
    static List<MatchSquare> UndeadRaidGoals(BoardSnapshot s, PlayerType owner)
    {
        if (s.boss.hp > 0) return BossEngine<SnapshotPiece>.BossCells(s.boss);
        List<MatchSquare> res = new List<MatchSquare>();
        foreach (SnapshotPiece q in PiecesInBoardOrder(s))
        {
            if (q.Owner != owner) res.Add(new MatchSquare(q.x, q.y));
        }
        return res;
    }

    // 盤上の駒（Prototype の allPieces の順：段の大きい方から、各段は筋の小さい方から。巨人は 1 回だけ）
    static List<SnapshotPiece> PiecesInBoardOrder(BoardSnapshot s)
    {
        List<SnapshotPiece> res = new List<SnapshotPiece>();
        for (int x = BoardCells.Size - 1; x >= 0; x--)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                SnapshotPiece q = s.At(x, y);
                if (q != null && !res.Contains(q)) res.Add(q);
            }
        }
        return res;
    }

    static int GoalDistance(int x, int y, List<MatchSquare> goals)
    {
        int best = int.MaxValue;
        foreach (MatchSquare g in goals) best = System.Math.Min(best, Chebyshev(x, y, g.x, g.y));
        return best;
    }

    // 討伐任務のアンデッドの進軍（UndeadMarch.Run の討伐任務の分）：ボスに近い駒から順に 1 手ずつ。
    // ボスのマスに入る手（＝ボスへの攻撃）が最優先。なければいちばん近づく手（同じなら相手の駒を取る手 → 対局の乱数）
    static void RunUndeadRaid(BoardSnapshot s, PlayerType owner)
    {
        List<MatchSquare> goals = UndeadRaidGoals(s, owner);
        if (goals.Count == 0) return;
        List<SnapshotPiece> order = PiecesInBoardOrder(s).FindAll(p => p.Owner == owner && p.undead);
        if (order.Count == 0) return;
        if (!order.Exists(p => UndeadMoves(s, p).Count > 0)) return;

        List<int> dist = order.ConvertAll(p => GoalDistance(p.x, p.y, goals));
        List<int> index = new List<int>();
        for (int i = 0; i < order.Count; i++) index.Add(i);
        index.Sort((a, b) => dist[a] != dist[b] ? dist[a] - dist[b] : a - b);
        foreach (int i in index)
        {
            SnapshotPiece p = order[i];
            if (IsDecided(s)) break;
            if (!s.Contains(p)) continue;
            List<MatchSquare> now = UndeadRaidGoals(s, owner);
            List<MatchSquare> moves = UndeadMoves(s, p);
            if (moves.Count == 0 || now.Count == 0) continue;
            MatchSquare pick;
            int kill = moves.FindIndex(m => now.Contains(m));
            if (kill >= 0) pick = moves[kill];
            else
            {
                int best = int.MaxValue;
                foreach (MatchSquare m in moves) best = System.Math.Min(best, GoalDistance(m.x, m.y, now));
                List<MatchSquare> closest = moves.FindAll(m => GoalDistance(m.x, m.y, now) == best);
                List<MatchSquare> takes = closest.FindAll(m =>
                {
                    SnapshotPiece q = s.At(m.x, m.y);
                    return q != null && q.owner != p.owner && !BoardSnapshot.Detonates(p, q);
                });
                List<MatchSquare> pool = takes.Count > 0 ? takes : closest;
                GameRng rng = RngOf(s);
                pick = rng.Pick(pool);
                s.rngState = rng.State;
            }
            int fromX = p.x;
            bool boss = s.BossAt(pick.x, pick.y);
            SnapshotPiece at = s.At(pick.x, pick.y);
            bool wall = at != null && at.owner != p.owner && at.IsWallPiece; // 防壁への攻撃（跳ね返る）も移動として数える
            bool? res = Step(s, p, pick.x, pick.y, false);
            // ボス・防壁への攻撃（跳ね返る）も移動として数え、攻撃したマスが敵陣なら成る
            if (s.Contains(p) && (res == true || (res == null && (boss || wall))))
            {
                p.moveCount++;
                if (CanPromote(p, fromX, boss || wall ? pick.x : p.x)) p.promoted = true;
            }
            ResolveCounters(s);
        }
    }
}
