using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using DG.Tweening;
using TMPro;

// ルール説明のオーバーレイ（Prototype の src/scenes/RulesScene.js の showRules）。
//
//  ・画面の左下に「ルール [Tab]」ボタン。押す（Tab キー）と、対局の上にルール説明を重ねて開く
//  ・上のタブでページを切り替え（← → / A D でも可）。長いページはスクロール（ホイール / ↑ ↓ / W S）
//  ・閉じる：Tab・Esc・Space・「閉じる」ボタン・枠の外をクリック
//  ・開いている間は対局の操作（カーソル・決定・取消・各キー）を受け付けない（IsBlockingInput）
//
// 中身は RulesBook（プレーンなクラス）が定義データから作る。開くたびに作り直すので、
// 錬成のレシピ・必殺技・魔法（Resources/SkillDatabase）を足すと自動で説明にも出る。
// シーンに置かなくても GameSystem が自動で追加する
public class RulesOverlayView : MonoBehaviour
{
    [Header("References (未指定なら自動で探す・読む)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private SkillDatabase skillDatabase; // 未指定なら Resources/SkillDatabase
    [SerializeField] private TMP_FontAsset font;          // 未指定なら NotoSansJP を Resources から読む

    [Header("Keys")]
    [SerializeField] private Key toggleKey = Key.Tab;

    [Header("Layout (基準解像度 1920×1080 でのピクセル)")]
    [SerializeField] private Vector2 buttonAnchor = new Vector2(0f, 0f);    // ボタンを置く画面の角（0,0 = 左下）
    [SerializeField] private Vector2 buttonOffset = new Vector2(20f, 20f);  // 角からの距離（内側が正）
    [SerializeField] private Vector2 buttonSize = new Vector2(230f, 64f);
    [SerializeField] private Vector2 boxSize = new Vector2(1500f, 960f);
    [SerializeField] private float bodyFontSize = 27f;
    [SerializeField] private float keyScrollSpeed = 900f;                    // ↑ ↓ キーでのスクロールの速さ（ピクセル/秒）

    [Header("Sorting")]
    [SerializeField] private int buttonSortingOrder = 60;    // カットイン（500）より奥
    [SerializeField] private int overlaySortingOrder = 1000; // ほかの UI・カットインより手前

    // 開いている間（と閉じた直後の 1 フレーム）は、対局の操作を止める。
    // 閉じたキー（Space など）が、同じフレーム・次のフレームで対局の取消として読まれないようにする
    private static RulesOverlayView active;
    public static bool IsBlockingInput
    {
        get
        {
            if (active == null) return false;
            return active.isOpen || Time.frameCount <= active.closedFrame + 1;
        }
    }

    private static readonly Color DimColor = new Color(0f, 0f, 0f, 0.75f);
    private static readonly Color BoxColor = new Color(0x1a / 255f, 0x0f / 255f, 0x26 / 255f, 0.97f);
    private static readonly Color BoxLineColor = Hex(0xffd54a);
    private static readonly Color TabColor = Hex(0x37474f);
    private static readonly Color TabSelectedColor = Hex(0x8e24aa);
    private const string HeadingColor = "#ffd54a";
    private const string BadgeColor = "#ff80ab";
    private const string BodyColor = "#f5f5f5";

    private Canvas buttonCanvas;
    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private RectTransform tabRow;
    private ScrollRect scroll;
    private TextMeshProUGUI body;
    private readonly List<Image> tabImages = new List<Image>();
    private readonly List<GameObject> tabObjects = new List<GameObject>();

    private List<RulesPage> pages = new List<RulesPage>();
    private int pageIndex;
    private bool isOpen;
    private int closedFrame = -10;

    public bool IsOpen => isOpen;

    void Awake()
    {
        active = this;
    }

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (skillDatabase == null) skillDatabase = SkillDatabase.LoadDefault();
        if (skillDatabase == null) Debug.LogWarning("魔法の一覧（Resources/SkillDatabase）が見つからないので、ルール説明の魔法の一覧は空になります");
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");

        EnsureEventSystem();
        BuildButton();
        BuildOverlay();
        overlayCanvas.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (active == this) active = null;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || overlayCanvas == null) return;

        if (!isOpen)
        {
            if (keyboard[toggleKey].wasPressedThisFrame) Open();
            return;
        }

        if (keyboard[toggleKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
        {
            Close();
            return;
        }
        if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) ShowPage(pageIndex - 1);
        else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) ShowPage(pageIndex + 1);

        float dir = 0f;
        if (keyboard.upArrowKey.isPressed || keyboard.wKey.isPressed) dir += 1f;
        if (keyboard.downArrowKey.isPressed || keyboard.sKey.isPressed) dir -= 1f;
        if (dir != 0f) ScrollBy(dir * keyScrollSpeed * Time.unscaledDeltaTime);
    }

    // ------------------------------------------------------------
    //  開く・閉じる・ページ
    // ------------------------------------------------------------
    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isOpen || overlayCanvas == null) return;
        isOpen = true;

        // 開くたびに定義データから作り直す（この対局の編成の印も付ける）
        overlayCanvas.gameObject.SetActive(true);
        pages = RulesBook.Build(CollectSources());
        RebuildTabs();
        ShowPage(Mathf.Clamp(pageIndex, 0, pages.Count - 1));

        overlayGroup.DOKill();
        overlayGroup.alpha = 0f;
        overlayGroup.DOFade(1f, 0.2f).SetUpdate(true).SetLink(overlayCanvas.gameObject);
        ClearUiSelection();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        closedFrame = Time.frameCount;
        overlayGroup.DOKill();
        overlayGroup.DOFade(0f, 0.15f).SetUpdate(true).SetLink(overlayCanvas.gameObject)
            .OnComplete(() => { if (!isOpen) overlayCanvas.gameObject.SetActive(false); });
        ClearUiSelection();
    }

    void ShowPage(int index)
    {
        if (pages.Count == 0) return;
        pageIndex = (index % pages.Count + pages.Count) % pages.Count;
        body.text = ToRichText(pages[pageIndex]);
        for (int i = 0; i < tabImages.Count; i++)
        {
            tabImages[i].color = i == pageIndex ? TabSelectedColor : TabColor;
        }
        // ページの先頭から（本文は上端を基準に置いているので、位置 0 が先頭）
        scroll.StopMovement();
        scroll.content.anchoredPosition = Vector2.zero;
    }

    void ScrollBy(float pixels)
    {
        RectTransform content = scroll.content;
        RectTransform viewport = scroll.viewport;
        float overflow = content.rect.height - viewport.rect.height;
        if (overflow <= 0f) return;
        scroll.verticalNormalizedPosition = Mathf.Clamp01(scroll.verticalNormalizedPosition + pixels / overflow);
    }

    RulesSources CollectSources()
    {
        RulesSources src = new RulesSources
        {
            ForgeKeyLabel = GameSystem.ForgeKeyLabel,
        };
        if (gameSystem != null)
        {
            src.ForgeRecipes = gameSystem.ForgeRecipes;
            src.Specials = gameSystem.SpecialMoves;
            src.Player1Deck = DeckSkills(gameSystem.GetSkillDeck(PlayerType.Player1));
            src.Player2Deck = DeckSkills(gameSystem.GetSkillDeck(PlayerType.Player2));
        }
        else
        {
            ForgeRecipeBook book = ForgeRecipeBook.LoadDefault();
            if (book != null) src.ForgeRecipes = book.recipes;
            src.Specials = SpecialMoveBook.LoadDefault();
        }
        if (skillDatabase != null) src.Magics = skillDatabase.allSkills;
        return src;
    }

    static List<SkillDefinition> DeckSkills(SkillDeck deck)
    {
        List<SkillDefinition> list = new List<SkillDefinition>();
        if (deck == null || deck.skills == null) return list;
        foreach (SkillDefinition s in deck.skills)
        {
            if (s != null) list.Add(s);
        }
        return list;
    }

    // RulesPage を TextMeshPro のリッチテキストにする。
    // 字下げは <indent>（折り返した行も同じだけ下がる）。データの文は <noparse> で囲み、< などをそのまま出す
    string ToRichText(RulesPage page)
    {
        StringBuilder sb = new StringBuilder();
        bool first = true;
        foreach (RulesLine line in page.Lines)
        {
            if (line.Kind == RulesLineKind.Heading)
            {
                if (!first) sb.Append("<size=45%> </size>\n"); // 見出しの前を少し空ける
                sb.Append($"<size=112%><color={HeadingColor}>■ <noparse>{line.Text}</noparse></color></size>\n");
            }
            else
            {
                if (line.Indent > 0) sb.Append($"<indent={line.Indent}em>");
                if (!string.IsNullOrEmpty(line.Badge)) sb.Append($"<color={BadgeColor}>［{line.Badge}］</color>");
                string color = line.Color.HasValue ? "#" + ColorUtility.ToHtmlStringRGB(line.Color.Value) : BodyColor;
                sb.Append($"<color={color}><noparse>{line.Text}</noparse></color>");
                if (line.Indent > 0) sb.Append("</indent>");
                sb.Append('\n');
            }
            first = false;
        }
        return sb.ToString().TrimEnd('\n');
    }

    // ------------------------------------------------------------
    //  UI を作る
    // ------------------------------------------------------------
    void BuildButton()
    {
        buttonCanvas = CreateCanvas("RulesButtonCanvas", buttonSortingOrder);
        RectTransform root = (RectTransform)buttonCanvas.transform;

        Button button = CreateButton("RulesButton", root, TabColor, out _);
        RectTransform rt = (RectTransform)button.transform;
        rt.anchorMin = buttonAnchor;
        rt.anchorMax = buttonAnchor;
        rt.pivot = buttonAnchor;
        rt.sizeDelta = buttonSize;
        rt.anchoredPosition = new Vector2(
            Mathf.Lerp(buttonOffset.x, -buttonOffset.x, buttonAnchor.x),
            Mathf.Lerp(buttonOffset.y, -buttonOffset.y, buttonAnchor.y));
        Outline line = button.gameObject.AddComponent<Outline>();
        line.effectColor = WithAlpha(BoxLineColor, 0.8f);
        line.effectDistance = new Vector2(2f, -2f);

        TextMeshProUGUI label = CreateText("Label", rt, $"ルール  [{toggleKey}]", 28f, Color.white, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, Vector2.zero, Vector2.zero);
        button.onClick.AddListener(Toggle);
    }

    void BuildOverlay()
    {
        overlayCanvas = CreateCanvas("RulesOverlayCanvas", overlaySortingOrder);
        RectTransform root = (RectTransform)overlayCanvas.transform;
        overlayGroup = overlayCanvas.gameObject.AddComponent<CanvasGroup>();

        // 暗幕（クリックで閉じる）
        Button dim = CreateButton("Dim", root, DimColor, out _);
        Stretch((RectTransform)dim.transform, new Vector2(-10f, -10f), new Vector2(10f, 10f));
        ColorBlock dc = dim.colors;
        dc.highlightedColor = Color.white;
        dc.pressedColor = Color.white;
        dc.selectedColor = Color.white;
        dim.colors = dc;
        Destroy(dim.GetComponent<Outline>());
        dim.onClick.AddListener(Close);

        // 枠（クリックしても閉じない）
        GameObject boxObj = new GameObject("Box", typeof(RectTransform));
        RectTransform box = (RectTransform)boxObj.transform;
        box.SetParent(root, false);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
        box.sizeDelta = boxSize;
        Image boxImage = boxObj.AddComponent<Image>();
        boxImage.color = BoxColor;
        Outline boxLine = boxObj.AddComponent<Outline>();
        boxLine.effectColor = WithAlpha(BoxLineColor, 0.9f);
        boxLine.effectDistance = new Vector2(3f, -3f);

        const float pad = 30f;
        const float headerH = 56f;
        const float tabH = 56f;
        const float footH = 44f;

        // 上：題名と「閉じる」
        TextMeshProUGUI title = CreateText("Title", box, "ルール説明", 38f, Hex(0xffd54a), TextAlignmentOptions.Left);
        TopBand(title.rectTransform, pad, pad, -pad - 260f, headerH);

        Button close = CreateButton("CloseButton", box, Hex(0x546e7a), out _);
        RectTransform crt = (RectTransform)close.transform;
        crt.anchorMin = crt.anchorMax = crt.pivot = new Vector2(1f, 1f);
        crt.sizeDelta = new Vector2(240f, headerH - 6f);
        crt.anchoredPosition = new Vector2(-pad, -pad);
        TextMeshProUGUI closeLabel = CreateText("Label", crt, $"閉じる  [{toggleKey}]", 26f, Color.white, TextAlignmentOptions.Center);
        Stretch(closeLabel.rectTransform, Vector2.zero, Vector2.zero);
        close.onClick.AddListener(Close);

        // タブの列（ページの数だけ。開くたびに作り直す）
        GameObject rowObj = new GameObject("Tabs", typeof(RectTransform));
        tabRow = (RectTransform)rowObj.transform;
        tabRow.SetParent(box, false);
        TopBand(tabRow, pad, pad + headerH + 10f, -pad, tabH);
        HorizontalLayoutGroup h = rowObj.AddComponent<HorizontalLayoutGroup>();
        h.spacing = 10f;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = true;
        h.childForceExpandHeight = true;

        // 本文（スクロール）
        float top = pad + headerH + 10f + tabH + 16f;
        GameObject scrollObj = new GameObject("Scroll", typeof(RectTransform));
        RectTransform srt = (RectTransform)scrollObj.transform;
        srt.SetParent(box, false);
        Stretch(srt, new Vector2(pad, pad + footH), new Vector2(-pad + 6f, -top));
        scroll = scrollObj.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 45f;

        GameObject viewportObj = new GameObject("Viewport", typeof(RectTransform));
        RectTransform vrt = (RectTransform)viewportObj.transform;
        vrt.SetParent(srt, false);
        Stretch(vrt, Vector2.zero, new Vector2(-18f, 0f)); // 右はスクロールバーの分を空ける
        viewportObj.AddComponent<RectMask2D>();
        Image vImage = viewportObj.AddComponent<Image>(); // ホイール・ドラッグを受けるため（透明）
        vImage.color = new Color(0f, 0f, 0f, 0f);
        scroll.viewport = vrt;

        body = CreateText("Body", vrt, "", bodyFontSize, Color.white, TextAlignmentOptions.TopLeft);
        body.textWrappingMode = TextWrappingModes.Normal;
        body.richText = true;
        body.lineSpacing = 12f;
        body.paragraphSpacing = 6f;
        RectTransform brt = body.rectTransform;
        brt.anchorMin = new Vector2(0f, 1f);
        brt.anchorMax = new Vector2(1f, 1f);
        brt.pivot = new Vector2(0.5f, 1f);
        brt.offsetMin = new Vector2(4f, 0f);
        brt.offsetMax = new Vector2(-4f, 0f);
        ContentSizeFitter fitter = body.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scroll.content = brt;

        scroll.verticalScrollbar = CreateScrollbar(srt);
        scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;

        // 下：案内
        TextMeshProUGUI foot = CreateText("Foot", box,
            $"タブ・← → でページを切り替え ／ ホイール・↑ ↓ でスクロール ／ {toggleKey}・Esc・Space・枠の外をクリックで閉じる",
            22f, Hex(0x9e9e9e), TextAlignmentOptions.Center);
        RectTransform frt = foot.rectTransform;
        frt.anchorMin = new Vector2(0f, 0f);
        frt.anchorMax = new Vector2(1f, 0f);
        frt.pivot = new Vector2(0.5f, 0f);
        frt.offsetMin = new Vector2(pad, pad * 0.5f);
        frt.offsetMax = new Vector2(-pad, pad * 0.5f + footH);
    }

    void RebuildTabs()
    {
        foreach (GameObject obj in tabObjects) Destroy(obj);
        tabObjects.Clear();
        tabImages.Clear();
        for (int i = 0; i < pages.Count; i++)
        {
            int index = i;
            Button tab = CreateButton($"Tab_{i}", tabRow, TabColor, out Image image);
            TextMeshProUGUI label = CreateText("Label", tab.transform, pages[i].Title, 24f, Color.white, TextAlignmentOptions.Center);
            label.enableAutoSizing = true;
            label.fontSizeMin = 16f;
            label.fontSizeMax = 24f;
            Stretch(label.rectTransform, new Vector2(6f, 0f), new Vector2(-6f, 0f));
            tab.onClick.AddListener(() => { ShowPage(index); ClearUiSelection(); });
            tabObjects.Add(tab.gameObject);
            tabImages.Add(image);
        }
    }

    Scrollbar CreateScrollbar(RectTransform parent)
    {
        GameObject barObj = new GameObject("Scrollbar", typeof(RectTransform));
        RectTransform rt = (RectTransform)barObj.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(1f, 0f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 0.5f);
        rt.sizeDelta = new Vector2(12f, 0f);
        Image bg = barObj.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.08f);

        GameObject areaObj = new GameObject("Sliding Area", typeof(RectTransform));
        RectTransform area = (RectTransform)areaObj.transform;
        area.SetParent(rt, false);
        Stretch(area, Vector2.zero, Vector2.zero);

        GameObject handleObj = new GameObject("Handle", typeof(RectTransform));
        RectTransform handle = (RectTransform)handleObj.transform;
        handle.SetParent(area, false);
        Stretch(handle, Vector2.zero, Vector2.zero);
        Image hImage = handleObj.AddComponent<Image>();
        hImage.color = WithAlpha(BoxLineColor, 0.7f);

        Scrollbar bar = barObj.AddComponent<Scrollbar>();
        bar.direction = Scrollbar.Direction.BottomToTop;
        bar.handleRect = handle;
        bar.targetGraphic = hImage;
        Navigation nav = bar.navigation;
        nav.mode = Navigation.Mode.None;
        bar.navigation = nav;
        return bar;
    }

    Canvas CreateCanvas(string name, int order)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(transform, false);
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = order;
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        obj.AddComponent<GraphicRaycaster>();
        return canvas;
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
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        button.colors = colors;
        // キーボードの W A S D・矢印で UI の選択が動かないように（盤の操作と同じキーなので）
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
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

    // ボタンを押したあと、EventSystem の選択を外す（Space・Enter でボタンがもう一度押されないように）
    static void ClearUiSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // クリックを受けるための EventSystem（シーンになければ作る）
    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject obj = new GameObject("EventSystem");
        obj.AddComponent<EventSystem>();
        obj.AddComponent<InputSystemUIInputModule>();
    }

    // 親の上端に沿った帯（left・right は左右の余白、top は上からの距離、height は高さ）
    static void TopBand(RectTransform rt, float left, float top, float right, float height)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(left, -top - height);
        rt.offsetMax = new Vector2(right, -top);
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
