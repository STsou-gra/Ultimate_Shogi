using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using TMPro;

// 対戦モードを選ぶ画面（Prototype の src/scenes/ModeScene.js のうち、本将棋型の「2人で対戦」「CPU と対戦」）。
// タイトルの「ゲームスタート」（GameSceneManager.PlayGame）で、タイトルの上に重ねて開く（シーンに置かなくてよい。Canvas は実行時に作る）。
//  ・「2人で対戦」：1 台の画面で交代に操作（今までと同じ）→ 魔法の編成（両者）
//  ・「CPU と対戦」：自分が先手か後手か・CPU の強さ（Lv0〜Lv2）を選ぶ → 魔法の編成（人の分だけ。CPU は自分で選ぶ）
// 選んだ設定は MatchSetup に入れる（前回の設定を覚えている）。
// 操作：マウス、またはキーボード（← → で選ぶ・↑ ↓ で行を移る・Enter で決定・Esc で戻る）
public class ModeSelectView : MonoBehaviour
{
    const string FontPath = "Fonts & Materials/NotoSansJP-Regular SDF";
    const string NextScene = "OrganizationScene";

    static readonly Color Panel = new Color(0.05f, 0.06f, 0.1f, 0.94f);
    static readonly Color Normal = new Color(0.2f, 0.24f, 0.32f, 1f);
    static readonly Color Picked = new Color(0.13f, 0.5f, 0.9f, 1f);
    static readonly Color Focus = new Color(1f, 0.84f, 0.29f, 1f);

    TMP_FontAsset font;
    Canvas canvas;
    GameObject modePage, cpuPage;
    readonly List<Button> modeButtons = new List<Button>();
    readonly List<Button> sideButtons = new List<Button>();
    readonly List<Button> levelButtons = new List<Button>();
    Button startButton, backButton;
    TextMeshProUGUI levelNote;

    int modeFocus;             // 0 = 2人で対戦、1 = CPU と対戦
    int cpuRow;                // 0 = 手番、1 = 強さ、2 = 開始
    PlayerType side;
    int level;
    bool onCpuPage;

    static ModeSelectView open;
    int openedFrame;

    // タイトルの上に開く（もう開いていれば何もしない）
    public static void Open()
    {
        if (open != null) return;
        open = new GameObject("ModeSelectView").AddComponent<ModeSelectView>();
    }

    void Awake()
    {
        openedFrame = Time.frameCount;
        font = Resources.Load<TMP_FontAsset>(FontPath);
        side = MatchSetup.HumanSide;
        level = MatchSetup.CpuLevel;
        modeFocus = MatchSetup.IsVsCpu ? 1 : 0;
        EnsureEventSystem();
        Build();
        ShowModePage();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null || Time.frameCount == openedFrame) return; // 開いたキー（Enter）を決定として読まない
        bool left = k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame;
        bool right = k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame;
        bool up = k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame;
        bool down = k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame;
        bool enter = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame;
        bool back = k.escapeKey.wasPressedThisFrame;

        if (!onCpuPage)
        {
            if (left || right) { modeFocus = 1 - modeFocus; Refresh(); }
            if (enter) ChooseMode(modeFocus);
            if (back) Close();
            return;
        }
        if (up) { cpuRow = (cpuRow + 2) % 3; Refresh(); }
        if (down) { cpuRow = (cpuRow + 1) % 3; Refresh(); }
        if (left || right)
        {
            int d = right ? 1 : -1;
            if (cpuRow == 0) side = side == PlayerType.Player1 ? PlayerType.Player2 : PlayerType.Player1;
            else if (cpuRow == 1) level = CpuLevels.Clamp(level + d);
            Refresh();
        }
        if (enter) StartCpuMatch();
        if (back) ShowModePage();
    }

    // ------------------------------------------------------------
    //  決定
    // ------------------------------------------------------------

    void ChooseMode(int index)
    {
        modeFocus = index;
        if (index == 0)
        {
            MatchSetup.SetTwoPlayers();
            SceneManager.LoadScene(NextScene);
            return;
        }
        ShowCpuPage();
    }

    void StartCpuMatch()
    {
        MatchSetup.SetVsCpu(side, level);
        SceneManager.LoadScene(NextScene);
    }

    void Close()
    {
        Destroy(gameObject);
    }

    void ShowModePage()
    {
        onCpuPage = false;
        modePage.SetActive(true);
        cpuPage.SetActive(false);
        Refresh();
    }

    void ShowCpuPage()
    {
        onCpuPage = true;
        cpuRow = 2;
        modePage.SetActive(false);
        cpuPage.SetActive(true);
        Refresh();
    }

    void Refresh()
    {
        for (int i = 0; i < modeButtons.Count; i++) Paint(modeButtons[i], i == modeFocus, !onCpuPage && i == modeFocus);
        for (int i = 0; i < sideButtons.Count; i++) Paint(sideButtons[i], (i == 0) == (side == PlayerType.Player1), cpuRow == 0 && (i == 0) == (side == PlayerType.Player1));
        for (int i = 0; i < levelButtons.Count; i++) Paint(levelButtons[i], i == level, cpuRow == 1 && i == level);
        Paint(startButton, true, cpuRow == 2);
        if (levelNote != null) levelNote.text = CpuLevels.Note(level);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    static void Paint(Button b, bool picked, bool focused)
    {
        if (b == null) return;
        Image img = b.targetGraphic as Image;
        if (img != null) img.color = picked ? Picked : Normal;
        Outline outline = b.GetComponent<Outline>();
        if (outline != null)
        {
            outline.effectColor = focused ? Focus : new Color(0f, 0f, 0f, 0.5f);
            outline.effectDistance = focused ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
        }
    }

    // ------------------------------------------------------------
    //  画面を作る
    // ------------------------------------------------------------

    void Build()
    {
        GameObject root = new GameObject("ModeSelectCanvas", typeof(RectTransform));
        root.transform.SetParent(transform, false);
        canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 500;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        // 画面全体を覆う背景（タイトルのボタンを押せないように）
        Image bg = new GameObject("Background", typeof(RectTransform)).AddComponent<Image>();
        bg.transform.SetParent(root.transform, false);
        Stretch(bg.rectTransform);
        bg.color = Panel;

        Text(root.transform, "対戦モード", 72, new Vector2(0, 360), new Vector2(1200, 100));

        // 1 ページ目：2人で対戦 / CPU と対戦
        modePage = Page(root.transform, "ModePage");
        modeButtons.Add(BigButton(modePage.transform, "2人で対戦", "1 台の画面で交代に操作", new Vector2(-330, 40), () => ChooseMode(0)));
        modeButtons.Add(BigButton(modePage.transform, "CPU と対戦", "先手・後手と CPU の強さを選ぶ", new Vector2(330, 40), () => ChooseMode(1)));
        Text(modePage.transform, "← → で選んで Enter／クリック　　Esc：タイトルに戻る", 30, new Vector2(0, -300), new Vector2(1400, 60));
        Button close = SmallButton(modePage.transform, "◀ タイトルに戻る", new Vector2(0, -400), Close);
        close.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 80);

        // 2 ページ目：CPU と対戦の設定
        cpuPage = Page(root.transform, "CpuPage");
        Text(cpuPage.transform, "あなたの手番", 38, new Vector2(-560, 180), new Vector2(360, 70));
        sideButtons.Add(SmallButton(cpuPage.transform, "先手（盤の手前・先に指す）", new Vector2(-80, 180), () => { side = PlayerType.Player1; cpuRow = 0; Refresh(); }));
        sideButtons.Add(SmallButton(cpuPage.transform, "後手", new Vector2(420, 180), () => { side = PlayerType.Player2; cpuRow = 0; Refresh(); }));
        sideButtons[0].GetComponent<RectTransform>().sizeDelta = new Vector2(520, 90);

        Text(cpuPage.transform, "CPU の強さ", 38, new Vector2(-560, 30), new Vector2(360, 70));
        for (int lv = CpuLevels.Min; lv <= CpuLevels.Max; lv++)
        {
            int captured = lv;
            levelButtons.Add(SmallButton(cpuPage.transform, CpuLevels.Label(lv), new Vector2(-180 + 330 * lv, 30), () => { level = captured; cpuRow = 1; Refresh(); }));
        }
        levelNote = Text(cpuPage.transform, "", 30, new Vector2(150, -50), new Vector2(1100, 60));

        Text(cpuPage.transform, "魔法スキルの編成は、あなたの分だけ選びます（CPU は自分で選びます）", 30, new Vector2(0, -150), new Vector2(1500, 60));
        startButton = SmallButton(cpuPage.transform, "編成へ ▶", new Vector2(220, -270), StartCpuMatch);
        startButton.GetComponent<RectTransform>().sizeDelta = new Vector2(420, 100);
        backButton = SmallButton(cpuPage.transform, "◀ 戻る", new Vector2(-260, -270), ShowModePage);
        Text(cpuPage.transform, "↑ ↓ で行を選ぶ・← → で変える・Enter で決定・Esc で戻る", 28, new Vector2(0, -390), new Vector2(1400, 60));
    }

    GameObject Page(Transform parent, string name)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(parent, false);
        Stretch((RectTransform)page.transform);
        return page;
    }

    Button BigButton(Transform parent, string title, string note, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        Button b = MakeButton(parent, title, pos, new Vector2(560, 300), onClick);
        TextMeshProUGUI label = b.GetComponentInChildren<TextMeshProUGUI>();
        label.text = $"{title}\n<size=60%>{note}</size>";
        label.fontSize = 60;
        return b;
    }

    Button SmallButton(Transform parent, string title, Vector2 pos, UnityEngine.Events.UnityAction onClick)
    {
        return MakeButton(parent, title, pos, new Vector2(300, 90), onClick);
    }

    Button MakeButton(Transform parent, string title, Vector2 pos, Vector2 size, UnityEngine.Events.UnityAction onClick)
    {
        GameObject obj = new GameObject(title, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image img = obj.AddComponent<Image>();
        img.color = Normal;
        Button b = obj.AddComponent<Button>();
        b.targetGraphic = img;
        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None; // キーボードの選択はこのクラスが行う
        b.navigation = nav;
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);
        b.onClick.AddListener(onClick);

        TextMeshProUGUI label = Text(obj.transform, title, 34, Vector2.zero, size);
        Stretch(label.rectTransform);
        return b;
    }

    TextMeshProUGUI Text(Transform parent, string text, float size, Vector2 pos, Vector2 box)
    {
        GameObject obj = new GameObject("Text", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = Color.white;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.raycastTarget = false;
        tmp.rectTransform.anchoredPosition = pos;
        tmp.rectTransform.sizeDelta = box;
        return tmp;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject obj = new GameObject("EventSystem");
        obj.AddComponent<EventSystem>();
        obj.AddComponent<InputSystemUIInputModule>();
    }
}
