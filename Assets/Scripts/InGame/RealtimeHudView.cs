using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// 合戦将棋型の HUD（Prototype の Game.js のヘッダーの経過時間・「一時停止」ボタン・両者の PlayerPanel）。
//  ・画面の上：戦闘型と経過時間・「一時停止 [Esc]」ボタン
//  ・画面の左：先手（下）・後手（上）の席のパネル。キーの割り当て・エナジー・魔法（キーとコスト。今使えない魔法は暗い）・その席の最後の案内文。
//    操作中の席（スキルボタン・錬成ボタンが指す席）は枠が明るくなる
//  ・止めている間（一時停止・ルール説明を開いている・ウィンドウが非アクティブ）は画面を暗くして理由を出す
// 合戦将棋型の対局で GameSystem が自動で追加する（シーンに置かなくてよい）
public class RealtimeHudView : MonoBehaviour
{
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む
    [SerializeField] private int sortingOrder = 55; // ルールのボタン（60）・カットイン（500）より奥

    private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.10f, 0.78f);
    private static readonly Color SenteColor = new Color(0x2f / 255f, 0x7b / 255f, 1f, 1f);   // 陣営オーラと同じ青
    private static readonly Color GoteColor = new Color(1f, 0x2a / 255f, 0x4a / 255f, 1f);     // 陣営オーラと同じ赤
    private static readonly Color PauseButtonColor = new Color(0.22f, 0.28f, 0.34f, 1f);

    private class SeatPanel
    {
        public PlayerType Owner;
        public RectTransform Rect;
        public Outline Line;
        public TextMeshProUGUI Header;
        public TextMeshProUGUI Body;
        public TextMeshProUGUI Status;
    }

    private Canvas canvas;
    private TextMeshProUGUI clockText;
    private TextMeshProUGUI pauseLabel;
    private GameObject pauseOverlay;
    private TextMeshProUGUI pauseText;
    private readonly List<SeatPanel> panels = new List<SeatPanel>();
    private float nextRefresh = 0f;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        if (gameSystem == null || !gameSystem.IsRealtime) return;
        Build();
        Refresh();
    }

    void Update()
    {
        if (canvas == null || gameSystem == null) return;

        // マウスで押したボタンが選ばれたままだと、後手の決定（Enter）でもう一度押されるので外す
        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        bool paused = gameSystem.IsRealtimePaused && gameSystem.IsInTurn;
        if (pauseOverlay.activeSelf != paused) pauseOverlay.SetActive(paused);
        if (paused)
        {
            string reason = gameSystem.PauseReason;
            pauseText.text = gameSystem.IsUserPaused ? $"一時停止中\n<size=40%>Esc・「再開」ボタンで再開</size>" : $"一時停止中\n<size=40%>{reason}</size>";
        }
        pauseLabel.text = gameSystem.IsUserPaused ? "再開  [Esc]" : "一時停止  [Esc]";

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.1f;
            Refresh();
        }
    }

    void Refresh()
    {
        if (gameSystem.Match == null) return;
        clockText.text = gameSystem.Policy.ProgressLabel;
        foreach (SeatPanel p in panels) RefreshSeat(p);
    }

    void RefreshSeat(SeatPanel p)
    {
        PlayerType owner = p.Owner;
        bool focus = gameSystem.FocusSide == owner;
        Color side = owner == PlayerType.Player1 ? SenteColor : GoteColor;
        p.Line.effectColor = focus ? Color.Lerp(side, Color.white, 0.35f) : WithAlpha(side, 0.45f);
        p.Line.effectDistance = focus ? new Vector2(4f, -4f) : new Vector2(2f, -2f);

        SeatKeys k = gameSystem.KeysOf(owner);
        bool cpu = gameSystem.SeatController(owner) != null;
        string name = owner == PlayerType.Player1 ? "☗ 先手" : "☖ 後手";
        string energy = $"エナジー {gameSystem.Match.Energy.Get(owner)}";
        string state = cpu ? "（CPU）" : focus ? "（操作中）" : "";
        TimeStopState ts = gameSystem.TimeStop;
        if (ts != null && ts.Owner != owner) state = "（相手の時間停止中は動けない）";
        p.Header.text = $"{name}{state}　{energy}";

        StringBuilder sb = new StringBuilder();
        sb.Append($"<size=85%>移動 {SeatKeys.Labels(k.Up, k.Left, k.Down, k.Right)}　決定 {SeatKeys.Label(k.Ok)}　取消 {SeatKeys.Label(k.Cancel)}　持ち駒 {SeatKeys.Label(k.Hand)}\n");
        sb.Append($"必殺技 {SeatKeys.Label(k.Special)}　狙撃 {SeatKeys.Label(k.Snipe)}　向き {SeatKeys.Label(k.Orient)}　解除 {SeatKeys.Label(k.Release)}　錬成 {SeatKeys.Label(k.Forge, 0)}・{SeatKeys.Label(k.Forge, 1)}</size>\n");
        SkillDeck deck = gameSystem.GetSkillDeck(owner);
        if (deck != null && deck.skills != null)
        {
            for (int i = 0; i < deck.skills.Count; i++)
            {
                SkillDefinition skill = deck.skills[i];
                if (skill == null) continue;
                string key = SeatKeys.Label(k.Skills, i);
                bool usable = gameSystem.CanUseSkill(owner, skill);
                string color = usable ? "#FFFFFF" : "#7A7A86";
                string used = skill.rank == SkillRank.Transcendent
                    ? (gameSystem.IsTranscendentUsed(owner) ? "（使用済み）" : gameSystem.IsTranscendentUnlocked(owner) ? "" : "（未解放）")
                    : "";
                sb.Append($"<color={color}>[{key}] {skill.skillName} {skill.cost}{used}</color>  ");
                if (i % 3 == 2) sb.Append('\n');
            }
        }
        p.Body.text = sb.ToString().TrimEnd();
        p.Status.text = OrEmpty(GameSystem.LocalizeKeys(gameSystem.SeatStatus(owner), k));
    }

    static string OrEmpty(string text)
    {
        return string.IsNullOrEmpty(text) ? "" : text;
    }

    // ------------------------------------------------------------
    //  UI を作る
    // ------------------------------------------------------------
    void Build()
    {
        GameObject obj = new GameObject("RealtimeHudCanvas", typeof(RectTransform));
        obj.transform.SetParent(transform, false);
        canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        obj.AddComponent<GraphicRaycaster>();
        RectTransform root = (RectTransform)obj.transform;

        // 上：経過時間と「一時停止」
        RectTransform bar = CreatePanel("ClockBar", root, new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(620f, 58f));
        clockText = CreateText("Clock", bar, "", 30f, Color.white, TextAlignmentOptions.Left);
        Stretch(clockText.rectTransform, new Vector2(18f, 0f), new Vector2(-230f, 0f));
        Button pause = CreateButton("PauseButton", bar, PauseButtonColor);
        RectTransform prt = (RectTransform)pause.transform;
        prt.anchorMin = new Vector2(1f, 0.5f);
        prt.anchorMax = new Vector2(1f, 0.5f);
        prt.pivot = new Vector2(1f, 0.5f);
        prt.sizeDelta = new Vector2(210f, 44f);
        prt.anchoredPosition = new Vector2(-8f, 0f);
        pauseLabel = CreateText("Label", prt, "一時停止  [Esc]", 24f, Color.white, TextAlignmentOptions.Center);
        Stretch(pauseLabel.rectTransform, Vector2.zero, Vector2.zero);
        pause.onClick.AddListener(() => gameSystem.TogglePause());

        // 左：後手（上）・先手（下）の席
        panels.Add(CreateSeatPanel(PlayerType.Player2, root, new Vector2(0f, 1f), new Vector2(16f, -80f)));
        panels.Add(CreateSeatPanel(PlayerType.Player1, root, new Vector2(0f, 0f), new Vector2(16f, 110f)));

        // 止めている間の暗幕（クリックは通す：「再開」ボタンを押せるように、暗幕はボタンより奥）
        pauseOverlay = new GameObject("PauseOverlay", typeof(RectTransform));
        RectTransform ort = (RectTransform)pauseOverlay.transform;
        ort.SetParent(root, false);
        Stretch(ort, Vector2.zero, Vector2.zero);
        Image dim = pauseOverlay.AddComponent<Image>();
        dim.color = new Color(0f, 0f, 0f, 0.55f);
        dim.raycastTarget = false;
        pauseText = CreateText("PauseText", ort, "一時停止中", 96f, Color.white, TextAlignmentOptions.Center);
        Stretch(pauseText.rectTransform, Vector2.zero, Vector2.zero);
        pauseOverlay.transform.SetSiblingIndex(0);
        pauseOverlay.SetActive(false);
    }

    SeatPanel CreateSeatPanel(PlayerType owner, RectTransform root, Vector2 anchor, Vector2 pos)
    {
        RectTransform rt = CreatePanel(owner == PlayerType.Player1 ? "SentePanel" : "GotePanel", root, anchor, pos, new Vector2(560f, 250f));
        SeatPanel p = new SeatPanel { Owner = owner, Rect = rt, Line = rt.GetComponent<Outline>() };
        p.Header = CreateText("Header", rt, "", 26f, owner == PlayerType.Player1 ? Color.Lerp(SenteColor, Color.white, 0.5f) : Color.Lerp(GoteColor, Color.white, 0.5f), TextAlignmentOptions.TopLeft);
        TopBand(p.Header.rectTransform, 14f, 8f, -14f, 34f);
        p.Body = CreateText("Body", rt, "", 20f, Color.white, TextAlignmentOptions.TopLeft);
        p.Body.textWrappingMode = TextWrappingModes.Normal;
        TopBand(p.Body.rectTransform, 14f, 44f, -14f, 130f);
        p.Status = CreateText("Status", rt, "", 18f, new Color(1f, 0.92f, 0.7f, 1f), TextAlignmentOptions.TopLeft);
        p.Status.textWrappingMode = TextWrappingModes.Normal;
        p.Status.overflowMode = TextOverflowModes.Ellipsis;
        p.Status.fontStyle = FontStyles.Normal;
        TopBand(p.Status.rectTransform, 14f, 176f, -14f, 66f);
        return p;
    }

    RectTransform CreatePanel(string name, Transform parent, Vector2 anchor, Vector2 pos, Vector2 size)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        RectTransform rt = (RectTransform)obj.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
        Image bg = obj.AddComponent<Image>();
        bg.color = PanelColor;
        bg.raycastTarget = false;
        Outline line = obj.AddComponent<Outline>();
        line.effectColor = new Color(1f, 1f, 1f, 0.3f);
        line.effectDistance = new Vector2(2f, -2f);
        return rt;
    }

    Button CreateButton(string name, Transform parent, Color color)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        // キーボードで UI の選択が動かないように（盤の操作と同じキーなので）
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
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
        tmp.richText = true;
        tmp.raycastTarget = false;
        return tmp;
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
}
