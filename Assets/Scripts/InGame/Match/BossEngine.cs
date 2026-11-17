using System.Collections.Generic;

// ボスの AI と攻撃（Prototype の src/core/boss/ai.js・attack.js・effects.js・minions.js と src/defs/bossAttacks/*.js）。
// 対局（GameSystem.Boss.cs。駒は GamePiece）と盤面のシミュレーション（BoardSimulator.Boss.cs。駒は SnapshotPiece）の両方が、
// 同じこのエンジンを使う（ルールは 1 か所。盤の読み書きだけを IBossHost で受け持つ）。MonoBehaviour に依存しない。
//
//  AI の状態機械（BossState.ai。毎 tick、対局の時間で進める。乱数は対局の乱数）
//    cooldown（行動ペース）└ 期限 → 攻撃を選ぶ → 向きを決める（王将の方。向きを固定するボスはその向き）→ 範囲を確定 → telegraph
//                            （予告の秒数が 0 の攻撃（守備）は予告なしで、すぐ下の「期限」の処理へ）
//    telegraph（予告）     ├ 照準が駒を追う攻撃（弓）：期限の LockSecBefore 秒前まで毎 tick 範囲を直し、そこで固める
//                          ├ 一発の攻撃：期限 → 当てる → cooldown
//                          └ 続く行動（暴れ回り・守備）：期限 → acting
//    acting（毎 tick）→ 期限 → resting（休憩。行動ペースを数えない。休憩のない行動は cooldown）→ 期限 → cooldown
//  手下（ボスの席の駒）も、毎 tick ボスの AI の前に進める（現れた順）。動き方は駒の種類で決まる（MinionRules.PatternOf）：
//    小赤鬼＝周り 8 マスで襲う・王将へ近づく ／ 軍歩兵＝前へ 1 マスずつ ／ 軍銃兵＝照準で追った駒のマスを撃つ ／ 防壁＝動かない。
//  停止世界の間・勝敗がついたあとは進めない（呼ぶ側と Tick が見る）。ボスの HP が 0 になったあとは、手下が残っていると勝てないボス
//  （BossDef.MinionsOutliveBoss：大将軍）だけ、手下が動き続ける（ボスの AI は止まる）
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
    int SerialOf(TPiece p);                    // 駒の番号（作った順。照準が駒を選ぶ順・追う駒を指す。Prototype の駒の id）
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
    TPiece SpawnMinion(PieceType type, int x, int y);   // ボスの席（後手）の駒を (x, y) に置く（防壁なら HP を WallRules.MaxHp にする）

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
        if (b == null || !b.active || h.IsOver) return;
        // ボスの HP が 0 になったあとも手下が残っていると勝てないボス（大将軍）は、手下だけ動き続ける
        if (b.hp <= 0 && !MinionsOutliveBoss(b)) return;
        TickMinions(h);
        if (b.hp <= 0 || h.IsOver) return;
        if (b.ai.phase == BossAiState.Telegraph) TelegraphTick(h);
        else if (b.ai.phase == BossAiState.Acting) ActingTick(h);
        if (b.hp <= 0 || h.IsOver) return;
        if (h.Now >= b.ai.until) Expire(h);
    }

    // ボスの HP が 0 になっても手下が動き続けるか（勝つのに手下を全部倒す必要があるボス。Prototype の minionsOutliveBoss）
    public static bool MinionsOutliveBoss(BossState b)
    {
        BossDef def = b != null ? b.Def : null;
        return def != null && def.MinionsOutliveBoss;
    }

    // ボスの勝ちの条件で、まだ盤上に残っていてはいけない手下の数（Prototype の remainingRequiredMinions）
    public static int RequiredMinionsLeft(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossDef def = b != null ? b.Def : null;
        if (def == null || !def.MinionsOutliveBoss) return 0;
        int n = 0;
        List<TPiece> seen = new List<TPiece>();
        foreach (MatchSquare c in BoardOrder())
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q == null || seen.Contains(q)) continue;
            seen.Add(q);
            if (h.OwnerOf(q) == PlayerType.Player2 && def.IsRequiredMinion(h.TypeOf(q))) n++;
        }
        return n;
    }

    // 防壁の後ろの守り（WallRules.IsShielded）：(x, y) のボスの席の駒が、同じ筋で手前にある防壁に守られているか
    public static bool IsShieldedByWall(IBossHost<TPiece> h, int x, int y)
    {
        return WallRules.IsShielded((px, py) =>
        {
            TPiece q = h.PieceAt(px, py);
            return q != null ? h.TypeOf(q) : (PieceType?)null;
        }, x, y);
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
        ai.aim = default;
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
                DecideFacing(h);
                ClearData(ai); // 範囲を決めるとき（AttackCells）に、攻撃が途中のデータ（照準）を入れられる
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
                // 続く行動の終わり（暴れ回り：次の 1 歩の表示を消す ／ 守備：構えを解く）
                ClearData(ai);
                if (atk != null && atk.Invulnerable) h.Emit(new BossEvent { Type = BossEventType.GuardEnd, Attack = atk.Id });
                else h.Emit(new BossEvent { Type = BossEventType.StepWarn, Cells = new List<MatchSquare>(), Sec = 0f });
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
            // 続く行動の始め（暴れ回り：最初の 1 歩を決めて表示する ／ 守備：構える）
            if (atk.Invulnerable) h.Emit(new BossEvent { Type = BossEventType.GuardStart, Attack = atk.Id, Sec = atk.DurationSec, Until = ai.until });
            else PlanRampageStep(h, atk);
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

    // 攻撃を選んだときの向き：向きを固定するボス（BossDef.HasFixedFacing。動かない・向きを変えないボス）はその向き。それ以外は王将の方
    static void DecideFacing(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        BossDef def = b.Def;
        if (def != null && def.HasFixedFacing)
        {
            b.facingX = def.FixedFacingX;
            b.facingY = def.FixedFacingY;
            return;
        }
        FaceToward(h, FindKing(h, PlayerType.Player1));
    }

    // 予告の毎 tick：照準が駒を追う攻撃（弓）は、期限の LockSecBefore 秒前まで範囲を直し、そこで固める（Prototype の PHASES.telegraph.onTick）
    static void TelegraphTick(IBossHost<TPiece> h)
    {
        BossAiState ai = h.Boss.ai;
        BossAttackDef atk = BossAttacks.ById(ai.attack);
        if (atk == null || !atk.Retarget || ai.aim.locked) return;
        if (h.Now >= ai.until - Ticks(atk.LockSecBefore))
        {
            ai.aim.locked = true;
            h.Emit(new BossEvent { Type = BossEventType.TelegraphUpdate, Attack = ai.attack, Cells = new List<MatchSquare>(ai.cells), Locked = true });
            return;
        }
        if (!ai.aim.active || !FollowAim(h, ref ai.aim)) return;
        ai.hasCells = true;
        ai.cells = new List<MatchSquare> { ai.aim.Cell };
        h.Emit(new BossEvent { Type = BossEventType.TelegraphUpdate, Attack = ai.attack, Cells = new List<MatchSquare>(ai.cells), Locked = false });
    }

    // ------------------------------------------------------------
    //  照準（Prototype の src/core/boss/aim.js）。乱数は対局の乱数。追っていた駒が消えたら、残っている駒からまた選ぶ
    // ------------------------------------------------------------

    // 照準の候補：盤上のプレイヤーの駒（王将も含む。駒の番号の順）
    public static List<TPiece> AimCandidates(IBossHost<TPiece> h)
    {
        List<TPiece> res = new List<TPiece>();
        foreach (MatchSquare c in BoardOrder())
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q != null && h.OwnerOf(q) == PlayerType.Player1 && !res.Contains(q)) res.Add(q);
        }
        // 番号の順（同じ番号は盤の順のまま：List.Sort は安定でないので、添字も比べる）
        List<int> order = new List<int>();
        for (int i = 0; i < res.Count; i++) order.Add(i);
        order.Sort((a, c) =>
        {
            int d = h.SerialOf(res[a]).CompareTo(h.SerialOf(res[c]));
            return d != 0 ? d : a.CompareTo(c);
        });
        List<TPiece> sorted = new List<TPiece>(res.Count);
        foreach (int i in order) sorted.Add(res[i]);
        return sorted;
    }

    // 駒を 1 枚ランダムに選ぶ（いなければ null。そのときは乱数を引かない）
    static TPiece PickAimTarget(IBossHost<TPiece> h)
    {
        List<TPiece> list = AimCandidates(h);
        return list.Count > 0 ? list[Pick(h, list.Count)] : null;
    }

    // 新しい照準（駒を 1 枚選んで、その駒のマスを狙う）
    public static AimData CreateAim(IBossHost<TPiece> h)
    {
        TPiece t = PickAimTarget(h);
        AimData aim = new AimData { active = true };
        if (t != null)
        {
            aim.target = h.SerialOf(t);
            aim.hasCell = true;
            aim.x = h.XOf(t);
            aim.y = h.YOf(t);
        }
        return aim;
    }

    // 番号 serial のプレイヤーの駒（盤上にいなければ null）
    static TPiece FindBySerial(IBossHost<TPiece> h, int serial)
    {
        if (serial <= 0) return null;
        foreach (MatchSquare c in BoardOrder())
        {
            TPiece q = h.PieceAt(c.x, c.y);
            if (q != null && h.SerialOf(q) == serial && h.IsAlive(q)) return q;
        }
        return null;
    }

    // 照準を追わせる（固まっていれば何もしない）。追っていた駒が消えたら選び直す。照準のマスが変わったら true
    public static bool FollowAim(IBossHost<TPiece> h, ref AimData aim)
    {
        if (aim.locked) return false;
        TPiece t = FindBySerial(h, aim.target);
        if (t == null)
        {
            t = PickAimTarget(h);
            aim.target = t != null ? h.SerialOf(t) : 0;
        }
        if (t == null) return false; // 選べる駒がない（照準のマスはそのまま）
        int tx = h.XOf(t), ty = h.YOf(t);
        if (aim.hasCell && aim.x == tx && aim.y == ty) return false;
        aim.hasCell = true;
        aim.x = tx;
        aim.y = ty;
        return true;
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
            case "bow":
            {
                // 弓：駒を 1 枚選んで照準を合わせる（照準は ai.aim に入れ、予告の間その駒を追う）
                b.ai.aim = CreateAim(h);
                List<MatchSquare> res = new List<MatchSquare>();
                if (b.ai.aim.hasCell) res.Add(b.ai.aim.Cell);
                return res;
            }
            case "greatsword":
            {
                // 大剣斬り：正面に接する 横（ボスの幅 ＋ 左右 1 マス）× 奥行き GreatswordDepth マス（手前の列から。各列は Prototype の並び）
                List<MatchSquare> res = new List<MatchSquare>();
                for (int k = 0; k < BossAttacks.GreatswordDepth; k++)
                {
                    if (b.facingX != 0)
                    {
                        int x = b.facingX > 0 ? b.x + b.h + k : b.x - 1 - k;
                        for (int y = b.y - 1; y <= b.y + b.w; y++)
                        {
                            if (In(x, y)) res.Add(new MatchSquare(x, y));
                        }
                    }
                    else
                    {
                        int y = b.facingY > 0 ? b.y + b.w + k : b.y - 1 - k;
                        for (int x = b.x + b.h; x >= b.x - 1; x--)
                        {
                            if (In(x, y)) res.Add(new MatchSquare(x, y));
                        }
                    }
                }
                return res;
            }
            case "deploy":
            {
                // 出陣命令：正面に接する 1 列（盤の端から端まで）
                List<MatchSquare> res = new List<MatchSquare>();
                if (b.facingX != 0)
                {
                    int x = b.facingX > 0 ? b.x + b.h : b.x - 1;
                    for (int y = 0; y < BoardCells.Size; y++)
                    {
                        if (In(x, y)) res.Add(new MatchSquare(x, y));
                    }
                }
                else
                {
                    int y = b.facingY > 0 ? b.y + b.w : b.y - 1;
                    for (int x = BoardCells.Size - 1; x >= 0; x--)
                    {
                        if (In(x, y)) res.Add(new MatchSquare(x, y));
                    }
                }
                return res;
            }
            default:
                return null; // 暴れ回り・守備：範囲を出さない
        }
    }

    // 一発の攻撃を当てる
    static void ApplyAttack(IBossHost<TPiece> h, BossAttackDef atk, List<MatchSquare> cells)
    {
        if (atk.Id == "deploy")
        {
            // 出陣命令：駒に攻撃は当てない。列の空いたマス（駒・ボス・破壊マス・壁がない）に兵を置く
            List<MatchSquare> spawned = new List<MatchSquare>();
            foreach (MatchSquare c in cells)
            {
                if (h.PieceAt(c.x, c.y) != null || h.IsBlocked(c.x, c.y) || h.Boss.Covers(c.x, c.y)) continue;
                if (Spawn(h, BossAttacks.DeployPiece, c.x, c.y, atk.Id) != null) spawned.Add(c);
            }
            h.Emit(new BossEvent { Type = BossEventType.Deployed, Attack = atk.Id, Cells = spawned });
            return;
        }
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
            if (h.DamageGiant(q, AttackName(attack))) h.Smash(q); // 巨人：1 回の攻撃（HP が 0 になったら撃破）
            return result;
        }
        h.Smash(q);
        return result;
    }

    // 攻撃の名前（ボスの攻撃は BossAttacks、手下の攻撃は MinionRules。なければ「ボスの攻撃」）
    public static string AttackName(string attack)
    {
        return MinionRules.AttackName(attack) ?? BossAttacks.NameOf(attack);
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
        if (atk == null || atk.Invulnerable) return; // 守備：毎 tick の処理はない
        if (!ai.hasData || ai.nextAt < 0 || h.Now < ai.nextAt) return;
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
    //  手下（ボスの席の駒。Prototype の minions.js）。動き方は MinionRules.PatternOf
    // ------------------------------------------------------------

    // ボスの席の駒を (x, y) に置く（通知なし。討伐任務の初期配置・呼び出しの共通部分。Prototype の placeMinion）。動き方ごとの状態を作る：
    // 一定の間隔で動く手下の最初の 1 歩は StepSec 秒後。軍銃兵は置いたときに照準を選ぶ（対局の乱数）。防壁は番号だけ
    public static TPiece PlaceMinion(IBossHost<TPiece> h, PieceType type, int x, int y)
    {
        TPiece q = h.SpawnMinion(type, x, y);
        if (q == null) return null;
        RaidState raid = h.Raid;
        MinionData d = new MinionData { Serial = raid != null ? raid.minionSerial++ : 0 };
        switch (MinionRules.PatternOf(type))
        {
            case MinionPattern.Chase:
            case MinionPattern.Forward:
                d.Active = true;
                d.Next = h.Now + Ticks(MinionRules.StepSec);
                break;
            case MinionPattern.Gunner:
                d.Gunner = true;
                d.GunNext = h.Now + Ticks(MinionRules.GunnerOffsetSec + MinionRules.GunnerIntervalSec);
                d.Aim = CreateAim(h);
                break;
        }
        h.SetMinion(q, d);
        return q;
    }

    // 手下を (x, y) に呼び出す（通知つき。小赤鬼号令・出陣命令）
    public static TPiece Spawn(IBossHost<TPiece> h, PieceType type, int x, int y, string attack = null)
    {
        TPiece q = PlaceMinion(h, type, x, y);
        if (q == null) return null;
        h.Emit(new BossEvent { Type = BossEventType.MinionSpawned, Piece = q, At = new MatchSquare(x, y), Attack = attack });
        return q;
    }

    // ボスの定義の初期配置の駒（陣）を置く（討伐任務の準備。定義の順：乱数を引く順になる）
    public static void PlaceEnemies(IBossHost<TPiece> h, BossDef def)
    {
        if (def == null || def.Enemies == null) return;
        foreach (BossEnemy e in def.Enemies) PlaceMinion(h, e.Type, e.X, e.Y);
    }

    // 手下が動いている途中で止めるか（勝敗がついた・ボスが倒れた。ボスが倒れても手下が残っていると勝てないボスは続ける）
    static bool ShouldStop(IBossHost<TPiece> h)
    {
        BossState b = h.Boss;
        return h.IsOver || (b.hp <= 0 && !MinionsOutliveBoss(b));
    }

    // 手下を 1 tick 進める（現れた順）
    static void TickMinions(IBossHost<TPiece> h)
    {
        foreach (TPiece m in Minions(h))
        {
            if (!h.IsAlive(m)) continue;
            switch (MinionRules.PatternOf(h.TypeOf(m)))
            {
                case MinionPattern.Chase: TickStepper(h, m, false); break;
                case MinionPattern.Forward: TickStepper(h, m, true); break;
                case MinionPattern.Gunner: TickGunner(h, m); break;
                default: continue; // 防壁：動かない
            }
            if (ShouldStop(h)) return;
        }
    }

    // 一定の間隔で動く手下：行き先を WarnSec 秒前に決め、StepSec 秒ごとに動く（forward なら前へ、そうでなければ周り 8 マス）
    static void TickStepper(IBossHost<TPiece> h, TPiece m, bool forward)
    {
        MinionData st = h.GetMinion(m);
        if (!st.Active) return;
        int now = h.Now;
        if (!st.Planned && now >= st.Next - Ticks(MinionRules.WarnSec))
        {
            MatchSquare? to = forward ? PlanForwardStep(h, m) : PlanMinionStep(h, m);
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
            StepMinion(h, m, to, forward);
        }
    }

    // 前へ進む手下が (x, y) へ進めるか：盤内・破壊マス・壁でない・ボスでない・ボスの席の駒（防壁・兵）がいない（プレイヤーの駒は「攻撃」として進める）
    static bool CanAdvance(IBossHost<TPiece> h, int x, int y)
    {
        if (!In(x, y) || h.IsBlocked(x, y) || h.Boss.Covers(x, y)) return false;
        TPiece q = h.PieceAt(x, y);
        return q == null || h.OwnerOf(q) == PlayerType.Player1;
    }

    // 前へ進む手下の次のマス（プレイヤー側へ 1 マス。進めなければ null。いちばん手前の段にいれば盤の外なので止まる）
    public static MatchSquare? PlanForwardStep(IBossHost<TPiece> h, TPiece m)
    {
        int x = h.XOf(m) - 1, y = h.YOf(m);
        return CanAdvance(h, x, y) ? new MatchSquare(x, y) : (MatchSquare?)null;
    }

    // 軍銃兵を 1 tick 進める：撃つ時刻なら撃って次の周期（新しい照準）、撃つ LockSecBefore 秒前で照準を固め、それまでは駒を追う
    static void TickGunner(IBossHost<TPiece> h, TPiece m)
    {
        MinionData g = h.GetMinion(m);
        if (!g.Gunner) return;
        int now = h.Now;
        if (now >= g.GunNext)
        {
            FireGunner(h, m, g);
            if (!h.IsAlive(m)) return; // 撃った先の火薬の爆発に巻き込まれた
            g.GunNext = now + Ticks(MinionRules.GunnerIntervalSec);
            g.Aim = CreateAim(h);
            h.SetMinion(m, g);
            EmitGunnerAim(h, m, g.Aim);
            return;
        }
        if (!g.Aim.active)
        {
            g.Aim = CreateAim(h);
            h.SetMinion(m, g);
            EmitGunnerAim(h, m, g.Aim);
            return;
        }
        if (!g.Aim.locked && now >= g.GunNext - Ticks(MinionRules.GunnerLockSecBefore))
        {
            g.Aim.locked = true;
            h.SetMinion(m, g);
            EmitGunnerAim(h, m, g.Aim);
            return;
        }
        if (FollowAim(h, ref g.Aim))
        {
            h.SetMinion(m, g);
            EmitGunnerAim(h, m, g.Aim);
        }
        else h.SetMinion(m, g); // 追う駒を選び直しただけ（マスは同じ）でも、選んだ駒は残す
    }

    static void EmitGunnerAim(IBossHost<TPiece> h, TPiece m, AimData aim)
    {
        h.Emit(new BossEvent
        {
            Type = BossEventType.GunnerAim, Piece = m, From = new MatchSquare(h.XOf(m), h.YOf(m)),
            HasTo = aim.hasCell, To = aim.Cell, Locked = aim.locked,
        });
    }

    // 軍銃兵の照準のマスを撃つ：プレイヤーの駒がいれば、ボスの攻撃を受ける（防壁・ほかの駒にはさえぎられない。ボスの席の駒には当たらない）
    static void FireGunner(IBossHost<TPiece> h, TPiece m, MinionData g)
    {
        if (!g.Aim.active || !g.Aim.hasCell) return;
        MatchSquare cell = g.Aim.Cell;
        TPiece q = h.PieceAt(cell.x, cell.y);
        bool hit = q != null && h.OwnerOf(q) == PlayerType.Player1;
        h.Emit(new BossEvent
        {
            Type = BossEventType.GunnerShot, Piece = m, From = new MatchSquare(h.XOf(m), h.YOf(m)), At = cell,
            Target = hit ? q : null, Hits = hit ? 1 : 0, Attack = MinionRules.GunshotAttackId,
        });
        if (hit) HitPiece(h, q, MinionRules.GunshotAttackId);
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

    // 手下 m を行き先へ動かす。プレイヤーの駒がいれば襲い、消えたら進む（行き先を決めたあとに押し出されて隣でなくなったら動かない）。
    // forward（軍歩兵）は、行き先の判定が CanAdvance（炎上・落とし穴のマスへも進む）
    static void StepMinion(IBossHost<TPiece> h, TPiece m, MatchSquare? target, bool forward)
    {
        if (!target.HasValue) return;
        MatchSquare to = target.Value;
        int mx = h.XOf(m), my = h.YOf(m);
        bool CanGo() => forward ? CanAdvance(h, to.x, to.y) : CanStep(h, to.x, to.y, false);
        if (System.Math.Max(System.Math.Abs(to.x - mx), System.Math.Abs(to.y - my)) != 1 || !CanGo()) return;
        TPiece q = h.PieceAt(to.x, to.y);
        if (q != null)
        {
            h.Emit(new BossEvent { Type = BossEventType.MinionStrike, Piece = m, Target = q, At = to });
            HitPiece(h, q, MinionRules.AttackIdOf(h.TypeOf(m)));
            if (!h.IsAlive(m) || h.PieceAt(to.x, to.y) != null || !CanGo()) return; // 爆発に巻き込まれた・駒が残った
        }
        h.Emit(new BossEvent { Type = BossEventType.MinionStep, Piece = m, From = new MatchSquare(mx, my), To = to });
        h.Relocate(m, to.x, to.y);
    }

    // ------------------------------------------------------------
    //  ボスへのダメージ（Prototype の damage.js の damageBoss のうち、時間停止の予定でない分）
    // ------------------------------------------------------------

    // ボスが今ダメージを受けないか：続く行動（acting）の攻撃が Invulnerable（守備）のあいだ（Prototype の isBossInvulnerable）
    public static bool IsInvulnerable(BossState b)
    {
        if (b == null || !b.active || b.ai == null || b.ai.phase != BossAiState.Acting) return false;
        BossAttackDef atk = BossAttacks.ById(b.ai.attack);
        return atk != null && atk.Invulnerable;
    }

    // ボスにダメージを与える（HP を減らして通知する）。与えたダメージを返す（守りの間は 0：ここ 1 か所で止める）
    public static int Damage(IBossHost<TPiece> h, BossHit hit, MatchSquare at)
    {
        BossState b = h.Boss;
        if (b == null || !b.active || b.hp <= 0) return 0;
        if (IsInvulnerable(b))
        {
            h.Emit(new BossEvent { Type = BossEventType.Guarded, Source = hit, At = at });
            return 0;
        }
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
    Cleared,    // ボスの HP 0（大将軍は兵の全滅も）：討伐成功（先手の勝ち）
    KingLost,   // 王将が倒れた：討伐失敗
    KingHits,   // 王将の被弾がボスの上限に達した：討伐失敗
}

public static class RaidResults
{
    // ボスの HP 0（手下を全滅させる必要があるボスは、その種類の手下も盤上に 1 体もいない：requiredMinionsLeft = 0）→ 討伐成功 ／
    // 王将が倒れた・王将の被弾が上限 → 討伐失敗（この順に見る）
    public static RaidOutcome Of(BossState boss, RaidState raid, bool kingAlive, int requiredMinionsLeft = 0)
    {
        if (boss != null && boss.active && boss.hp <= 0 && requiredMinionsLeft <= 0) return RaidOutcome.Cleared;
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
