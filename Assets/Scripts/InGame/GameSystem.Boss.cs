using System.Collections.Generic;
using UnityEngine;

// 討伐任務（ボスバトル。Prototype の src/defs/modes/bossRaid.js・src/core/boss/）。GameSystem の続き（partial）。
//
//  ・モード「討伐任務」：プレイヤー 1 人（先手・盤の手前）対 ボス（後手の席。行動するのは Match/BossEngine.cs の AI）。戦闘型は合戦将棋型だけ。
//    対局の始め（Start）に、MatchSetup（棋譜の再生なら棋譜）のボスと編成で、自陣に駒を並べてボスを置く（SetupRaid）
//  ・ボスは駒ではない：盤の駒の配列には入れず、MatchState.Boss（BossState）が持つ（GameBoard.Boss からも引ける）。3×3 マス・HP 30
//  ・駒をボスのマスへ動かすと攻撃になり、駒は元のマスへ跳ね返る（MovePieceCore）。必殺技・魔法は 1 回の効果につき 1 回だけ当たる
//    （BeginBossEffect 〜 EndBossEffect の間の EffectHitBoss）。爆発・ファランクスの槍・狙撃も当たる。ダメージの表は Match/RaidRules.cs。
//    時間停止中のダメージは予定にして、時間が動き出してから与える（TimeStopKind.Boss）
//  ・ボスの AI・攻撃・手下（小赤鬼）は、盤面のシミュレーション（BoardSimulator.Boss.cs）と同じ BossEngine を使う
//    （ここは盤の読み書きと演出の通知だけ：BossHostImpl）。毎 tick、自動行動のあとに進める（RealtimeTick）。停止世界の間は進めない
//  ・勝敗：ボスの HP 0 → 討伐成功 ／ 王将が倒れた・王将の被弾がボスの上限 → 討伐失敗（ResolveKingLoss から ResolveRaid）
//  ・エナジー：1 秒ごとの +1 は先手だけ。盤上の駒の移動・必殺技ごとに +2（AfterRealtimeAction）
//  ・見た目（盤上のボス・予告の範囲と残り秒・小赤鬼の矢印・HP のパネル）は BossView が BossEventRaised を受けて出す
// ここ（ダメージの当て方・勝敗・行動のあとの処理）を変えたら、BoardSimulator.Boss.cs も同じように変えること
public partial class GameSystem
{
    // ボスの攻撃で撃破された駒の消失の理由（PieceDefeat.Reason。演出で見分けるのに使う）
    public const string ReasonBossAttack = "ボスの攻撃";

    // ボス・手下の動きの通知（BossView・ログ用。盤面はもう変わっている）
    public event System.Action<BossEvent> BossEventRaised;

    // 討伐任務の対局か
    public bool IsRaid => match != null && match.Policy.IsRaid && match.HasBoss;
    // ボスの状態（討伐任務でなければ null）
    public BossState Boss => match != null && match.HasBoss ? match.Boss : null;
    public RaidState Raid => IsRaid ? match.Raid : null;
    public BossDef BossDefinition => Boss != null ? Boss.Def : null;
    // (x, y) がボスのマスか（倒れたボスはどのマスも占めない）
    public bool BossAt(int x, int y) => match != null && match.BossAt(x, y);
    // 今の勝敗（討伐任務でなければ None）
    public RaidOutcome RaidOutcomeNow => IsRaid ? RaidResults.Of(match.Boss, match.Raid, IsKingAlive(PlayerType.Player1)) : RaidOutcome.None;
    // 王将があと何回ボスの攻撃に耐えられるか
    public int KingHitsLeft
    {
        get
        {
            BossDef def = BossDefinition;
            return def != null && Raid != null ? Mathf.Max(0, def.KingMaxHits - 1 - Raid.kingHits) : 0;
        }
    }

    private List<ForgeRecipe> raidRecipes;   // 討伐任務で錬成できる駒（編成から決まる。討伐任務でなければ null）
    private BossHostImpl bossHost;

    // ------------------------------------------------------------
    //  対局の準備（Start から）
    // ------------------------------------------------------------

    // この対局のモードを決める：棋譜の再生なら棋譜の設定、それ以外は MatchSetup（オンライン対戦は基本対戦）。
    // 討伐任務なら def にボス、formation に編成（正しくない編成なら、おまかせ配置にする）
    GameModeDef DecideStartMode(out BossDef def, out RaidFormation formation)
    {
        def = null;
        formation = null;
        if (IsOnlineMatch) return GameModes.Versus;
        if (IsReplaying)
        {
            KifuFile k = replay.Kifu;
            if (k.config != null && k.config.mode == GameModes.BossRaid.Id)
            {
                def = BossDefs.ById(k.config.boss);
                formation = k.setup;
            }
        }
        else if (MatchSetup.IsBossRaid)
        {
            def = MatchSetup.Boss;
            formation = MatchSetup.Formation;
        }
        if (def == null) return GameModes.Versus;
        if (!def.Implemented)
        {
            Debug.LogWarning($"ボス「{def.Name}」はまだ遊べません。基本対戦として始めます");
            def = null;
            return GameModes.Versus;
        }
        if (!RaidRules.IsValid(formation, def))
        {
            if (formation != null) Debug.LogWarning("討伐任務の編成が正しくないので、おまかせ配置で始めます：" + string.Join(" / ", RaidRules.Validate(formation, def, out _)));
            formation = new RaidFormation { pieces = RaidRules.AutoFormation(def) };
        }
        return GameModes.BossRaid;
    }

    // 討伐任務の準備（Prototype の setupRaid）：ボスを置き、編成を自陣に並べ、錬成できる駒を決める。後手（ボスの席）は魔法を持たない
    void SetupRaid(BossDef def, RaidFormation formation)
    {
        match.SetupRaid(def, formation);
        gameBoard.Boss = match.Boss;
        bossHost = new BossHostImpl(this);

        List<PieceSpawnData> layout = new List<PieceSpawnData>();
        foreach (RaidPlacement f in formation.pieces)
        {
            layout.Add(new PieceSpawnData { X = f.x, Y = f.y, type = f.type, player = PlayerType.Player1 });
        }
        gameBoard.UseLayout(layout);

        raidRecipes = BuildRaidRecipes(formation);
        if (player2 != null) player2.SetSkillDeck(EmptyDeck("Boss Seat Deck"));

        if (FindAnyObjectByType<BossView>() == null) gameObject.AddComponent<BossView>();
        Debug.Log($"討伐任務：{def.Name}（HP {def.Hp}）。編成 {formation.pieces.Count} 枚（コスト {RaidRules.FormationCost(formation.pieces)} / {def.CostLimit}）・" +
                  $"錬成できる駒 {raidRecipes.Count} 種。王将はボスの攻撃に {def.KingMaxHits - 1} 回まで耐える");
    }

    static SkillDeck EmptyDeck(string name)
    {
        SkillDeck deck = ScriptableObject.CreateInstance<SkillDeck>();
        deck.name = name;
        deck.skills = new List<SkillDefinition>();
        for (int i = 0; i < SkillRanks.SlotCount; i++) deck.skills.Add(null);
        return deck;
    }

    // 錬成できる駒（RaidRules.ForgeRecipes）のレシピを作る。オリジナル駒は Resources/ForgeRecipeBook のレシピ（絵・説明つき）を使い、
    // 通常の駒は実行時に作る（コストは RaidRules.RegularForgeCost）
    List<ForgeRecipe> BuildRaidRecipes(RaidFormation formation)
    {
        List<ForgeRecipe> res = new List<ForgeRecipe>();
        if (forgeRecipeBook == null) forgeRecipeBook = ForgeRecipeBook.LoadDefault();
        foreach (RaidForgeOption o in RaidRules.ForgeRecipes(formation))
        {
            ForgeRecipe recipe = null;
            if (forgeRecipeBook != null)
            {
                foreach (ForgeRecipe r in forgeRecipeBook.recipes)
                {
                    if (r != null && r.pieceType == o.Type) recipe = r;
                }
            }
            if (recipe == null)
            {
                recipe = ScriptableObject.CreateInstance<ForgeRecipe>();
                recipe.name = $"Raid_{o.Type}_Recipe";
                recipe.pieceType = o.Type;
                recipe.label = o.Label;
                recipe.cost = o.Cost;
                recipe.description = $"{PieceNames.Full(o.Type)}を 1 枚作って持ち駒に加える（討伐任務：初期配置に選んだ駒・錬成駒に選んだ駒を錬成できる）。";
            }
            res.Add(recipe);
        }
        return res;
    }

    // ------------------------------------------------------------
    //  盤の読み書き（BossEngine へ渡す）
    // ------------------------------------------------------------

    sealed class BossHostImpl : IBossHost<GamePiece>
    {
        readonly GameSystem sys;

        public BossHostImpl(GameSystem sys)
        {
            this.sys = sys;
        }

        public BossState Boss => sys.match.Boss;
        public RaidState Raid => sys.match.Raid;
        public int Now => sys.match.Clock != null ? sys.match.Clock.Time : 0;
        public bool IsOver => !sys.IsInTurn;
        public double NextRandom() => sys.match.Rng.Next();

        public bool IsBlocked(int x, int y) => sys.Cells != null && sys.Cells.IsBlocked(x, y);
        public bool IsHazard(int x, int y) => sys.Cells != null && sys.Cells.IsHazard(x, y);

        public bool IsPlainCell(int x, int y)
        {
            BoardCell c = sys.Cells != null ? sys.Cells.At(x, y) : null;
            return c != null && c.State == CellState.Normal && !c.HasTrap;
        }

        public GamePiece PieceAt(int x, int y) => sys.gameBoard.GetPieceAt(x, y);
        public bool IsAlive(GamePiece p) => p != null && sys.gameBoard.GetPieceAt(p.X, p.Y) == p;
        public PieceType TypeOf(GamePiece p) => p.type;
        public PlayerType OwnerOf(GamePiece p) => p.player;
        public int XOf(GamePiece p) => p.X;
        public int YOf(GamePiece p) => p.Y;
        public bool HasShield(GamePiece p) => p.hasShield;
        public bool IsUndead(GamePiece p) => p.isUndead;
        public int HpOf(GamePiece p) => p.hp;
        public MinionData GetMinion(GamePiece p) => p.minion;
        public void SetMinion(GamePiece p, MinionData d) => p.minion = d;

        public void Explode(GamePiece bomb) => sys.Explode(bomb.X, bomb.Y, 0f);
        public void BreakShield(GamePiece p) => sys.BreakShield(p, null, PlayerType.Player2);
        public void VanishUndead(GamePiece p) => sys.VanishPiece(p, ReasonUndeadRest);
        public bool DamageGiant(GamePiece p, string by) => sys.HitGiant(p, by);

        public void Smash(GamePiece p)
        {
            PlayerType owner = p.player;
            PieceType type = p.type;
            sys.RemoveDefeatedPiece(new PieceDefeat(p, DefeatKind.Vanish, null, ReasonBossAttack));
            sys.AddPoint(owner, PieceValues.Of(type)); // 持ち主が駒の価値ぶんのエナジーを得る（誰の持ち駒にもならない）
        }

        public void Destroy(GamePiece p)
        {
            sys.RemoveDefeatedPiece(new PieceDefeat(p, DefeatKind.Vanish, null, ReasonBossAttack));
        }

        public void Relocate(GamePiece p, int x, int y)
        {
            if (PieceRules.IsGiant(p)) sys.gameBoard.MoveGiant(p, x, y);
            else
            {
                sys.gameBoard.UpdateBoardData(p.X, p.Y, x, y);
                p.X = x;
                p.Y = y;
            }
            sys.AnimatePieceMove(p, GameHelper.CalcPanelLocation(x, y));
            sys.EnterCell(p, x, y);
        }

        public GamePiece SpawnMinion(PieceType type, int x, int y)
        {
            return sys.gameBoard.SpawnPiece(type, PlayerType.Player2, x, y);
        }

        public void Emit(BossEvent e) => sys.OnBossEvent(e);
    }

    // ボス・手下の動きの通知：ログ・案内文を出して、BossView へ渡す
    void OnBossEvent(BossEvent e)
    {
        boardChangedThisStep = true;
        BossDef def = BossDefinition;
        string boss = def != null ? def.Name : "ボス";
        switch (e.Type)
        {
            case BossEventType.Telegraph:
            {
                BossAttackDef atk = BossAttacks.ById(e.Attack);
                string name = atk != null ? atk.Name : e.Attack;
                Debug.Log($"[Boss] {boss}：「{name}」の予告（{e.Sec:0} 秒後。{(e.Cells != null ? $"範囲 {e.Cells.Count} マス" : e.Text)}）");
                ShowSeatNotice(PlayerType.Player1, e.Cells != null
                    ? $"【{boss}】 「{name}」が来る！ 赤く光るマスから {e.Sec:0} 秒以内に駒を逃がそう"
                    : $"【{boss}】 「{name}」が来る！ {e.Sec:0} 秒後から盤上を動き回る（踏まれた駒は攻撃を受ける）");
                break;
            }
            case BossEventType.Attack:
                Debug.Log($"[Boss] {boss}：「{BossAttacks.NameOf(e.Attack)}」（{e.Hits} 枚に当たった）");
                break;
            case BossEventType.KingHit:
                Debug.Log($"[Boss] 王将がボスの攻撃を受けた（{e.Hits} / {e.Max} 回目）");
                ShowSeatNotice(PlayerType.Player1, e.Hits >= e.Max
                    ? "【王将】 ボスの攻撃に耐えきれなかった……"
                    : $"【王将】 ボスの攻撃を受けた！ あと {e.Max - 1 - e.Hits} 回まで耐えられる（{e.Max} 回目でゲームオーバー）");
                break;
            case BossEventType.Damaged:
                Debug.Log($"[Boss] {boss}に {e.Amount} ダメージ（{RaidRules.HitLabel(e.Source)}。HP {e.Hp} / {e.MaxHp}）");
                break;
            case BossEventType.Defeated:
                Debug.Log($"[Boss] {boss}を討伐した！");
                break;
            case BossEventType.MinionSpawned:
                Debug.Log($"[Boss] ({e.At.x}, {e.At.y}) に小赤鬼が現れた");
                break;
        }
        BossEventRaised?.Invoke(e);
    }

    // ボスの AI と手下を 1 tick 進める（RealtimeTick から。停止世界の間は呼ばれない）
    void TickBoss()
    {
        if (!IsRaid || timeStop != null || bossHost == null) return;
        BossEngine<GamePiece>.Tick(bossHost);
    }

    // ------------------------------------------------------------
    //  ボスへのダメージ（Prototype の damage.js の damageBoss・hitBossCells、context.js の hitBoss）
    // ------------------------------------------------------------

    // ボスにダメージを与える。与えた（時間停止中は予定にした）ダメージを返す
    public int DamageBoss(BossHit hit, Vector2Int at)
    {
        BossState b = Boss;
        if (b == null || b.hp <= 0 || bossHost == null) return 0;
        if (timeStop != null)
        {
            // 時間停止中：ダメージの予定にして、時間が動き出してから与える
            int planned = RaidRules.BossDamage(b.Def, hit);
            if (planned <= 0) return 0;
            AddPending(new TimeStopPending { Kind = TimeStopKind.Boss, BossHit = hit, At = at }, $"{(b.Def != null ? b.Def.Name : "ボス")}に {planned} ダメージ");
            return planned;
        }
        return BossEngine<GamePiece>.Damage(bossHost, hit, at);
    }

    // マスの一覧がボスに重なれば、1 回だけダメージを与える（爆発・ファランクスの槍）。与えたダメージを返す
    public int HitBossCells(IEnumerable<Vector2Int> cells, BossHit hit)
    {
        if (Boss == null) return 0;
        Vector2Int? at = gameBoard.BossOverlap(cells);
        return at.HasValue ? DamageBoss(hit, at.Value) : 0;
    }

    private bool bossEffectActive;   // 必殺技・魔法の効果の途中か
    private bool bossEffectDone;     // その効果でもうボスに当てたか（1 回の効果につき 1 回だけ）
    private BossHit bossEffectHit;   // その効果のダメージの種類

    // 必殺技・魔法の効果の始まり・終わり（ExecuteSpecial・ExecuteSkill とその合戦将棋型の分から）
    void BeginBossEffect(BossHit hit)
    {
        bossEffectActive = true;
        bossEffectDone = false;
        bossEffectHit = hit;
    }

    void EndBossEffect()
    {
        bossEffectActive = false;
        bossEffectDone = false;
    }

    // 今の必殺技・魔法の範囲 cells がボスに重なれば、この効果で 1 回だけダメージを与える（Prototype の ctx.hitBoss）。当てたら true
    public bool EffectHitBoss(IEnumerable<Vector2Int> cells)
    {
        if (!bossEffectActive || bossEffectDone || Boss == null) return false;
        if (HitBossCells(cells, bossEffectHit) <= 0) return false;
        bossEffectDone = true;
        return true;
    }

    // 駒 p がボスのマス (x, y) へ入ろうとした（必殺技の突進・跳躍、押し出し、アンデッドの移動）：攻撃して元のマスに残る。
    // 必殺技・魔法の効果の途中（viaEffect）なら、その技のダメージで 1 回だけ。それ以外は駒の種類のダメージ
    public void AttackBossByStep(GamePiece p, int x, int y, bool viaEffect)
    {
        Vector2Int at = new Vector2Int(x, y);
        if (viaEffect && bossEffectActive) EffectHitBoss(new[] { at });
        else DamageBoss(BossHit.OfPiece(p.type, p.isPromoted), at);
    }

    // 駒 p の前方 1×3 マスにボスがいるか（Prototype の MagicKit.frontHasBoss）
    public bool FrontHasBoss(GamePiece p)
    {
        if (Boss == null || p == null) return false;
        return gameBoard.BossOverlap(MagicKit.FrontSquares(gameBoard, p)).HasValue;
    }

    // ------------------------------------------------------------
    //  勝敗
    // ------------------------------------------------------------

    // 討伐任務の勝敗が決まっていれば対局を終える（討伐成功＝先手の勝ち ／ 討伐失敗＝後手の勝ち）。終わったら true
    bool ResolveRaid()
    {
        RaidOutcome o = RaidOutcomeNow;
        if (o == RaidOutcome.None) return false;
        if (currentState == GameState.GameOver) return true;
        int ticks = match.Clock != null ? match.Clock.Time : 0;
        BossDef def = BossDefinition;
        GameResult.Raid = new RaidResultInfo
        {
            BossId = def != null ? def.Id : "",
            BossName = def != null ? def.Name : "ボス",
            Outcome = o,
            TimeTicks = ticks,
            BossHp = Boss.hp,
            BossMaxHp = Boss.maxHp,
            KingHits = Raid.kingHits,
        };
        Debug.Log(o == RaidOutcome.Cleared
            ? $"討伐成功！ {GameResult.Raid.BossName}を倒した（クリアタイム {RaidResults.TimeLabel(ticks)}）"
            : $"討伐失敗……（{(o == RaidOutcome.KingLost ? "王将が倒れた" : "王将がボスの攻撃に耐えきれなかった")}。{RaidResults.TimeLabel(ticks)}・ボスの残り HP {Boss.hp}）");
        EndGame(o == RaidOutcome.Cleared ? PlayerType.Player1 : PlayerType.Player2);
        return true;
    }

    // ボスの席（後手）の Controller：何もしない（その席はキーで操作できない。行動するのは BossEngine）
    sealed class BossSeatController : IRealtimeSeatController
    {
        public IEnumerable<MatchAction> RealtimeActions(GameSystem system, PlayerType seat) => null;
        public void OnRejected(GameSystem system, MatchAction action, string reason) { }
        public void OnAction(GameSystem system, MatchAction entry) { }
    }

    private readonly BossSeatController bossSeat = new BossSeatController();

    // ボスの席に、何もしない Controller を付ける（対局の始め・棋譜の再生が終わったあと）
    void AttachBossSeat()
    {
        if (IsRaid && IsRealtime && SeatController(PlayerType.Player2) == null) SetSeatController(PlayerType.Player2, bossSeat);
    }
}

// 討伐任務の結果（結果画面へ渡す。GameResult.Raid）
public class RaidResultInfo
{
    public string BossId;
    public string BossName;
    public RaidOutcome Outcome;
    public int TimeTicks;      // 対局の時間（tick。クリアタイム）
    public int BossHp;
    public int BossMaxHp;
    public int KingHits;

    public bool Cleared => Outcome == RaidOutcome.Cleared;
    public string TimeLabel => RaidResults.TimeLabel(TimeTicks);
}
