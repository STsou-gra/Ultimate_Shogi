using System.Collections.Generic;

// ボスの AI と攻撃（Prototype の src/core/boss/ai.js・attack.js・effects.js・minions.js と src/defs/bossAttacks/*.js）。
// 対局（GameSystem.Boss.cs。駒は GamePiece）と盤面のシミュレーション（BoardSimulator.Boss.cs。駒は SnapshotPiece）の両方が、
// 同じこのエンジンを使う（ルールは 1 か所。盤の読み書きだけを IBossHost で受け持つ）。MonoBehaviour に依存しない。
//
//  AI の状態機械（BossState.ai。毎 tick、対局の時間で進める。乱数は対局の乱数）
//    cooldown（行動ペース）└ 期限 → 攻撃を選ぶ → 王将の方を向く → 範囲を確定 → telegraph
//    telegraph（予告）     ├ 一発の攻撃：期限 → 当てる → cooldown
//                          └ 続く行動（暴れ回り）：期限 → acting
//    acting（毎 tick）→ 期限 → resting（休憩。行動ペースを数えない）→ 期限 → cooldown
//  手下（小赤鬼）も、毎 tick ボスの AI の前に進める（現れた順）。停止世界の間・勝敗がついたあと・HP 0 のあとは進めない（呼ぶ側と Tick が見る）
//
// 座標：状態（BossState・MinionData・通知のマス）は GamePiece と同じ（x = 段、y = 筋）。
// ただし、マスを調べる順・並べる順は Prototype と同じにしている（乱数で選ぶ候補の並び・駒に当てる順が変わると、Prototype の棋譜と盤面がずれる）。
// Prototype のマス (px, py) は Unity の (8 − py, px)。Prototype の「上の段から、各段は左から」は「段の大きい方から、筋の小さい方から」
public interface IBossHost<TPiece> where TPiece : class
{
    BossState Boss { get; }
    RaidState Raid { get; }
    int Now { get; }                           // 対局の時間（tick）
    bool IsOver { get; }                       // 勝敗がついている
    double NextRandom();                       // 対局の乱数（0 以上 1 未満）

    // ---- 盤を読む
    bool IsBlocked(int x, int y);              // 入れないマス（破壊・壁）
    bool IsHazard(int x, int y);               // 入った駒が消失するマス（炎上・落とし穴）
    bool IsPlainCell(int x, int y);            // 通常状態で、落とし穴もないマス
    TPiece PieceAt(int x, int y);
    bool IsAlive(TPiece p);                    // 盤上にいるか
    PieceType TypeOf(TPiece p);
    PlayerType OwnerOf(TPiece p);
    int XOf(TPiece p);
    int YOf(TPiece p);
    bool HasShield(TPiece p);
    bool IsUndead(TPiece p);
    int HpOf(TPiece p);                        // 巨人の残りの HP
    MinionData GetMinion(TPiece p);
    void SetMinion(TPiece p, MinionData d);

    // ---- 盤を変える
    void Explode(TPiece bomb);                 // 火薬が爆発する（範囲がボスに重なればボスにも当たる）
    void BreakShield(TPiece p);                // 盾が割れる（駒は残る）
    void VanishUndead(TPiece p);               // アンデッドが成仏する
    bool DamageGiant(TPiece p, string by);     // 巨人の HP -1。0 になったら true
    void Smash(TPiece p);                      // 撃破：盤から消え、持ち主が駒の価値ぶんのエナジーを得る（誰の持ち駒にもならない）
    void Destroy(TPiece p);                    // 駒を盤から消す（押し出す先がないとき。エナジーなし）
    void Relocate(TPiece p, int x, int y);     // 駒を (x, y) へ動かし、入ったマスの効果を受けさせる（押し出し・手下の 1 歩。巨人は中央のマス）
    TPiece SpawnMinion(PieceType type, int x, int y);   // ボスの席（後手）の駒を (x, y) に置く

    // ---- 通知（演出）
    void Emit(BossEvent e);
}

public static class BossEngine<TPiece> where TPiece : class
{
    static int Ticks(float sec) => MatchPolicy.SecToTicks(sec);

    // 周り 8 方向（Prototype の DIRS = [-1,-1],[0,-1],[1,-1],[-1,0],[1,0],[-1,1],[0,1],[1,1] を Unity の（段, 筋）に直したもの。この順で候補を並べる）
    static readonly int[][] Dirs =
    {
        new[] { 1, -1 }, new[] { 1, 0 }, new[] { 1, 1 }, new[] { 0, -1 }, new[] { 0, 1 }, new[] { -1, -1 }, new[] { -1, 0 }, new[] { -1, 1 },
    };

    // ------------------------------------------------------------
    //  参照
    // ------------------------------------------------------------

    // ボスが (bx, by) にいるときに占めるマス（Prototype の bossCells の順：段の大きい方から、各段は筋の小さい方から）
    public static List<MatchSquare> BossCells(BossState b, int bx, int by)
    {
        List<MatchSquare> res = new List<MatchSquare>(b.w * b.h);
        for (int x = bx + b.h - 1; x >= bx; x--)
        {
            for (int y = by; y < by + b.w; y++) res.Add(new MatchSquare(x, y));
        }
        return res;
    }

    public static List<MatchSquare> BossCells(BossState b) => BossCells(b, b.x, b.y);

    // 盤の全マス（Prototype の「y = 0 から、各段は x = 0 から」の順）
    static IEnumerable<MatchSquare> BoardOrder(int margin = 0)
    {
        for (int x = BoardCells.Size - 1 - margin; x >= margin; x--)
        {
            for (int y = margin; y < BoardCells.Size - margin; y++) yield return new MatchSquare(x, y);
        }
    }

    // owner の王将（盤の順で最初に見つかったもの。いなければ null）
    public static TPiece FindKing(IBossHost<TPiece> h, PlayerType owner)
    {
        foreach (MatchSquare c in BoardOrder())
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q != null && h.TypeOf(q) == PieceType.King && h.OwnerOf(q) == owner) return q;
        }
        return null;
    }

    // マスの一覧にいる駒（巨人は何マス重なっても 1 つ。先に出たほうの順）
    static List<TPiece> PiecesIn(IBossHost<TPiece> h, List<MatchSquare> cells)
    {
        List<TPiece> res = new List<TPiece>();
        foreach (MatchSquare c in cells)
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q != null && !res.Contains(q)) res.Add(q);
        }
        return res;
    }

    static bool IsMinion(IBossHost<TPiece> h, TPiece p) => p != null && PieceRules.IsMinion(h.TypeOf(p));

    // 盤上の手下（現れた順）
    public static List<TPiece> Minions(IBossHost<TPiece> h)
    {
        List<TPiece> res = new List<TPiece>();
        foreach (MatchSquare c in BoardOrder())
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (IsMinion(h, q) && !res.Contains(q)) res.Add(q);
        }
        res.Sort((a, b) => h.GetMinion(a).Serial.CompareTo(h.GetMinion(b).Serial));
        return res;
    }

    static int Pick(IBossHost<TPiece> h, int count)
    {
        int i = (int)(h.NextRandom() * count);
        return i < count ? i : count - 1;
    }

    // ------------------------------------------------------------
    //  1 tick 進める（Prototype の tickBoss）
    // ------------------------------------------------------------

    public static void Tick(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        if (b == null || !b.active || h.IsOver || b.hp <= 0) return;
        TickMinions(h);
        if (b.hp <= 0 || h.IsOver) return;
        if (b.ai.phase == BossAiState.Acting) ActingTick(h);
        if (b.hp <= 0 || h.IsOver) return;
        if (h.Now >= b.ai.until) Expire(h);
    }

    // フェーズを変える
    static void Enter(IBossHost<TPiece> h, string phase, float sec)
    {
        BossAiState ai = h.Boss.ai;
        ai.phase = phase;
        ai.until = h.Now + Ticks(sec);
        h.Emit(new BossEvent { Type = BossEventType.Phase, Phase = phase, Until = ai.until, Attack = ai.attack });
    }

    static void ClearAttack(BossAiState ai, bool keepAttack)
    {
        if (!keepAttack) ai.attack = "";
        ai.hasCells = false;
        ai.cells = new List<MatchSquare>();
        ClearData(ai);
    }

    static void ClearData(BossAiState ai)
    {
        ai.hasData = false;
        ai.nextAt = -1;
        ai.hasNext = false;
        ai.next = MatchSquare.None;
    }

    // 期限になった
    static void Expire(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossAiState ai = b.ai;
        BossDef def = b.Def;
        switch (ai.phase)
        {
            case BossAiState.Cooldown:
            {
                string id = ChooseAttack(h);
                BossAttackDef atk = BossAttacks.ById(id);
                if (atk == null) { Enter(h, BossAiState.Cooldown, def != null ? def.CooldownSec : 5f); return; }
                FaceToward(h, FindKing(h, PlayerType.Player1));
                ClearData(ai);
                List<MatchSquare> cells = AttackCells(h, atk);
                ai.attack = id;
                ai.hasCells = cells != null;
                ai.cells = cells ?? new List<MatchSquare>();
                if (!(atk.TelegraphSec > 0f))
                {
                    Fire(h); // 予告なし：すぐ始める
                    return;
                }
                Enter(h, BossAiState.Telegraph, atk.TelegraphSec);
                h.Emit(new BossEvent
                {
                    Type = BossEventType.Telegraph, Attack = id, Cells = cells != null ? new List<MatchSquare>(cells) : null,
                    Sec = atk.TelegraphSec, Until = ai.until, Text = atk.TelegraphText,
                });
                return;
            }
            case BossAiState.Telegraph:
                h.Emit(new BossEvent { Type = BossEventType.TelegraphEnd });
                Fire(h);
                return;
            case BossAiState.Acting:
            {
                BossAttackDef atk = BossAttacks.ById(ai.attack);
                // 暴れ回りの終わり：次の 1 歩の表示を消す
                ClearData(ai);
                h.Emit(new BossEvent { Type = BossEventType.StepWarn, Cells = new List<MatchSquare>(), Sec = 0f });
                ai.last = ai.attack;
                if (atk != null && atk.RestSec > 0f)
                {
                    ClearAttack(ai, true); // 休憩の間は攻撃の Id を残す（Prototype と同じ）
                    Enter(h, BossAiState.Resting, atk.RestSec);
                }
                else
                {
                    ClearAttack(ai, false);
                    Enter(h, BossAiState.Cooldown, def.CooldownSec);
                }
                return;
            }
            default: // Resting
                ClearAttack(ai, false);
                Enter(h, BossAiState.Cooldown, def.CooldownSec);
                return;
        }
    }

    // 予告が終わった（予告のない攻撃は選んだ直後）：一発の攻撃は当てて cooldown へ、続く行動は acting へ
    static void Fire(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossAiState ai = b.ai;
        BossAttackDef atk = BossAttacks.ById(ai.attack);
        if (atk.IsLasting)
        {
            Enter(h, BossAiState.Acting, atk.DurationSec);
            PlanRampageStep(h, atk); // 続く行動の始め（暴れ回り：最初の 1 歩を決めて表示する）
            return;
        }
        ApplyAttack(h, atk, ai.cells ?? new List<MatchSquare>());
        ai.last = ai.attack;
        ClearAttack(ai, false);
        Enter(h, BossAiState.Cooldown, b.Def.CooldownSec);
    }

    // 次の攻撃を選ぶ：ボスの ChooseAttack があればそれ。なければ重み付きの抽選（NoRepeat の攻撃は前回と同じなら外す）
    public static string ChooseAttack(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossDef def = b.Def;
        if (def.ChooseAttack != null) return def.ChooseAttack(b, h.Now);
        List<BossAttackEntry> list = new List<BossAttackEntry>();
        foreach (BossAttackEntry a in def.Attacks)
        {
            if (!(a.NoRepeat && a.Id == b.ai.last)) list.Add(a);
        }
        if (list.Count == 0) list.AddRange(def.Attacks);
        if (list.Count == 0) return null;
        int total = 0;
        foreach (BossAttackEntry a in list) total += a.Weight;
        double r = h.NextRandom() * total;
        foreach (BossAttackEntry a in list)
        {
            r -= a.Weight;
            if (r < 0) return a.Id;
        }
        return list[list.Count - 1].Id;
    }

    // ボスを王将の方へ向ける：ボスの中心から王将への差の大きい軸（同じなら段の方向）の上下左右。王将がいなければプレイヤー側（Prototype の faceToward）
    public static void FaceToward(IBossHost<TPiece> h, TPiece king)
    {
        BossState b = h.Boss;
        if (king == null)
        {
            b.facingX = -1;
            b.facingY = 0;
            return;
        }
        FaceToward(b, h.XOf(king), h.YOf(king));
    }

    public static void FaceToward(BossState b, int kingX, int kingY)
    {
        // 中心との差を 2 倍して整数で比べる
        int dx = 2 * kingX - (2 * b.x + b.h - 1);   // 段の方向
        int dy = 2 * kingY - (2 * b.y + b.w - 1);   // 筋の方向
        if (System.Math.Abs(dx) >= System.Math.Abs(dy))
        {
            // Prototype は「段の差が 0 以上なら下（プレイヤー側）」：Unity の段は逆向きなので、dx <= 0 がプレイヤー側
            b.facingX = dx > 0 ? 1 : -1;
            b.facingY = 0;
        }
        else
        {
            b.facingX = 0;
            b.facingY = dy < 0 ? -1 : 1;
        }
    }

    // ------------------------------------------------------------
    //  攻撃の範囲（予告を始めるときに確定する。盤の外は切る）
    // ------------------------------------------------------------

    static bool In(int x, int y) => BoardCells.InBounds(x, y);

    // 向き (fx, fy) から見た 前・後ろ・右・左（Prototype の relativeDirs。下向きのボスの右手は盤の左）
    static int[][] RelativeDirs(BossState b)
    {
        int fx = b.facingX, fy = b.facingY;
        return new[]
        {
            new[] { fx, fy },      // 前
            new[] { -fx, -fy },    // 後ろ
            new[] { -fy, fx },     // 右（Prototype の { dx: -dy, dy: dx } を（段, 筋）に直したもの）
            new[] { fy, -fx },     // 左
        };
    }

    // ボスの dir の側に接する 1 列（ボスの辺の長さ ＋ 両端 1 マスずつ。3×3 のボスなら 5 マス）
    static List<MatchSquare> BandCells(BossState b, int[] dir)
    {
        List<MatchSquare> res = new List<MatchSquare>();
        if (dir[0] != 0)
        {
            // 段の方向の側：横の 1 列（筋の小さい方から）
            int x = dir[0] > 0 ? b.x + b.h : b.x - 1;
            for (int y = b.y - 1; y <= b.y + b.w; y++)
            {
                if (In(x, y)) res.Add(new MatchSquare(x, y));
            }
        }
        else
        {
            // 筋の方向の側：縦の 1 列（段の大きい方から）
            int y = dir[1] > 0 ? b.y + b.w : b.y - 1;
            for (int x = b.x + b.h; x >= b.x - 1; x--)
            {
                if (In(x, y)) res.Add(new MatchSquare(x, y));
            }
        }
        return res;
    }

    // 攻撃 atk の範囲（範囲を出さない攻撃は null）
    public static List<MatchSquare> AttackCells(IBossHost<TPiece> h, BossAttackDef atk)
    {
        BossState b = h.Boss;
        switch (atk.Id)
        {
            case "sweep":
            {
                // 側ごとの重み付きの抽選（正面・右・左・裏の順。範囲が 1 マスもない側は外す）
                int[][] dirs = RelativeDirs(b);
                int[] order = { 0, 2, 3, 1 }; // 正面 → 右 → 左 → 裏（Prototype の sides の並び）
                List<List<MatchSquare>> cells = new List<List<MatchSquare>>();
                List<int> weights = new List<int>();
                for (int i = 0; i < order.Length; i++)
                {
                    int w = BossAttacks.SweepSideWeights[i];
                    List<MatchSquare> band = BandCells(b, dirs[order[i]]);
                    if (band.Count == 0 || w <= 0) continue;
                    cells.Add(band);
                    weights.Add(w);
                }
                if (cells.Count == 0) return new List<MatchSquare>();
                int total = 0;
                foreach (int w in weights) total += w;
                double r = h.NextRandom() * total;
                for (int i = 0; i < cells.Count; i++)
                {
                    r -= weights[i];
                    if (r < 0) return cells[i];
                }
                return cells[cells.Count - 1];
            }
            case "smash":
            {
                List<MatchSquare> res = new List<MatchSquare>();
                for (int k = 0; k < BossAttacks.SmashDepth; k++)
                {
                    int x, y;
                    if (b.facingX != 0)
                    {
                        x = b.facingX > 0 ? b.x + b.h + k : b.x - 1 - k;
                        y = b.y + b.w / 2;
                    }
                    else
                    {
                        // Prototype の y = b.y + floor(h / 2)（上から数えた中央の段）。3×3 なら中央
                        x = b.x + b.h - 1 - b.h / 2;
                        y = b.facingY > 0 ? b.y + b.w + k : b.y - 1 - k;
                    }
                    if (In(x, y)) res.Add(new MatchSquare(x, y));
                }
                return res;
            }
            case "spin":
            {
                int[][] d = RelativeDirs(b);
                List<MatchSquare> res = new List<MatchSquare>();
                foreach (int i in new[] { 0, 1, 2, 3 }) // 正面 → 裏 → 右 → 左（角のマスは先に出たほうを残す）
                {
                    foreach (MatchSquare c in BandCells(b, d[i]))
                    {
                        if (!res.Contains(c)) res.Add(c);
                    }
                }
                return res;
            }
            case "summon":
            {
                List<MatchSquare> res = new List<MatchSquare>();
                foreach (MatchSquare c in BossAttacks.SummonSquares)
                {
                    if (!In(c.x, c.y) || h.IsBlocked(c.x, c.y) || b.Covers(c.x, c.y)) continue;
                    if (IsMinion(h, h.PieceAt(c.x, c.y))) continue;
                    res.Add(c);
                }
                return res;
            }
            default:
                return null; // 暴れ回り：範囲を出さない
        }
    }

    // 一発の攻撃を当てる
    static void ApplyAttack(IBossHost<TPiece> h, BossAttackDef atk, List<MatchSquare> cells)
    {
        HitCells(h, cells, atk.Id);
        if (atk.Id != "summon") return;

        // 小赤鬼号令：呼び出すマスにいた駒は攻撃を受けた。空いたマスに呼び出す（盾・王将で残ったマスには出さない。盤上は SummonMax 体まで）
        int room = BossAttacks.SummonMax;
        foreach (TPiece m in Minions(h))
        {
            if (h.TypeOf(m) == BossAttacks.SummonPiece) room--;
        }
        BossState b = h.Boss;
        foreach (MatchSquare c in cells)
        {
            if (room <= 0) break;
            if (h.PieceAt(c.x, c.y) != null || h.IsBlocked(c.x, c.y) || b.Covers(c.x, c.y)) continue;
            Spawn(h, BossAttacks.SummonPiece, c.x, c.y, atk.Id);
            room--;
        }
    }

    // マスの一覧にいる駒すべて（ボスの手下は除く）にボスの攻撃を当てる（Prototype の ctx.hitCells）。当たった駒の数
    public static int HitCells(IBossHost<TPiece> h, List<MatchSquare> cells, string attack)
    {
        List<TPiece> hits = new List<TPiece>();
        foreach (MatchSquare c in cells)
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q == null || IsMinion(h, q) || hits.Contains(q)) continue; // 巨人は何マス重なっても 1 回
            hits.Add(q);
            HitPiece(h, q, attack);
        }
        h.Emit(new BossEvent { Type = BossEventType.Attack, Attack = attack, Cells = new List<MatchSquare>(cells), Hits = hits.Count });
        return hits.Count;
    }

    // ------------------------------------------------------------
    //  ボスの攻撃が駒に当たった（Prototype の effects.js の hitPiece）
    // ------------------------------------------------------------

    // 火薬は爆発（ボスにも当たる）。盾があれば盾が割れて残る。王将は被弾の数が増える（Raid.kingHits）。巨人は HP -1。
    // それ以外は「撃破」：盤から消え、持ち主が駒の価値ぶんのエナジーを得る（誰の持ち駒にもならない）。アンデッドは成仏。
    // 戻り値：結果（BossEvent.Guarded など。盤上にいなければ null）
    public static string HitPiece(IBossHost<TPiece> h, TPiece q, string attack)
    {
        if (q == null || !h.IsAlive(q)) return null;
        PieceType type = h.TypeOf(q);
        RaidState raid = h.Raid;
        string result;
        if (PieceRules.IsExplosive(type)) result = BossEvent.Exploded;
        else if (h.HasShield(q)) result = BossEvent.Guarded;
        else if (PieceRules.IsGiant(type)) result = h.HpOf(q) > 1 ? BossEvent.Endured : BossEvent.Smashed;
        else if (type == PieceType.King && raid != null && raid.active) result = BossEvent.King;
        else result = BossEvent.Smashed;
        h.Emit(new BossEvent { Type = BossEventType.HitPiece, Piece = q, At = new MatchSquare(h.XOf(q), h.YOf(q)), Attack = attack, Result = result });

        if (result == BossEvent.Exploded) { h.Explode(q); return result; }
        if (result == BossEvent.Guarded) { h.BreakShield(q); return result; }
        if (result == BossEvent.King)
        {
            raid.kingHits++;
            BossDef def = h.Boss != null ? h.Boss.Def : null;
            h.Emit(new BossEvent { Type = BossEventType.KingHit, Piece = q, At = new MatchSquare(h.XOf(q), h.YOf(q)), Hits = raid.kingHits, Max = def != null ? def.KingMaxHits : 3 });
            return result;
        }
        if (h.IsUndead(q)) { h.VanishUndead(q); return result; }
        if (PieceRules.IsGiant(type))
        {
            if (h.DamageGiant(q, BossAttacks.NameOf(attack))) h.Smash(q); // 巨人：1 回の攻撃（HP が 0 になったら撃破）
            return result;
        }
        h.Smash(q);
        return result;
    }

    // ------------------------------------------------------------
    //  暴れ回り（続く行動）
    // ------------------------------------------------------------

    // ボスが (x, y) に立てるか：盤内で、破壊マス・壁のマスがない
    public static bool CanBossStand(IBossHost<TPiece> h, int x, int y)
    {
        BossState b = h.Boss;
        if (b == null || !b.active) return false;
        for (int cx = x; cx < x + b.h; cx++)
        {
            for (int cy = y; cy < y + b.w; cy++)
            {
                if (!In(cx, cy) || h.IsBlocked(cx, cy)) return false;
            }
        }
        return true;
    }

    // 次の 1 歩を決めて表示する（行ける方向がなければ、その場にとどまる）
    static void PlanRampageStep(IBossHost<TPiece> h, BossAttackDef atk)
    {
        BossState b = h.Boss;
        BossAiState ai = b.ai;
        List<int[]> ok = new List<int[]>();
        foreach (int[] d in Dirs)
        {
            if (CanBossStand(h, b.x + d[0], b.y + d[1])) ok.Add(d);
        }
        int[] dir = ok.Count > 0 ? ok[Pick(h, ok.Count)] : null;
        ai.hasData = true;
        ai.nextAt = h.Now + Ticks(atk.StepSec);
        ai.hasNext = dir != null;
        ai.next = dir != null ? new MatchSquare(b.x + dir[0], b.y + dir[1]) : MatchSquare.None;
        h.Emit(new BossEvent
        {
            Type = BossEventType.StepWarn, Sec = atk.StepSec, HasTo = ai.hasNext, To = ai.next,
            Cells = ai.hasNext ? BossCells(b, ai.next.x, ai.next.y) : new List<MatchSquare>(),
        });
    }

    // 続く行動の毎 tick（暴れ回り：時刻になったら 1 歩動き、次の 1 歩を決める）
    static void ActingTick(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossAiState ai = b.ai;
        BossAttackDef atk = BossAttacks.ById(ai.attack);
        if (atk == null || !ai.hasData || ai.nextAt < 0 || h.Now < ai.nextAt) return;
        if (ai.hasNext && CanBossStand(h, ai.next.x, ai.next.y)) MoveBoss(h, ai.next.x - b.x, ai.next.y - b.y, atk.Id);
        if (h.Now + Ticks(atk.StepSec) <= ai.until) PlanRampageStep(h, atk);
        else
        {
            // 最後の 1 歩のあとは動かない
            ai.hasData = true;
            ai.nextAt = -1;
            ai.hasNext = false;
            ai.next = MatchSquare.None;
        }
    }

    // ボスを (dx, dy) 動かす（Prototype の moveBoss）。新しい範囲にいる駒は踏まれる（攻撃を受けたのと同じ）。
    // 生き残った駒（盾・王将）とボスの手下は移動方向へ押し出す（塞がっていれば、いちばん近い空きマス）
    public static bool MoveBoss(IBossHost<TPiece> h, int dx, int dy, string attack)
    {
        BossState b = h.Boss;
        if (b == null || !b.active || (dx == 0 && dy == 0) || !CanBossStand(h, b.x + dx, b.y + dy)) return false;
        MatchSquare from = new MatchSquare(b.x, b.y);
        b.x += dx;
        b.y += dy;
        h.Emit(new BossEvent { Type = BossEventType.Step, From = from, To = new MatchSquare(b.x, b.y), Attack = attack });
        List<TPiece> trampled = PiecesIn(h, BossCells(b)); // 巨人は 1 回だけ踏まれる
        foreach (TPiece q in trampled)
        {
            if (!IsMinion(h, q)) HitPiece(h, q, attack); // 手下は踏まれず、押し出されるだけ
        }
        foreach (TPiece q in trampled)
        {
            if (h.IsAlive(q) && OverlapsBoss(h, q)) Shove(h, q, dx, dy);
        }
        return true;
    }

    static List<MatchSquare> Footprint(IBossHost<TPiece> h, TPiece q, int x, int y)
    {
        return PieceRules.IsGiant(h.TypeOf(q)) ? GiantRules.Cells(x, y) : new List<MatchSquare> { new MatchSquare(x, y) };
    }

    static bool OverlapsBoss(IBossHost<TPiece> h, TPiece q)
    {
        foreach (MatchSquare c in Footprint(h, q, h.XOf(q), h.YOf(q)))
        {
            if (h.Boss.Covers(c.x, c.y)) return true;
        }
        return false;
    }

    // 空きマスか（駒・ボス・破壊マス・壁がない）。safe なら炎上・落とし穴も避ける
    static bool IsFree(IBossHost<TPiece> h, int x, int y, bool safe)
    {
        if (!In(x, y) || h.IsBlocked(x, y) || h.PieceAt(x, y) != null || h.Boss.Covers(x, y)) return false;
        return !safe || h.IsPlainCell(x, y);
    }

    // 巨人 q が中央のマス (x, y) に立てるか：占めるマスが全部、空き（q 自身は除く）・ボスでない・破壊マス・壁のマスでない
    static bool GiantFits(IBossHost<TPiece> h, TPiece q, int x, int y, bool safe)
    {
        foreach (MatchSquare c in GiantRules.Cells(x, y))
        {
            if (!In(c.x, c.y) || h.IsBlocked(c.x, c.y) || h.Boss.Covers(c.x, c.y)) return false;
            TPiece o = h.PieceAt(c.x, c.y);
            if (o != null && o != q) return false;
            if (safe && !h.IsPlainCell(c.x, c.y)) return false;
        }
        return true;
    }

    static int ShoveDistance(int x, int y, int qx, int qy)
    {
        int ax = System.Math.Abs(x - qx), ay = System.Math.Abs(y - qy);
        return System.Math.Max(ax, ay) * 100 + ax + ay;
    }

    // 踏まれて生き残った駒 q を、ボスの外へ押し出す（移動方向 → なければいちばん近い空きマス。Prototype の shove・shoveGiant）
    public static void Shove(IBossHost<TPiece> h, TPiece q, int dx, int dy)
    {
        int qx = h.XOf(q), qy = h.YOf(q);
        bool giant = PieceRules.IsGiant(h.TypeOf(q));
        MatchSquare? to = null;
        if (giant)
        {
            for (int k = 1; k <= 4 && !to.HasValue; k++)
            {
                if (GiantFits(h, q, qx + dx * k, qy + dy * k, true)) to = new MatchSquare(qx + dx * k, qy + dy * k);
            }
        }
        else
        {
            for (int k = 1; k <= 3 && !to.HasValue; k++)
            {
                int x = qx + dx * k, y = qy + dy * k;
                if (!In(x, y)) break;
                if (h.Boss.Covers(x, y)) continue;
                if (IsFree(h, x, y, true)) to = new MatchSquare(x, y);
                break;
            }
        }
        foreach (bool safe in new[] { true, false })
        {
            if (to.HasValue) break;
            int bestD = int.MaxValue;
            foreach (MatchSquare c in BoardOrder(giant ? 1 : 0))
            {
                if (giant ? !GiantFits(h, q, c.x, c.y, safe) : !IsFree(h, c.x, c.y, safe)) continue;
                int d = ShoveDistance(c.x, c.y, qx, qy);
                if (d < bestD)
                {
                    bestD = d;
                    to = c;
                }
            }
        }
        if (!to.HasValue)
        {
            h.Destroy(q); // 盤が埋まっていて押し出せない（実際には起こらない）
            return;
        }
        h.Emit(new BossEvent { Type = BossEventType.Shove, Piece = q, From = new MatchSquare(qx, qy), To = to.Value });
        h.Relocate(q, to.Value.x, to.Value.y);
    }

    // ------------------------------------------------------------
    //  手下（小赤鬼。Prototype の minions.js の chase）
    // ------------------------------------------------------------

    // 手下を (x, y) に呼び出す。最初の 1 歩は StepSec 秒後
    public static TPiece Spawn(IBossHost<TPiece> h, PieceType type, int x, int y, string attack = null)
    {
        TPiece q = h.SpawnMinion(type, x, y);
        if (q == null) return null;
        RaidState raid = h.Raid;
        h.SetMinion(q, new MinionData { Active = true, Serial = raid != null ? raid.minionSerial++ : 0, Next = h.Now + Ticks(MinionRules.StepSec) });
        h.Emit(new BossEvent { Type = BossEventType.MinionSpawned, Piece = q, At = new MatchSquare(x, y), Attack = attack });
        return q;
    }

    // 手下を 1 tick 進める（現れた順）
    static void TickMinions(IBossHost<TPiece> h)
    {
        foreach (TPiece m in Minions(h))
        {
            if (!h.IsAlive(m)) continue;
            TickStepper(h, m);
            if (h.IsOver || h.Boss.hp <= 0) return;
        }
    }

    // 行き先を WarnSec 秒前に決め、StepSec 秒ごとに動く
    static void TickStepper(IBossHost<TPiece> h, TPiece m)
    {
        MinionData st = h.GetMinion(m);
        if (!st.Active) return;
        int now = h.Now;
        if (!st.Planned && now >= st.Next - Ticks(MinionRules.WarnSec))
        {
            MatchSquare? to = PlanMinionStep(h, m);
            st.Planned = true;
            st.HasTo = to.HasValue;
            st.ToX = to.HasValue ? to.Value.x : -1;
            st.ToY = to.HasValue ? to.Value.y : -1;
            h.SetMinion(m, st);
            h.Emit(new BossEvent
            {
                Type = BossEventType.MinionPlanned, Piece = m, From = new MatchSquare(h.XOf(m), h.YOf(m)),
                HasTo = st.HasTo, To = to ?? MatchSquare.None, Sec = MinionRules.WarnSec,
            });
        }
        if (now >= st.Next)
        {
            MatchSquare? to = st.HasTo ? new MatchSquare(st.ToX, st.ToY) : (MatchSquare?)null;
            st.Next = now + Ticks(MinionRules.StepSec);
            st.Planned = false;
            st.HasTo = false;
            st.ToX = -1;
            st.ToY = -1;
            h.SetMinion(m, st);
            StepMinion(h, m, to);
        }
    }

    // 手下が (x, y) へ動けるか（プレイヤーの駒のマスは「襲う」として動ける）
    static bool CanStep(IBossHost<TPiece> h, int x, int y, bool safe)
    {
        if (!In(x, y) || h.IsBlocked(x, y) || h.Boss.Covers(x, y)) return false;
        TPiece q = h.PieceAt(x, y);
        if (q != null && h.OwnerOf(q) != PlayerType.Player1) return false;
        return !(safe && q == null && h.IsHazard(x, y));
    }

    // 手下 m の次の行き先を決める（動けなければ null）：周り 8 マスのうち、プレイヤーの駒が隣にいればそのどれか（襲う）。
    // いなければ王将に近づくマス（縦横斜めの歩数、同じならまっすぐなほう。それでも同じなら対局の乱数）
    public static MatchSquare? PlanMinionStep(IBossHost<TPiece> h, TPiece m)
    {
        int mx = h.XOf(m), my = h.YOf(m);
        List<MatchSquare> ok = new List<MatchSquare>();
        foreach (int[] d in Dirs)
        {
            if (CanStep(h, mx + d[0], my + d[1], true)) ok.Add(new MatchSquare(mx + d[0], my + d[1]));
        }
        if (ok.Count == 0) return null;
        List<MatchSquare> prey = ok.FindAll(c => h.PieceAt(c.x, c.y) != null);
        if (prey.Count > 0) return prey[Pick(h, prey.Count)];
        TPiece king = FindKing(h, PlayerType.Player1);
        if (king == null) return ok[Pick(h, ok.Count)];
        int kx = h.XOf(king), ky = h.YOf(king);
        int best = int.MaxValue;
        foreach (MatchSquare c in ok) best = System.Math.Min(best, ShoveDistance(c.x, c.y, kx, ky));
        List<MatchSquare> closest = ok.FindAll(c => ShoveDistance(c.x, c.y, kx, ky) == best);
        return closest[Pick(h, closest.Count)];
    }

    // 手下 m を行き先へ動かす。プレイヤーの駒がいれば襲い、消えたら進む（行き先を決めたあとに押し出されて隣でなくなったら動かない）
    static void StepMinion(IBossHost<TPiece> h, TPiece m, MatchSquare? target)
    {
        if (!target.HasValue) return;
        MatchSquare to = target.Value;
        int mx = h.XOf(m), my = h.YOf(m);
        if (System.Math.Max(System.Math.Abs(to.x - mx), System.Math.Abs(to.y - my)) != 1 || !CanStep(h, to.x, to.y, false)) return;
        TPiece q = h.PieceAt(to.x, to.y);
        if (q != null)
        {
            h.Emit(new BossEvent { Type = BossEventType.MinionStrike, Piece = m, Target = q, At = to });
            HitPiece(h, q, MinionRules.AttackId);
            if (!h.IsAlive(m) || h.PieceAt(to.x, to.y) != null || !CanStep(h, to.x, to.y, false)) return; // 爆発に巻き込まれた・駒が残った
        }
        h.Emit(new BossEvent { Type = BossEventType.MinionStep, Piece = m, From = new MatchSquare(mx, my), To = to });
        h.Relocate(m, to.x, to.y);
    }

    // ------------------------------------------------------------
    //  ボスへのダメージ（Prototype の damage.js の damageBoss のうち、時間停止の予定でない分）
    // ------------------------------------------------------------

    // ボスにダメージを与える（HP を減らして通知する）。与えたダメージを返す
    public static int Damage(IBossHost<TPiece> h, BossHit hit, MatchSquare at)
    {
        BossState b = h.Boss;
        if (b == null || !b.active || b.hp <= 0) return 0;
        int amount = System.Math.Min(b.hp, RaidRules.BossDamage(b.Def, hit));
        if (amount <= 0) return 0;
        b.hp -= amount;
        h.Emit(new BossEvent { Type = BossEventType.Damaged, Amount = amount, Hp = b.hp, MaxHp = b.maxHp, Source = hit, At = at });
        if (b.hp <= 0) h.Emit(new BossEvent { Type = BossEventType.Defeated });
        return amount;
    }
}

// 討伐任務の勝敗（Prototype の formation.js の raidResult）
public enum RaidOutcome
{
    None,       // まだ決まっていない
    Cleared,    // ボスの HP 0：討伐成功（先手の勝ち）
    KingLost,   // 王将が倒れた：討伐失敗
    KingHits,   // 王将の被弾がボスの上限に達した：討伐失敗
}

public static class RaidResults
{
    // ボスの HP 0 → 討伐成功 ／ 王将が倒れた・王将の被弾が上限 → 討伐失敗（この順に見る）
    public static RaidOutcome Of(BossState boss, RaidState raid, bool kingAlive)
    {
        if (boss != null && boss.active && boss.hp <= 0) return RaidOutcome.Cleared;
        if (!kingAlive) return RaidOutcome.KingLost;
        BossDef def = boss != null ? boss.Def : null;
        int max = def != null ? def.KingMaxHits : 3;
        if (raid != null && raid.active && raid.kingHits >= max) return RaidOutcome.KingHits;
        return RaidOutcome.None;
    }

    public static bool IsCleared(RaidOutcome o) => o == RaidOutcome.Cleared;

    // クリアタイムなどの表示（m:ss.s）
    public static string TimeLabel(int ticks)
    {
        float sec = MatchPolicy.TicksToSec(ticks);
        int m = (int)(sec / 60f);
        return $"{m}:{sec - m * 60:00.0}";
    }
}
