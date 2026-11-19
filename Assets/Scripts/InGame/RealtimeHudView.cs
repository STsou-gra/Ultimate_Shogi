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
//  ・討伐任務は先手（プレイヤー）の席のパネルだけ（後手の席の位置には BossView がボスのパネルを出す）
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

    // 本文の元になる値（文字列を作らずに前回と比べるため）
    private struct BodyInputs
    {
        public SeatKeys Keys;         // キー（キー案内・魔法のキー）
        public SkillDeck Deck;        // 編成
        public int SkillCount;        // 編成の魔法の数
        public int SkillsHash;        // 編成の魔法（の参照）
        public int Ctrl;              // 席の操作：0 人・1 CPU（RealtimeCpuPacer）・2 それ以外の自動の席（棋譜の再生など）
        public int CpuLevel;          // CPU の強さ
        public int CpuInterval;       // CPU の動く間隔（tick）
        public bool Thinking;         // CPU が考え中か
        public long SkillStates;      // 魔法ごとに 3 ビット：使えるか・超常魔法の使用済み・解放済み
        public bool Overflow;         // 魔法が多すぎて SkillStates に入りきらない（毎回作り直す）

        public bool SameAs(in BodyInputs o)
        {
            return !Overflow && !o.Overflow && Keys == o.Keys && Deck == o.Deck && SkillCount == o.SkillCount && SkillsHash == o.SkillsHash
                && Ctrl == o.Ctrl && CpuLevel == o.CpuLevel && CpuInterval == o.CpuInterval && Thinking == o.Thinking && SkillStates == o.SkillStates;
        }
    }

    private class SeatPanel
    {
        public PlayerType Owner;
        public RectTransform Rect;
        public Outline Line;
        public TextMeshProUGUI Header;
        public TextMeshProUGUI Body;
        public TextMeshProUGUI Status;

        // 前回の表示に使った値（同じなら文字列を作り直さない・Outline を書き直さない）
        public bool Shown;            // 一度でも書いたか
        public bool Focus;            // 枠（Outline）：操作中の席か
        public string State;          // 見出し：状態の文言（「（CPU）」など。どれも固定の文字列）
        public int Energy;            // 見出し：エナジー
        public BodyInputs BodyShown;      // 本文：前回の元の値
        public string StatusRaw;      // 案内文：LocalizeKeys に渡す前の文字列
        public SeatKeys StatusKeys;   // 案内文：LocalizeKeys に渡したキー
        public SeatKeys GuideKeys;    // キー案内（移動・決定・錬成など）を作ったときのキー
        public string Guide;          // キー案内（キーごとに固定なので一度だけ作る）
    }

    private Canvas canvas;
    private TextMeshProUGUI clockText;
    private TextMeshProUGUI pauseLabel;
    private GameObject pauseOverlay;
    private TextMeshProUGUI pauseText;
    private readonly List<SeatPanel> panels = new List<SeatPanel>();
    private float nextRefresh = 0f;
    private readonly StringBuilder bodyBuilder = new StringBuilder();
    private string shownPauseReason; // 一時停止の理由（前回の表示）。null なら「Esc・再開」の文言
    private bool pauseTextShown;

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
            // 理由が前回と同じなら文字列を作り直さない
            string reason = gameSystem.IsUserPaused ? null : gameSystem.PauseReason ?? "";
            if (!pauseTextShown || reason != shownPauseReason)
            {
                pauseTextShown = true;
                shownPauseReason = reason;
                pauseText.text = reason == null ? "一時停止中\n<size=40%>Esc・「再開」ボタンで再開</size>" : $"一時停止中\n<size=40%>{reason}</size>";
            }
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

    // 0.1 秒ごと。文字列は、元になる値が前回と変わったときだけ作る。比べる値：
    //  枠（Outline）：FocusSide がこの席か
    //  見出し：状態の文言（棋譜の再生／CPU／操作中／相手の時間停止中。どれも固定の文字列）・エナジー（名前は対局中に変わらない）
    //  本文：BodyInputs（キー（SeatKeys の参照）・編成の魔法・席の操作の種類・CPU の強さと間隔・考え中か・魔法ごとの使える／使用済み／未解放）
    //  案内文：SeatStatus の文字列・キー（SeatKeys の参照）
    void RefreshSeat(SeatPanel p)
    {
        PlayerType owner = p.Owner;
        bool focus = gameSystem.FocusSide == owner;
        bool first = !p.Shown;
        p.Shown = true;
        if (first || focus != p.Focus)
        {
            // Outline は値を書くたびに頂点を作り直すので、変わったときだけ書く
            p.Focus = focus;
            Color side = owner == PlayerType.Player1 ? SenteColor : GoteColor;
            p.Line.effectColor = focus ? Color.Lerp(side, Color.white, 0.35f) : WithAlpha(side, 0.45f);
            p.Line.effectDistance = focus ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
        }

        SeatKeys k = gameSystem.KeysOf(owner);
        IRealtimeSeatController ctrl = gameSystem.SeatController(owner);
        bool cpu = ctrl != null;
        int energy = gameSystem.Match.Energy.Get(owner);
        string state = ctrl is RealtimeReplayController ? "（棋譜の再生）" : cpu ? "（CPU）" : focus ? "（操作中）" : "";
        TimeStopState ts = gameSystem.TimeStop;
        if (ts != null && ts.Owner != owner) state = "（相手の時間停止中は動けない）";
        if (first || state != p.State || energy != p.Energy)
        {
            p.State = state;
            p.Energy = energy;
            string name = gameSystem.IsRaid ? "☗ プレイヤー" : owner == PlayerType.Player1 ? "☗ 先手" : "☖ 後手";
            p.Header.text = $"{name}{state}　エナジー {energy}";
        }

        SkillDeck deck = gameSystem.GetSkillDeck(owner);
        BodyInputs body = BodyInputsOf(owner, k, ctrl, deck);
        if (first || !body.SameAs(p.BodyShown))
        {
            p.BodyShown = body;
            p.Body.text = BuildBody(p, k, ctrl, deck);
        }

        string status = gameSystem.SeatStatus(owner);
        if (first || status != p.StatusRaw || k != p.StatusKeys)
        {
            p.StatusRaw = status;
            p.StatusKeys = k;
            p.Status.text = OrEmpty(GameSystem.LocalizeKeys(status, k));
        }
    }

    BodyInputs BodyInputsOf(PlayerType owner, SeatKeys k, IRealtimeSeatController ctrl, SkillDeck deck)
    {
        BodyInputs v = new BodyInputs { Keys = k, Deck = deck, SkillsHash = 17 };
        if (ctrl is RealtimeCpuPacer pacer)
        {
            v.Ctrl = 1;
            v.CpuLevel = pacer.Level;
            v.CpuInterval = gameSystem.Policy.CpuIntervalTicks(pacer.Level);
            v.Thinking = pacer.IsThinking;
        }
        else if (ctrl != null)
        {
            v.Ctrl = 2;
        }
        if (deck == null || deck.skills == null) return v;
        v.SkillCount = deck.skills.Count;
        for (int i = 0; i < v.SkillCount; i++)
        {
            SkillDefinition skill = deck.skills[i];
            v.SkillsHash = v.SkillsHash * 31 + (skill != null ? skill.GetHashCode() : 0);
            if (skill == null) continue;
            if (i >= 21)
            {
                v.Overflow = true; // 今の編成は 5 枠まで。入りきらないときは毎回作り直す
                continue;
            }
            int b = gameSystem.CanUseSkill(owner, skill) ? 1 : 0;
            if (skill.rank == SkillRank.Transcendent)
            {
                if (gameSystem.IsTranscendentUsed(owner)) b |= 2;
                if (gameSystem.IsTranscendentUnlocked(owner)) b |= 4;
            }
            v.SkillStates |= (long)b << (i * 3);
        }
        return v;
    }

    string BuildBody(SeatPanel p, SeatKeys k, IRealtimeSeatController ctrl, SkillDeck deck)
    {
        PlayerType owner = p.Owner;
        bool cpu = ctrl != null;
        StringBuilder sb = bodyBuilder;
        sb.Clear();
        if (ctrl is RealtimeCpuPacer pacer)
        {
            // CPU の席：キーの代わりに強さと間隔（Lv0 2.0 秒／Lv1 1.5 秒／Lv2 1.2 秒）
            sb.Append($"<size=85%>{CpuLevels.Label(pacer.Level)}：{MatchPolicy.TicksToSec(gameSystem.Policy.CpuIntervalTicks(pacer.Level)):0.0} 秒ごとに動く{(pacer.IsThinking ? "（考え中）" : "")}</size>\n");
        }
        else if (!cpu)
        {
            sb.Append(KeyGuide(p, k));
        }
        if (deck != null && deck.skills != null)
        {
            for (int i = 0; i < deck.skills.Count; i++)
            {
                SkillDefinition skill = deck.skills[i];
                if (skill == null) continue;
                string key = cpu ? "" : SeatKeys.Label(k.Skills, i);
                bool usable = gameSystem.CanUseSkill(owner, skill);
                string color = usable ? "#FFFFFF" : "#7A7A86";
                string used = skill.rank == SkillRank.Transcendent
                    ? (gameSystem.IsTranscendentUsed(owner) ? "（使用済み）" : gameSystem.IsTranscendentUnlocked(owner) ? "" : "（未解放）")
                    : "";
                sb.Append($"<color={color}>{(key.Length > 0 ? $"[{key}] " : "")}{skill.skillName} {skill.cost}{used}</color>  ");
                if (i % 3 == 2) sb.Append('\n');
            }
        }
        return sb.ToString().TrimEnd();
    }

    // キー案内（移動・決定・取消・持ち駒・必殺技・狙撃・向き・解除・錬成）。キーごとに固定なので、キーが変わったときだけ作る
    string KeyGuide(SeatPanel p, SeatKeys k)
    {
        if (p.Guide != null && p.GuideKeys == k) return p.Guide;
        // 錬成のキーが多い席（討伐任務）は「Z〜;（錬成パネルの順）」とまとめる
        string forge;
        if (k.Forge.Length > 4)
        {
            forge = $"{SeatKeys.Label(k.Forge, 0)}〜{SeatKeys.Label(k.Forge, k.Forge.Length - 1)}（錬成パネルの順）";
        }
        else
        {
            StringBuilder f = new StringBuilder();
            for (int i = 0; i < k.Forge.Length; i++)
            {
                if (i > 0) f.Append('・');
                f.Append(SeatKeys.Label(k.Forge, i));
            }
            forge = f.ToString();
        }
        p.GuideKeys = k;
        p.Guide = $"<size=85%>移動 {SeatKeys.Labels(k.Up, k.Left, k.Down, k.Right)}　決定 {SeatKeys.Label(k.Ok)}　取消 {SeatKeys.Label(k.Cancel)}　持ち駒 {SeatKeys.Label(k.Hand)}\n"
            + $"必殺技 {SeatKeys.Label(k.Special)}　狙撃 {SeatKeys.Label(k.Snipe)}　向き {SeatKeys.Label(k.Orient)}　解除 {SeatKeys.Label(k.Release)}　錬成 {forge}</size>\n";
        return p.Guide;
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
        // 討伐任務：後手の席はボス（その位置には BossView がボスのパネルを出す）
        if (!gameSystem.IsRaid) panels.Add(CreateSeatPanel(PlayerType.Player2, root, new Vector2(0f, 1f), new Vector2(16f, -80f)));
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
