using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 超常魔法の解放条件の達成状況（Prototype の src/ui/PlayerPanel.js の refreshUnlockStatus・flashMagicButton と、
// Game.js の showTranscendentUnlock のボタンの「解放！」）。
//
// 超常魔法のボタン（スキルボタンの最後の枠）の下に、手番のプレイヤーの状況を出す（超常魔法を編成しているときだけ）：
//  未解放   … 「超常魔法の解放条件」／「侵入 ○・×」「駒数 自分 N vs 相手 M」（満たしている項目は緑）
//  解放済み … 「★ 超常魔法：解放済み ★」
//  使用済み … 「超常魔法：使用済み」
// 解放されたとき（GameSystem.TranscendentUnlocked）は、ボタンが光って弾み、上に「解放！」が出る。
//
// スキルボタンの列（GridLayoutGroup）は画面の高さいっぱいに並んでいるので、下に表示を入れる分だけボタンの高さを少し詰めて上に寄せる。
// シーンに置かなくても GameSystem が自動で追加する
public class TranscendentStatusView : MonoBehaviour
{
    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む

    [Header("Layout (Canvas の基準解像度 1920×1080 でのピクセル)")]
    [SerializeField] private float fontSize = 22f;
    [SerializeField] private float gap = 30f;           // ボタンの下端からの距離（スキル名の文字がボタンの下にはみ出している分）
    [SerializeField] private float reservedHeight = 72f; // ボタンの列の下に空ける高さ（2 行分）
    [SerializeField] private float maxWidth = 380f;

    private const string OnColor = "#69f0ae";
    private const string OffColor = "#9e9e9e";
    private const string RankColor = "#ea80fc"; // 超常魔法の枠の色（SkillRanks.ColorOf(Transcendent)）

    private Button button;            // 超常魔法のボタン
    private RectTransform buttonRect;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform panel;      // 背景（文字の大きさに合わせる）
    private TextMeshProUGUI label;
    private string shownText;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        if (gameSystem == null) return;

        button = FindTranscendentButton();
        if (button == null) return;
        buttonRect = button.GetComponent<RectTransform>();
        Canvas c = button.GetComponentInParent<Canvas>();
        canvas = c != null ? c.rootCanvas : null;
        if (canvas == null) return;
        canvasRect = canvas.GetComponent<RectTransform>();

        MakeRoomBelowButtons();
        Build();
        gameSystem.TranscendentUnlocked += OnUnlocked;
    }

    void OnDestroy()
    {
        if (gameSystem != null) gameSystem.TranscendentUnlocked -= OnUnlocked;
    }

    // スキルボタンのうち、超常魔法の枠（SkillRanks.FirstSlot(Transcendent)）のボタン
    Button FindTranscendentButton()
    {
        int slot = SkillRanks.FirstSlot(SkillRank.Transcendent);
        int i = 0;
        foreach (Button b in gameSystem.SkillSlotButtons)
        {
            if (i == slot) return b;
            i++;
        }
        return null;
    }

    // ボタンの列の下に表示の場所を空ける：足りなければボタンの高さを詰め、列を上に寄せる
    void MakeRoomBelowButtons()
    {
        Transform parent = buttonRect.parent;
        GridLayoutGroup grid = parent != null ? parent.GetComponent<GridLayoutGroup>() : null;
        RectTransform pr = parent as RectTransform;
        if (grid == null || pr == null) return;

        int n = 0;
        foreach (Transform child in parent)
        {
            LayoutElement le = child.GetComponent<LayoutElement>();
            if (le == null || !le.ignoreLayout) n++;
        }
        if (n == 0) return;

        float height = pr.rect.height;
        float pad = grid.padding.top + grid.padding.bottom;
        float need = reservedHeight + gap;
        float total = pad + n * grid.cellSize.y + (n - 1) * grid.spacing.y;
        if (height - total < need)
        {
            float cell = (height - need - pad - (n - 1) * grid.spacing.y) / n;
            cell = Mathf.Max(cell, grid.cellSize.y * 0.75f); // 詰めすぎない（入りきらなければ下の表示が重なる）
            grid.cellSize = new Vector2(grid.cellSize.x, cell);
        }
        grid.childAlignment = TextAnchor.UpperCenter;
    }

    void Build()
    {
        GameObject obj = new GameObject("TranscendentStatus", typeof(RectTransform));
        panel = obj.GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false);
        panel.anchorMin = new Vector2(0.5f, 0.5f);
        panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 1f);
        Image bg = obj.AddComponent<Image>();
        bg.color = new Color(0.08f, 0.02f, 0.10f, 0.78f);
        bg.raycastTarget = false;
        Outline line = obj.AddComponent<Outline>();
        line.effectColor = new Color(0xea / 255f, 0x80 / 255f, 0xfc / 255f, 0.6f);
        line.effectDistance = new Vector2(2f, -2f);

        GameObject textObj = new GameObject("Label", typeof(RectTransform));
        RectTransform tr = textObj.GetComponent<RectTransform>();
        tr.SetParent(panel, false);
        tr.anchorMin = Vector2.zero;
        tr.anchorMax = Vector2.one;
        tr.offsetMin = new Vector2(8f, 4f);
        tr.offsetMax = new Vector2(-8f, -4f);
        label = textObj.AddComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.fontSize = fontSize;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.Center;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Overflow;
        label.richText = true;
        label.raycastTarget = false;
        obj.SetActive(false);
    }

    void LateUpdate()
    {
        if (panel == null || gameSystem == null) return;

        Player current = gameSystem.CurrentPlayer;
        bool show = gameSystem.IsInTurn && current != null && button.gameObject.activeInHierarchy
                    && gameSystem.GetTranscendentSkill(current.PlayerType) != null;
        if (panel.gameObject.activeSelf != show) panel.gameObject.SetActive(show);
        if (!show) return;

        string text = StatusText(current.PlayerType);
        if (text != shownText)
        {
            shownText = text;
            label.text = text;
            Vector2 pref = label.GetPreferredValues(text, maxWidth - 16f, 0f);
            panel.sizeDelta = new Vector2(Mathf.Min(maxWidth, pref.x + 16f), pref.y + 8f);
        }

        // ボタンの下端の中央から gap だけ下（画面の下にはみ出すなら、はみ出さないところまで上げる）
        Vector3[] corners = new Vector3[4];
        buttonRect.GetWorldCorners(corners);
        Vector3 bottom = (corners[0] + corners[3]) * 0.5f;
        Vector2 local = canvasRect.InverseTransformPoint(bottom);
        float y = local.y - gap;
        float minY = canvasRect.rect.yMin + 4f + panel.sizeDelta.y;
        panel.anchoredPosition = new Vector2(local.x, Mathf.Max(y, minY));
    }

    // 手番のプレイヤー owner の状況（Prototype の refreshUnlockStatus と同じ文言）
    string StatusText(PlayerType owner)
    {
        if (gameSystem.IsTranscendentUsed(owner)) return $"<color={OffColor}>超常魔法：使用済み</color>";
        if (gameSystem.IsTranscendentUnlocked(owner)) return $"<color={RankColor}>★ 超常魔法：解放済み ★</color>";
        TranscendentProgress p = gameSystem.GetTranscendentProgress(owner);
        string invade = $"<color={(p.Invaded ? OnColor : OffColor)}>侵入 {(p.Invaded ? "○" : "×")}</color>";
        string count = $"<color={(p.MoreEnemies ? OnColor : OffColor)}>駒数 自分 {p.Mine} vs 相手 {p.Theirs}</color>";
        return $"<color={RankColor}>超常魔法の解放条件</color>\n{invade}　{count}";
    }

    // 解放された：手番のプレイヤーのボタンなら、光って弾み、上に「解放！」
    void OnUnlocked(PlayerType owner)
    {
        Player current = gameSystem.CurrentPlayer;
        if (current == null || current.PlayerType != owner || !button.gameObject.activeInHierarchy) return;

        Color color = SkillRanks.ColorOf(SkillRank.Transcendent);
        buttonRect.DOKill(true);
        buttonRect.localScale = Vector3.one;
        buttonRect.DOPunchScale(Vector3.one * 0.15f, 0.5f, 8, 0.6f).SetLink(button.gameObject);

        // ボタンに重ねた光（白 → 超常魔法の色で 3 回明滅して消える）
        GameObject glowObj = new GameObject("UnlockGlow", typeof(RectTransform));
        RectTransform gr = glowObj.GetComponent<RectTransform>();
        gr.SetParent(buttonRect, false);
        gr.anchorMin = Vector2.zero;
        gr.anchorMax = Vector2.one;
        gr.offsetMin = new Vector2(-6f, -6f);
        gr.offsetMax = new Vector2(6f, 6f);
        LayoutElement le = glowObj.AddComponent<LayoutElement>();
        le.ignoreLayout = true;
        Image glow = glowObj.AddComponent<Image>();
        glow.raycastTarget = false;
        glow.color = new Color(color.r, color.g, color.b, 0f);
        DOTween.Sequence().SetLink(glowObj)
            .Append(glow.DOFade(0.65f, 0.12f).SetLoops(6, LoopType.Yoyo))
            .Append(glow.DOFade(0f, 0.3f))
            .OnComplete(() => Destroy(glowObj));

        PopLabel("解放！", color);
    }

    // ボタンの上に出て、上へ流れて消える文字
    void PopLabel(string text, Color color)
    {
        GameObject obj = new GameObject("UnlockPop", typeof(RectTransform));
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.position = buttonRect.position;
        rt.sizeDelta = new Vector2(300f, 60f);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 44f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(Color.white, Color.white, color, color);
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = new Color32(0x2a, 0x00, 0x30, 0xff);
        tmp.raycastTarget = false;

        rt.localScale = Vector3.one * 0.3f;
        Vector2 start = rt.anchoredPosition;
        Sequence seq = DOTween.Sequence().SetLink(obj);
        seq.Insert(0f, rt.DOScale(1.25f, 0.16f).SetEase(Ease.OutBack, 3f));
        seq.Insert(0.16f, rt.DOScale(1f, 0.12f));
        seq.Insert(0.1f, rt.DOAnchorPos(start + new Vector2(0f, 70f), 1f).SetEase(Ease.OutQuad));
        seq.Insert(0.8f, DOVirtual.Float(1f, 0f, 0.3f, a => tmp.alpha = a));
        seq.OnComplete(() => Destroy(obj));
    }
}
