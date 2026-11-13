using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 駒錬成のボタンと「狙撃」ボタン（Prototype の src/ui/PlayerPanel.js の forgeButtons と、狙撃手を選んだときのボタン）
//
//  ・錬成ボタン … レシピ（ForgeRecipeBook）ごとに 1 つ。手番のプレイヤーのエナジーが足りるときだけ押せる。
//                 押すと GameSystem.Forge（キーボードなら Z・X …）。錬成すると「火薬 錬成！」と出てボタンが弾む
//  ・狙撃ボタン … 自分の手番に狙撃手を選んでいるときだけ出る（GameSystem.BeginSnipe。キーボードなら G）。
//                 狙撃の対象を選んでいる間は「狙撃をやめる」になる
//  ・必殺技ボタン … 自分の手番に、オーラの出ている（必殺技を使える）自分の駒を選んでいるときだけ出る
//                 （GameSystem.BeginSpecial。キーボードなら E）。効果の対象がないときは押せない。
//                 対象を選ぶ技（天馬跳躍）の対象を選んでいる間は「必殺技をやめる」になる
//
// スキルボタンのパネル（5 枠）は埋まっているので、画面の右上に実行時にパネルを作って並べる。
// シーンに置かなくても GameSystem が自動で追加する
public class ForgePanelView : MonoBehaviour
{
    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private Canvas canvas;      // 未指定ならスキルボタンのある Canvas
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む

    [Header("Layout (Canvas の基準解像度 1920×1080 でのピクセル)")]
    [SerializeField] private Vector2 margin = new Vector2(20f, 20f); // 画面の右上からの距離
    [SerializeField] private float panelWidth = 320f;
    [SerializeField] private float buttonHeight = 84f;

    private static readonly Color PanelColor = new Color(0.10f, 0.05f, 0.03f, 0.82f);
    private static readonly Color PanelLineColor = Hex(0xff9100);
    private static readonly Color ForgeButtonColor = Hex(0xbf360c);
    private static readonly Color SnipeButtonColor = Hex(0xc62828);
    private static readonly Color CancelButtonColor = Hex(0x546e7a);

    private class ForgeButton
    {
        public ForgeRecipe recipe;
        public Button button;
        public RectTransform rect;
        public TextMeshProUGUI label;
        public int index;
        public string shownKey;
    }

    private readonly List<ForgeButton> forgeButtons = new List<ForgeButton>();
    private RectTransform panel;
    private Button snipeButton;
    private Image snipeImage;
    private TextMeshProUGUI snipeLabel;
    private Button specialButton;
    private Image specialImage;
    private TextMeshProUGUI specialLabel;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        if (canvas == null) canvas = FindUICanvas();
        if (gameSystem == null || canvas == null) return;

        Build();
        gameSystem.Forged += OnForged;
    }

    void OnDestroy()
    {
        if (gameSystem != null) gameSystem.Forged -= OnForged;
    }

    void Update()
    {
        if (gameSystem == null || panel == null) return;

        for (int i = 0; i < forgeButtons.Count; i++)
        {
            ForgeButton fb = forgeButtons[i];
            fb.button.interactable = gameSystem.CanForge(fb.recipe);
            // 錬成のキーの表示（合戦将棋型は操作中の席のキー：先手 Z・X ／ 後手 N・M）
            string key = gameSystem.FocusForgeKeyLabel(fb.index);
            if (fb.label != null && key != fb.shownKey)
            {
                fb.shownKey = key;
                fb.label.text = $"{fb.recipe.label}を錬成" + (string.IsNullOrEmpty(key) ? "" : $"  [{key}]");
            }
        }

        bool aiming = gameSystem.IsSelectingSnipeTarget;
        bool show = aiming || gameSystem.CanBeginSnipe;
        if (snipeButton.gameObject.activeSelf != show) snipeButton.gameObject.SetActive(show);
        if (show)
        {
            SeatKeys keys = gameSystem.KeysOf(gameSystem.FocusSide); // 合戦将棋型は操作中の席のキー
            snipeLabel.text = aiming ? $"狙撃をやめる  [{SeatKeys.Label(keys.Cancel)}]" : $"狙撃  [{SeatKeys.Label(keys.Snipe)}]";
            snipeImage.color = aiming ? CancelButtonColor : SnipeButtonColor;
        }

        bool choosing = gameSystem.IsSelectingSpecialTarget;
        bool showSpecial = choosing || gameSystem.CanBeginSpecial;
        if (specialButton.gameObject.activeSelf != showSpecial) specialButton.gameObject.SetActive(showSpecial);
        if (showSpecial)
        {
            SpecialMove move = gameSystem.SelectedSpecial;
            string moveName = move != null ? move.moveName : "";
            bool usable = choosing || gameSystem.CanUseSelectedSpecial;
            specialButton.interactable = usable;
            SeatKeys keys = gameSystem.KeysOf(gameSystem.FocusSide);
            specialLabel.text = choosing ? $"必殺技をやめる  [{SeatKeys.Label(keys.Cancel)}]"
                : usable ? $"必殺技「{moveName}」  [{SeatKeys.Label(keys.Special)}]" : $"「{moveName}」 対象なし";
            specialImage.color = choosing ? CancelButtonColor : SpecialButtonColor(move);
        }
    }

    // 必殺技ボタンの色（技の色を少し暗くして、白い文字を読みやすくする）
    static Color SpecialButtonColor(SpecialMove move)
    {
        Color c = move != null ? move.color : PanelLineColor;
        return Color.Lerp(c, Color.black, 0.45f);
    }

    // ------------------------------------------------------------
    //  ボタンを押したとき・錬成したとき
    // ------------------------------------------------------------
    void OnSnipeClicked()
    {
        if (gameSystem.IsSelectingSnipeTarget) gameSystem.CancelSnipe();
        else gameSystem.BeginSnipe();
    }

    void OnSpecialClicked()
    {
        if (gameSystem.IsSelectingSpecialTarget) gameSystem.CancelTargetSelection();
        else gameSystem.BeginSpecial();
    }

    void OnForged(PlayerType owner, ForgeRecipe recipe)
    {
        ForgeButton fb = forgeButtons.Find(b => b.recipe == recipe);
        if (fb == null) return;

        // ボタンが弾み、上に「○○ 錬成！」
        fb.rect.DOKill(true);
        fb.rect.localScale = Vector3.one;
        fb.rect.DOPunchScale(Vector3.one * 0.12f, 0.35f, 8, 0.6f).SetLink(fb.rect.gameObject);
        PopLabel($"{recipe.label} 錬成！", fb.rect);

        PieceFx fx = FindAnyObjectByType<PieceFx>();
        if (fx != null) fx.Shake(4f, 0.2f);
    }

    // ボタンの上に出て、上へ流れて消える文字
    void PopLabel(string text, RectTransform anchor)
    {
        GameObject obj = new GameObject("ForgePop", typeof(RectTransform));
        RectTransform rt = obj.GetComponent<RectTransform>();
        rt.SetParent(canvas.transform, false);
        rt.position = anchor.position;
        rt.sizeDelta = new Vector2(panelWidth, 50f);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 34f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(Color.white, Color.white, Hex(0xff9100), Hex(0xff9100));
        tmp.outlineWidth = 0.2f;
        tmp.outlineColor = Hex(0x2a0a00);
        tmp.raycastTarget = false;

        rt.localScale = Vector3.one * 0.3f;
        Vector2 start = rt.anchoredPosition;
        Sequence seq = DOTween.Sequence().SetLink(obj);
        seq.Insert(0f, rt.DOScale(1.2f, 0.16f).SetEase(Ease.OutBack, 3f));
        seq.Insert(0.16f, rt.DOScale(1f, 0.12f));
        seq.Insert(0.1f, rt.DOAnchorPos(start + new Vector2(0f, 50f), 0.9f).SetEase(Ease.OutQuad));
        seq.Insert(0.7f, DOVirtual.Float(1f, 0f, 0.3f, a => tmp.alpha = a));
        seq.OnComplete(() => Destroy(obj));
    }

    // ------------------------------------------------------------
    //  UI を作る
    // ------------------------------------------------------------
    void Build()
    {
        GameObject panelObj = new GameObject("ForgePanel", typeof(RectTransform));
        panel = panelObj.GetComponent<RectTransform>();
        panel.SetParent(canvas.transform, false);
        panel.anchorMin = new Vector2(1f, 1f);
        panel.anchorMax = new Vector2(1f, 1f);
        panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-margin.x, -margin.y);
        panel.sizeDelta = new Vector2(panelWidth, 0f);

        Image bg = panelObj.AddComponent<Image>();
        bg.color = PanelColor;
        Outline line = panelObj.AddComponent<Outline>();
        line.effectColor = WithAlpha(PanelLineColor, 0.7f);
        line.effectDistance = new Vector2(3f, -3f);

        VerticalLayoutGroup layout = panelObj.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 10, 14);
        layout.spacing = 10f;
        layout.childAlignment = TextAnchor.UpperCenter;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        ContentSizeFitter fitter = panelObj.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CreateText("Title", panel, "駒錬成", 30f, Hex(0xffd180), TextAlignmentOptions.Center);
        AddLayout(title.gameObject, 40f);

        IReadOnlyList<ForgeRecipe> recipes = gameSystem.ForgeRecipes;
        for (int i = 0; i < recipes.Count; i++)
        {
            ForgeRecipe recipe = recipes[i];
            if (recipe == null) continue;
            ForgeButton fb = CreateForgeButton(recipe, GameSystem.ForgeKeyLabel(i));
            fb.index = i;
            fb.shownKey = GameSystem.ForgeKeyLabel(i);
            forgeButtons.Add(fb);
        }
        if (forgeButtons.Count == 0)
        {
            TextMeshProUGUI none = CreateText("NoRecipe", panel, "（錬成できる駒がありません）", 22f, Color.white, TextAlignmentOptions.Center);
            AddLayout(none.gameObject, 40f);
        }

        // 狙撃ボタン（狙撃手を選んでいるときだけ出る）
        Image image;
        snipeButton = CreateButton("SnipeButton", panel, SnipeButtonColor, out image);
        snipeImage = image;
        AddLayout(snipeButton.gameObject, buttonHeight * 0.8f);
        snipeLabel = CreateText("Label", snipeButton.transform, "狙撃  [G]", 30f, Color.white, TextAlignmentOptions.Center);
        Stretch(snipeLabel.rectTransform, Vector2.zero, Vector2.zero);
        snipeButton.onClick.AddListener(OnSnipeClicked);
        snipeButton.gameObject.SetActive(false);

        // 必殺技ボタン（オーラの出ている自分の駒を選んでいるときだけ出る）
        specialButton = CreateButton("SpecialButton", panel, SpecialButtonColor(null), out image);
        specialImage = image;
        AddLayout(specialButton.gameObject, buttonHeight * 0.8f);
        specialLabel = CreateText("Label", specialButton.transform, "必殺技  [E]", 28f, Color.white, TextAlignmentOptions.Center);
        Stretch(specialLabel.rectTransform, Vector2.zero, Vector2.zero);
        specialButton.onClick.AddListener(OnSpecialClicked);
        specialButton.gameObject.SetActive(false);
    }

    ForgeButton CreateForgeButton(ForgeRecipe recipe, string key)
    {
        Image image;
        Button button = CreateButton($"Forge_{recipe.pieceType}", panel, ForgeButtonColor, out image);
        AddLayout(button.gameObject, buttonHeight);
        RectTransform rt = button.GetComponent<RectTransform>();

        // 左：駒の画像
        float iconSize = buttonHeight - 16f;
        if (recipe.icon != null)
        {
            GameObject iconObj = new GameObject("Icon", typeof(RectTransform));
            RectTransform irt = iconObj.GetComponent<RectTransform>();
            irt.SetParent(rt, false);
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);
            irt.anchoredPosition = new Vector2(10f, 0f);
            irt.sizeDelta = new Vector2(iconSize, iconSize);
            Image icon = iconObj.AddComponent<Image>();
            icon.sprite = recipe.icon;
            icon.preserveAspect = true;
            icon.raycastTarget = false;
        }

        // 中央：「火薬を錬成 [Z]」／右：コスト
        float left = recipe.icon != null ? iconSize + 18f : 14f;
        string keyLabel = string.IsNullOrEmpty(key) ? "" : $"  [{key}]";
        TextMeshProUGUI label = CreateText("Label", rt, $"{recipe.label}を錬成{keyLabel}", 26f, Color.white, TextAlignmentOptions.Left);
        Stretch(label.rectTransform, new Vector2(left, 0f), new Vector2(-84f, 0f));
        TextMeshProUGUI cost = CreateText("Cost", rt, $"{recipe.cost} Pt", 26f, Hex(0xffe082), TextAlignmentOptions.Right);
        cost.rectTransform.anchorMin = new Vector2(1f, 0f);
        cost.rectTransform.anchorMax = new Vector2(1f, 1f);
        cost.rectTransform.pivot = new Vector2(1f, 0.5f);
        cost.rectTransform.sizeDelta = new Vector2(80f, 0f);
        cost.rectTransform.anchoredPosition = new Vector2(-12f, 0f);

        button.onClick.AddListener(() => gameSystem.Forge(recipe));
        return new ForgeButton { recipe = recipe, button = button, rect = rt, label = label };
    }

    Button CreateButton(string name, Transform parent, Color color, out Image image)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        image = obj.AddComponent<Image>();
        image.color = color;
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        button.colors = colors;
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);
        return button;
    }

    TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    static void AddLayout(GameObject obj, float height)
    {
        LayoutElement le = obj.AddComponent<LayoutElement>();
        le.minHeight = height;
        le.preferredHeight = height;
    }

    // 親いっぱいに広げる（offsetMin = 左下の余白、offsetMax = 右上の余白（負の値で内側））
    static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // スキルボタンのある Canvas（なければシーンの Canvas）
    Canvas FindUICanvas()
    {
        if (gameSystem != null)
        {
            foreach (Button button in gameSystem.SkillSlotButtons)
            {
                if (button == null) continue;
                Canvas c = button.GetComponentInParent<Canvas>();
                if (c != null) return c.rootCanvas;
            }
        }
        if (HandUIManager.Instance != null)
        {
            Canvas c = HandUIManager.Instance.GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }
        return FindAnyObjectByType<Canvas>();
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
