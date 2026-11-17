using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 討伐任務のボスの見た目（Prototype の src/view/bossView.js・BossPanel.js）。
//  ・盤上：ボスの絵（3×3 マス。動くときは滑らかに追う）・向きの印（金棒）・攻撃の予告の範囲（赤く光るマス）と
//    「技の名前 ＋ 残り秒数」・暴れ回りの次の 1 歩の行き先（橙のマスと矢印）・小赤鬼の次の行き先の矢印（動く 1 秒前から）・
//    ダメージの数字・攻撃が当たった瞬間の光
//  ・画面の左上（合戦将棋型の HUD の後手の席の位置）：ボスのパネル（名前・HP のバー・今の行動と残り秒数・王将の耐久・攻撃の一覧）
// 盤上の表示は、毎フレーム GameSystem.Boss（BossState）と駒の MinionData を読んで描く（局面の読み込み・棋譜の再生でもそのまま合う）。
// 一瞬の演出（ダメージ・攻撃の瞬間）だけ GameSystem.BossEventRaised で受ける。
// 討伐任務の対局で GameSystem が自動で追加する（シーンに置かなくてよい。絵は MagicSprites が実行時に作る）
public class BossView : MonoBehaviour
{
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private SpriteRenderer boardImage; // 描画順の基準（未指定なら "BoardImage" を探す）
    [SerializeField] private TMP_FontAsset font;         // 未指定なら NotoSansJP を Resources から読む
    [SerializeField] private int hudSortingOrder = 56;   // 合戦将棋型の HUD（55）の上

    private static readonly Color WarnColor = RuntimeUi.Hex(0xff1744);      // 予告の範囲
    private static readonly Color StepColor = RuntimeUi.Hex(0xff9100);      // 暴れ回りの次の 1 歩
    private static readonly Color MinionArrowColor = RuntimeUi.Hex(0xffab40);
    private static readonly Color PanelColor = new Color(0.10f, 0.03f, 0.04f, 0.82f);

    private Transform root;
    private int layer;
    private int baseOrder;

    private SpriteRenderer bossSr;
    private SpriteRenderer clubSr;
    private readonly SpriteRenderer[] cellSr = new SpriteRenderer[BoardCells.Size * BoardCells.Size];
    private TextMeshPro label;        // 予告の「技の名前 ＋ 残り秒数」
    private SpriteRenderer stepArrow; // 暴れ回りの次の 1 歩
    private readonly List<SpriteRenderer> minionArrows = new List<SpriteRenderer>();

    private Vector3 bossPos;
    private bool bossPlaced;
    private float shakeTime;
    private float flashTime;
    private float swingTime;
    private float attackFlashTime;
    private List<MatchSquare> attackFlashCells;

    private class Popup
    {
        public TextMeshPro Text;
        public Vector3 From;
        public float Age;
        public float Life;
    }
    private readonly List<Popup> popups = new List<Popup>();

    // HUD
    private Canvas canvas;
    private TextMeshProUGUI headerText;
    private TextMeshProUGUI hpText;
    private Image hpFill;
    private TextMeshProUGUI stateText;
    private TextMeshProUGUI kingText;
    private TextMeshProUGUI listText;
    private float nextHudRefresh;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (font == null) font = RuntimeUi.LoadFont();
        if (boardImage == null)
        {
            GameObject obj = GameObject.Find("BoardImage");
            if (obj != null) boardImage = obj.GetComponent<SpriteRenderer>();
        }
        if (gameSystem != null) gameSystem.BossEventRaised += OnBossEvent;
    }

    void OnDestroy()
    {
        if (gameSystem != null) gameSystem.BossEventRaised -= OnBossEvent;
        if (root != null) Destroy(root.gameObject);
    }

    // ------------------------------------------------------------
    //  作る
    // ------------------------------------------------------------

    void Build()
    {
        if (gameBoard == null || root != null) return;
        root = new GameObject("BossView").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = Vector3.zero;
        layer = boardImage != null ? boardImage.sortingLayerID : 0;
        baseOrder = boardImage != null ? boardImage.sortingOrder : 0;

        // 予告の範囲（盤のすぐ上・駒の下）
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                Vector2 at = GameHelper.CalcPanelLocation(x, y);
                SpriteRenderer sr = NewSprite($"Warn_{x}_{y}", MagicSprites.Square, baseOrder + 3);
                sr.transform.localPosition = new Vector3(at.x, at.y, -0.02f);
                sr.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
                sr.gameObject.SetActive(false);
                cellSr[GameHelper.CalcPanelNum(x, y)] = sr;
            }
        }

        // ボス（駒と同じ高さ。駒の下に隠れないように少し手前）
        bossSr = NewSprite("Boss", MagicSprites.Akaoni, 10);
        clubSr = NewSprite("Club", MagicSprites.Club, 11);
        stepArrow = NewSprite("StepArrow", MagicSprites.BossArrow, 12);
        stepArrow.gameObject.SetActive(false);

        GameObject labelObj = new GameObject("TelegraphLabel", typeof(RectTransform));
        labelObj.transform.SetParent(root, false);
        label = labelObj.AddComponent<TextMeshPro>();
        if (font != null) label.font = font;
        label.fontSize = 5.2f;
        label.fontStyle = FontStyles.Bold;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Overflow;
        label.color = Color.white;
        label.outlineColor = new Color32(60, 0, 0, 255);
        label.outlineWidth = 0.28f;
        label.sortingLayerID = layer;
        label.sortingOrder = 40;
        label.rectTransform.sizeDelta = new Vector2(6f, 1.2f);
        label.gameObject.SetActive(false);

        BuildHud();
    }

    SpriteRenderer NewSprite(string name, Sprite sprite, int order)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(root, false);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingLayerID = layer;
        sr.sortingOrder = order;
        return sr;
    }

    void BuildHud()
    {
        canvas = RuntimeUi.CreateCanvas("BossHudCanvas", transform, hudSortingOrder);
        RectTransform rootRt = (RectTransform)canvas.transform;
        Image panel = RuntimeUi.CreatePanel("BossPanel", rootRt, PanelColor, false);
        panel.raycastTarget = false;
        Outline line = panel.gameObject.AddComponent<Outline>();
        line.effectColor = new Color(1f, 0.25f, 0.3f, 0.8f);
        line.effectDistance = new Vector2(3f, -3f);
        RectTransform rt = panel.rectTransform;
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(16f, -80f);
        rt.sizeDelta = new Vector2(560f, 330f);

        headerText = Text("Header", rt, 28f, new Color(1f, 0.75f, 0.72f, 1f), 14f, 8f, 36f);

        // HP のバー
        Image barBg = RuntimeUi.CreatePanel("HpBar", rt, new Color(0f, 0f, 0f, 0.6f), false);
        barBg.raycastTarget = false;
        Band(barBg.rectTransform, 14f, 50f, 30f);
        Image fill = RuntimeUi.CreatePanel("Fill", barBg.rectTransform, RuntimeUi.Hex(0xff1744), false);
        fill.raycastTarget = false;
        fill.rectTransform.anchorMin = new Vector2(0f, 0f);
        fill.rectTransform.anchorMax = new Vector2(1f, 1f);
        fill.rectTransform.pivot = new Vector2(0f, 0.5f);
        fill.rectTransform.offsetMin = new Vector2(2f, 2f);
        fill.rectTransform.offsetMax = new Vector2(-2f, -2f);
        hpFill = fill;
        hpText = RuntimeUi.CreateText("HpText", barBg.rectTransform, "", 22f, Color.white, TextAlignmentOptions.Center, font);
        RuntimeUi.Stretch(hpText.rectTransform, Vector2.zero, Vector2.zero);

        stateText = Text("State", rt, 22f, new Color(1f, 0.92f, 0.7f, 1f), 14f, 88f, 30f);
        kingText = Text("King", rt, 22f, new Color(0.75f, 0.9f, 1f, 1f), 14f, 120f, 30f);
        listText = Text("Attacks", rt, 17f, new Color(0.92f, 0.92f, 0.95f, 1f), 14f, 156f, 168f);
        listText.fontStyle = FontStyles.Normal;
        listText.alignment = TextAlignmentOptions.TopLeft;
    }

    TextMeshProUGUI Text(string name, RectTransform parent, float size, Color color, float left, float top, float height)
    {
        TextMeshProUGUI t = RuntimeUi.CreateText(name, parent, "", size, color, TextAlignmentOptions.Left, font);
        t.richText = true;
        Band(t.rectTransform, left, top, height);
        return t;
    }

    // 親の上端に沿った帯
    static void Band(RectTransform rt, float margin, float top, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(margin, -top - height);
        rt.offsetMax = new Vector2(-margin, -top);
    }

    // ------------------------------------------------------------
    //  毎フレーム
    // ------------------------------------------------------------

    void Update()
    {
        BossState b = gameSystem != null ? gameSystem.Boss : null;
        if (b == null || !b.active)
        {
            if (root != null) root.gameObject.SetActive(false);
            if (canvas != null) canvas.gameObject.SetActive(false);
            return;
        }
        if (root == null) Build();
        if (root == null) return;
        if (!root.gameObject.activeSelf) root.gameObject.SetActive(true);
        if (!canvas.gameObject.activeSelf) canvas.gameObject.SetActive(true);

        float dt = Time.unscaledDeltaTime;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 7f);
        int now = gameSystem.Match != null && gameSystem.Match.Clock != null ? gameSystem.Match.Clock.Time : 0;
        BossAiState ai = b.ai ?? new BossAiState();
        int secLeft = Mathf.Max(0, Mathf.CeilToInt(MatchPolicy.TicksToSec(ai.until - now) - 1e-4f));

        UpdateBoss(b, ai, dt, pulse);
        UpdateCells(b, ai, pulse, dt);
        UpdateLabel(b, ai, secLeft, pulse);
        UpdateStepArrow(b, ai);
        UpdateMinionArrows();
        UpdatePopups(dt);

        if (Time.unscaledTime >= nextHudRefresh)
        {
            nextHudRefresh = Time.unscaledTime + 0.1f;
            RefreshHud(b, ai, secLeft);
        }
    }

    // ボスの占めるマスの中心（盤のローカル座標）
    static Vector3 CenterOf(BossState b, int x, int y)
    {
        Vector2 a = GameHelper.CalcPanelLocation(x, y);
        Vector2 c = GameHelper.CalcPanelLocation(x + b.h - 1, y + b.w - 1);
        return new Vector3((a.x + c.x) * 0.5f, (a.y + c.y) * 0.5f, -0.05f);
    }

    // 盤の (dx 段, dy 筋) の向きを、盤のローカル座標の向きにする（CalcPanelLocation は (段, 筋) → (筋, 段) に置く）
    static Vector2 LocalDir(int dx, int dy) => new Vector2(dy, dx);

    void UpdateBoss(BossState b, BossAiState ai, float dt, float pulse)
    {
        Vector3 target = CenterOf(b, b.x, b.y);
        if (!bossPlaced)
        {
            bossPos = target;
            bossPlaced = true;
        }
        bossPos = Vector3.MoveTowards(bossPos, target, dt * 9f);
        if ((bossPos - target).sqrMagnitude > 16f) bossPos = target; // 局面の読み込みなどで大きく飛んだ

        shakeTime = Mathf.Max(0f, shakeTime - dt);
        flashTime = Mathf.Max(0f, flashTime - dt);
        swingTime = Mathf.Max(0f, swingTime - dt);
        Vector3 shake = shakeTime > 0f
            ? new Vector3(Mathf.Sin(Time.unscaledTime * 90f), Mathf.Cos(Time.unscaledTime * 70f), 0f) * 0.06f * (shakeTime / 0.3f)
            : Vector3.zero;

        bool alive = b.hp > 0;
        bossSr.transform.localPosition = bossPos + shake;
        float breathe = 1f + 0.015f * Mathf.Sin(Time.unscaledTime * 2.2f);
        bossSr.transform.localScale = new Vector3(b.w / 3f * 0.97f * breathe, b.h / 3f * 0.97f * breathe, 1f);

        // 色：ダメージで白く光る・範囲を出さない予告（暴れ回り）と行動中は赤く光る・倒れたら消えていく
        Color c = Color.white;
        bool raging = alive && (ai.phase == BossAiState.Acting || (ai.phase == BossAiState.Telegraph && !ai.hasCells));
        if (raging) c = Color.Lerp(Color.white, new Color(1f, 0.25f, 0.2f, 1f), 0.35f + 0.45f * pulse);
        if (ai.phase == BossAiState.Resting && alive) c = new Color(0.72f, 0.72f, 0.8f, 1f); // 休憩中
        if (flashTime > 0f) c = Color.Lerp(c, new Color(1f, 1f, 0.6f, 1f), flashTime / 0.25f);
        float alpha = alive ? 1f : Mathf.MoveTowards(bossSr.color.a, 0f, dt * 1.2f);
        c.a = alpha;
        bossSr.color = c;

        // 向きの印（金棒）：向いている側の端に置く。攻撃の瞬間は振る
        Vector2 dir = LocalDir(b.facingX, b.facingY);
        if (dir.sqrMagnitude < 0.01f) dir = Vector2.down;
        dir.Normalize();
        float baseAngle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        float swing = swingTime > 0f ? Mathf.Lerp(-80f, 80f, 1f - swingTime / 0.3f) : -35f + 6f * Mathf.Sin(Time.unscaledTime * 2.2f);
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector3 hand = bossPos + (Vector3)(dir * (b.h * 0.5f - 0.35f)) + (Vector3)(side * (b.w * 0.5f - 0.3f)) + new Vector3(0f, 0f, -0.01f);
        clubSr.transform.localPosition = hand + shake;
        clubSr.transform.localRotation = Quaternion.Euler(0f, 0f, baseAngle + swing);
        clubSr.transform.localScale = new Vector3(1.25f, 1.25f, 1f);
        clubSr.color = new Color(1f, 1f, 1f, alpha);
    }

    // 予告の範囲（赤）・暴れ回りの次の 1 歩（橙）・攻撃が当たった瞬間（白っぽく光る）
    void UpdateCells(BossState b, BossAiState ai, float pulse, float dt)
    {
        foreach (SpriteRenderer sr in cellSr) sr.gameObject.SetActive(false);
        bool alive = b.hp > 0;

        if (alive && ai.phase == BossAiState.Telegraph && ai.hasCells)
        {
            foreach (MatchSquare c in ai.cells) Paint(c, WarnColor, 0.28f + 0.3f * pulse);
        }
        if (alive && ai.phase == BossAiState.Acting && ai.hasNext)
        {
            for (int dx = 0; dx < b.h; dx++)
            {
                for (int dy = 0; dy < b.w; dy++)
                {
                    MatchSquare c = new MatchSquare(ai.next.x + dx, ai.next.y + dy);
                    if (!b.Covers(c.x, c.y)) Paint(c, StepColor, 0.3f + 0.3f * pulse);
                }
            }
        }
        attackFlashTime = Mathf.Max(0f, attackFlashTime - dt);
        if (attackFlashTime > 0f && attackFlashCells != null)
        {
            foreach (MatchSquare c in attackFlashCells) Paint(c, new Color(1f, 0.9f, 0.6f, 1f), 0.85f * (attackFlashTime / 0.3f));
        }
    }

    void Paint(MatchSquare c, Color color, float alpha)
    {
        if (!BoardCells.InBounds(c.x, c.y)) return;
        SpriteRenderer sr = cellSr[GameHelper.CalcPanelNum(c.x, c.y)];
        sr.gameObject.SetActive(true);
        color.a = alpha;
        sr.color = color;
    }

    // 予告の「技の名前 ＋ 残り秒数」（範囲の中心。範囲を出さない予告・暴れ回りの間はボスの上）
    void UpdateLabel(BossState b, BossAiState ai, int secLeft, float pulse)
    {
        bool alive = b.hp > 0;
        BossAttackDef atk = BossAttacks.ById(ai.attack);
        string text = null;
        Vector3 at = bossPos;
        if (alive && ai.phase == BossAiState.Telegraph && atk != null)
        {
            text = $"{atk.Name}  <size=140%>{secLeft}</size>";
            if (ai.hasCells && ai.cells.Count > 0)
            {
                Vector2 sum = Vector2.zero;
                foreach (MatchSquare c in ai.cells) sum += GameHelper.CalcPanelLocation(c.x, c.y);
                at = sum / ai.cells.Count;
            }
        }
        else if (alive && ai.phase == BossAiState.Acting && atk != null)
        {
            text = $"{atk.Name}中  <size=140%>{secLeft}</size>";
        }
        if (text == null)
        {
            label.gameObject.SetActive(false);
            return;
        }
        label.gameObject.SetActive(true);
        label.text = text;
        label.color = Color.Lerp(Color.white, new Color(1f, 0.85f, 0.3f, 1f), pulse);
        label.transform.localPosition = new Vector3(Mathf.Clamp(at.x, -2.6f, 2.6f), at.y, -0.2f);
    }

    // 暴れ回りの次の 1 歩の矢印（ボスの中心から行き先の中心へ）
    void UpdateStepArrow(BossState b, BossAiState ai)
    {
        bool show = b.hp > 0 && ai.phase == BossAiState.Acting && ai.hasNext && (ai.next.x != b.x || ai.next.y != b.y);
        stepArrow.gameObject.SetActive(show);
        if (!show) return;
        Vector3 from = CenterOf(b, b.x, b.y);
        Vector3 to = CenterOf(b, ai.next.x, ai.next.y);
        PlaceArrow(stepArrow, from, to, 1.6f, StepColor);
    }

    // 小赤鬼の次の行き先の矢印（動く 1 秒前から）
    void UpdateMinionArrows()
    {
        int n = 0;
        if (gameSystem.IsInTurn)
        {
            foreach (GamePiece p in gameBoard.Pieces())
            {
                if (p == null || !p.minion.Active || !p.minion.Planned || !p.minion.HasTo) continue;
                if (n >= minionArrows.Count)
                {
                    SpriteRenderer sr = NewSprite($"MinionArrow_{n}", MagicSprites.BossArrow, 13);
                    minionArrows.Add(sr);
                }
                SpriteRenderer arrow = minionArrows[n++];
                arrow.gameObject.SetActive(true);
                Vector2 a = GameHelper.CalcPanelLocation(p.X, p.Y);
                Vector2 c = GameHelper.CalcPanelLocation(p.minion.ToX, p.minion.ToY);
                PlaceArrow(arrow, new Vector3(a.x, a.y, -0.1f), new Vector3(c.x, c.y, -0.1f), 0.8f, MinionArrowColor);
            }
        }
        for (int i = n; i < minionArrows.Count; i++) minionArrows[i].gameObject.SetActive(false);
    }

    static void PlaceArrow(SpriteRenderer sr, Vector3 from, Vector3 to, float size, Color color)
    {
        Vector3 d = to - from;
        float bob = 0.06f * Mathf.Sin(Time.unscaledTime * 8f);
        Vector3 dir = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.right;
        sr.transform.localPosition = (from + to) * 0.5f + dir * bob + new Vector3(0f, 0f, -0.12f);
        sr.transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
        sr.transform.localScale = new Vector3(size, size, 1f);
        color.a = 0.95f;
        sr.color = color;
    }

    // ------------------------------------------------------------
    //  一瞬の演出（GameSystem.BossEventRaised）
    // ------------------------------------------------------------

    void OnBossEvent(BossEvent e)
    {
        if (root == null) Build();
        if (root == null) return;
        switch (e.Type)
        {
            case BossEventType.Damaged:
                shakeTime = 0.3f;
                flashTime = 0.25f;
                AddPopup($"-{e.Amount}", e.At, new Color(1f, 0.95f, 0.4f, 1f), 7f);
                nextHudRefresh = 0f;
                break;
            case BossEventType.Attack:
                swingTime = 0.3f;
                attackFlashTime = 0.3f;
                attackFlashCells = e.Cells;
                break;
            case BossEventType.KingHit:
                AddPopup(e.Hits >= e.Max ? "王将 撃破" : $"王将 被弾 {e.Hits}/{e.Max - 1}", e.At, new Color(1f, 0.4f, 0.4f, 1f), 5f);
                nextHudRefresh = 0f;
                break;
            case BossEventType.HitPiece:
                if (e.Result == BossEvent.Guarded) AddPopup("盾", e.At, new Color(0.6f, 0.9f, 1f, 1f), 4.5f);
                break;
            case BossEventType.MinionSpawned:
                AddPopup("小赤鬼", e.At, new Color(1f, 0.6f, 0.4f, 1f), 3.6f);
                break;
            case BossEventType.Defeated:
                AddPopup("討伐！", new MatchSquare(gameSystem.Boss.x + 1, gameSystem.Boss.y + 1), new Color(1f, 0.9f, 0.3f, 1f), 9f);
                break;
        }
    }

    void AddPopup(string text, MatchSquare at, Color color, float size)
    {
        if (!BoardCells.InBounds(at.x, at.y)) return;
        GameObject obj = new GameObject("BossPopup", typeof(RectTransform));
        obj.transform.SetParent(root, false);
        TextMeshPro t = obj.AddComponent<TextMeshPro>();
        if (font != null) t.font = font;
        t.text = text;
        t.fontSize = size;
        t.fontStyle = FontStyles.Bold;
        t.alignment = TextAlignmentOptions.Center;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.color = color;
        t.outlineColor = new Color32(30, 0, 0, 255);
        t.outlineWidth = 0.3f;
        t.sortingLayerID = layer;
        t.sortingOrder = 45;
        t.rectTransform.sizeDelta = new Vector2(4f, 1f);
        Vector2 p = GameHelper.CalcPanelLocation(at.x, at.y);
        Popup popup = new Popup { Text = t, From = new Vector3(p.x, p.y + 0.2f, -0.3f), Life = 0.9f };
        t.transform.localPosition = popup.From;
        popups.Add(popup);
    }

    void UpdatePopups(float dt)
    {
        for (int i = popups.Count - 1; i >= 0; i--)
        {
            Popup p = popups[i];
            p.Age += dt;
            float t = Mathf.Clamp01(p.Age / p.Life);
            if (p.Text != null)
            {
                p.Text.transform.localPosition = p.From + new Vector3(0f, 0.7f * (1f - (1f - t) * (1f - t)), 0f);
                Color c = p.Text.color;
                c.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                p.Text.color = c;
            }
            if (t < 1f) continue;
            if (p.Text != null) Destroy(p.Text.gameObject);
            popups.RemoveAt(i);
        }
    }

    // ------------------------------------------------------------
    //  ボスのパネル
    // ------------------------------------------------------------

    void RefreshHud(BossState b, BossAiState ai, int secLeft)
    {
        BossDef def = b.Def;
        string name = def != null ? def.Name : "ボス";
        headerText.text = def != null && !string.IsNullOrEmpty(def.Title) ? $"【ボス】 {name}　<size=70%>{def.Title}</size>" : $"【ボス】 {name}";
        hpText.text = $"HP {Mathf.Max(0, b.hp)} / {b.maxHp}";
        float rate = b.maxHp > 0 ? Mathf.Clamp01((float)b.hp / b.maxHp) : 0f;
        hpFill.rectTransform.anchorMax = new Vector2(rate, 1f);
        hpFill.rectTransform.offsetMax = new Vector2(-2f, -2f);
        hpFill.color = Color.Lerp(RuntimeUi.Hex(0xffc400), RuntimeUi.Hex(0xff1744), rate);

        BossAttackDef atk = BossAttacks.ById(ai.attack);
        if (b.hp <= 0) stateText.text = "討伐成功！";
        else if (gameSystem.IsTimeStopped) stateText.text = "時間停止中（ボス・小赤鬼は止まっている）";
        else if (ai.phase == BossAiState.Telegraph && atk != null) stateText.text = $"<color=#FF8A80>「{atk.Name}」まで {secLeft} 秒</color>";
        else if (ai.phase == BossAiState.Acting && atk != null) stateText.text = $"<color=#FF8A80>「{atk.Name}」中　残り {secLeft} 秒</color>";
        else if (ai.phase == BossAiState.Resting) stateText.text = $"休憩中　あと {secLeft} 秒（攻撃のチャンス）";
        else stateText.text = $"次の攻撃の予告まで {secLeft} 秒";

        RaidState raid = gameSystem.Raid;
        int max = def != null ? def.KingMaxHits : 3;
        int hits = raid != null ? raid.kingHits : 0;
        int left = Mathf.Max(0, max - 1 - hits);
        StringBuilder hearts = new StringBuilder();
        for (int i = 0; i < max - 1; i++) hearts.Append(i < left ? "<color=#4FC3F7>◆</color>" : "<color=#555566>◇</color>");
        kingText.text = hits >= max ? "王将：倒れた……"
            : left > 0 ? $"王将の盾 {hearts}　あと {left} 回まで耐える（{max} 回目で負け）"
            : $"王将の盾 {hearts}　<color=#FF8A80>次にボスの攻撃を受けると負け</color>";

        StringBuilder sb = new StringBuilder();
        if (def != null)
        {
            foreach (BossAttackEntry entry in def.Attacks)
            {
                BossAttackDef a = BossAttacks.ById(entry.Id);
                if (a == null) continue;
                bool current = b.hp > 0 && ai.attack == a.Id && (ai.phase == BossAiState.Telegraph || ai.phase == BossAiState.Acting);
                string head = current ? "<color=#FF8A80>▶ " : "<color=#E0E0E8>・";
                sb.Append($"{head}<b>{a.Name}</b></color>　<color=#B0B0BC>{a.Summary}（予告 {a.TelegraphSec:0} 秒）</color>\n");
            }
        }
        sb.Append("<color=#B0B0BC>駒をボスのマスへ動かすと攻撃（駒は元のマスへ戻る）</color>");
        listText.text = sb.ToString();
    }
}
