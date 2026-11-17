using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

// タイトル画面（Prototype の src/scenes/TitleScene.js）。起動したとき・結果画面の「タイトルへ」で開き、遊ぶモードを選ぶ。
//  ・通常対局：2人で対戦・CPU と対戦 → 対戦モードの画面（ModeSelectView）へ
//  ・討伐任務：ボスバトル → ボス選択の画面（BossSelectView）へ
//  ・下のボタン：「ルール」（RulesOverlayView。Tab キーでも開く）・「棋譜を読み込んで再生」（ファイルを選んで、対局のシーンで最初から再生する）
//  ・操作：マウス、またはキーボード（← → で選ぶ・Enter / Space で決定）。前回選んだモードにカーソルを合わせて開く
// GameTitle シーンの GameSceneManager が Start で開く（Canvas は実行時に作る。シーンに元からある古いタイトルの Canvas は隠す）。
// 対戦モード・ボス選択・編成の画面は、この画面の上に重ねて開く（その間、この画面はキーを読まない）。それらの「◀ タイトルに戻る」で、この画面に戻る。
// モードのカードの説明は、戦闘型（BattleRulesets）・ボス（BossDefs）の定義から作る（ボスや戦闘型を足すと自動で出る）
public class TitleView : MonoBehaviour
{
    public const string SceneName = "GameTitle";
    const int SortingOrder = 400; // 対戦モード・ボス選択（500）・ルール説明（1000）より奥、シーンの UI より手前

    const float CardW = 720f, CardH = 600f, CardGap = 72f, CardY = -110f, BandH = 264f;

    static readonly Color Background = RuntimeUi.Hex(0x07040c);
    static readonly Color CardColor = RuntimeUi.Hex(0x150c1f);
    static readonly Color CardFocusColor = RuntimeUi.Hex(0x1e1029);
    static readonly Color Gold = RuntimeUi.Hex(0xffd54a);

    // タイトル画面に並べるモード
    class Mode
    {
        public string Id;         // "versus" / "raid"
        public string Name;
        public string En;
        public Color Color;
        public Color Light;
        public string Tagline;
        public string[] Lines;
    }

    class Card
    {
        public RectTransform Rect;
        public Image Frame;
        public Outline Line;
        public Image Glow;
    }

    class Ember
    {
        public RectTransform Rect;
        public Image Image;
        public float BaseX, Speed, Phase, Peak, Y;
    }

    // 前回選んだモード（この起動の間だけ覚える。Prototype の showTitle の prev）
    public static string LastMode { get; set; } = "versus";

    static TitleView open;
    public static bool IsOpen => open != null;

    TMP_FontAsset font;
    Canvas canvas;
    CanvasGroup group;
    RulesOverlayView rules;
    readonly List<Mode> modes = new List<Mode>();
    readonly List<Card> cards = new List<Card>();
    readonly List<Ember> embers = new List<Ember>();
    RectTransform logoRect;
    Image logoGlow;
    TextMeshProUGUI message;
    int focus;
    int openedFrame;
    float time;
    float chosenTime = -1f; // 決定した瞬間の演出

    // タイトル画面を開く（もう開いていれば何もしない）
    public static void Open()
    {
        if (open != null) return;
        open = new GameObject("TitleView").AddComponent<TitleView>();
    }

    void Awake()
    {
        openedFrame = Time.frameCount;
        font = RuntimeUi.LoadFont();
        RuntimeUi.EnsureEventSystem();
        HideLegacyCanvases();
        modes.AddRange(BuildModes());
        focus = Mathf.Max(0, modes.FindIndex(m => m.Id == LastMode));
        Build();
        Refresh();
        // ルール説明（対局の外でも開ける。左下の「ルール [Tab]」ボタンはこの画面の下に隠れるので、下のボタンから開く）
        rules = gameObject.AddComponent<RulesOverlayView>();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    // シーンに元からある Canvas（古いタイトル：「ゲームスタート」のボタン）を隠す。実行時に作る画面の Canvas は、画面のオブジェクトの子なので残る
    void HideLegacyCanvases()
    {
        foreach (Canvas c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
        {
            if (c != null && c.transform.parent == null && c.gameObject.scene == gameObject.scene) c.gameObject.SetActive(false);
        }
    }

    // タイトル画面に並べるモード（説明は戦闘型・ボスの定義から作る。Prototype の titleModes）
    static List<Mode> BuildModes()
    {
        List<string> rulesets = new List<string>();
        foreach (BattleRuleset r in BattleRulesets.All) rulesets.Add(RulesetLabel(r));
        List<string> bosses = new List<string>();
        List<string> raidRulesets = new List<string>();
        foreach (BossDef b in BossDefs.All)
        {
            if (b == null || !b.Implemented) continue;
            bosses.Add(b.Name);
            BattleRuleset r = BattleRulesets.ById(b.RulesetId);
            string label = r != null ? RulesetLabel(r) : b.RulesetId;
            if (!raidRulesets.Contains(label)) raidRulesets.Add(label);
        }
        return new List<Mode>
        {
            new Mode
            {
                Id = "versus", Name = "通常対局", En = "VERSUS", Color = RuntimeUi.Hex(0xffd54a), Light = RuntimeUi.Hex(0xffe082),
                Tagline = "1 対 1 で、敵の王将を討ち取れ",
                Lines = new[]
                {
                    "・2人で対戦（1 台の画面で）／ CPU と対戦",
                    $"・戦闘型：{string.Join("・", rulesets)}",
                    "・魔法スキル・必殺技・駒の錬成で戦う",
                },
            },
            new Mode
            {
                Id = "raid", Name = "討伐任務", En = "BOSS RAID", Color = RuntimeUi.Hex(0xff5252), Light = RuntimeUi.Hex(0xff8a80),
                Tagline = "強大なボスに 1 人で挑むボスバトル",
                Lines = new[]
                {
                    $"・ボス：{string.Join("・", bosses)}",
                    $"・戦闘型：{string.Join("・", raidRulesets)}",
                    "・駒の編成・錬成駒・魔法で HP を削り切れ",
                },
            },
        };
    }

    // 戦闘型の名前（手番制かリアルタイムか）
    static string RulesetLabel(BattleRuleset r) => $"{r.Name}（{(r.Realtime ? "リアルタイム" : "手番制")}）";

    // ------------------------------------------------------------
    //  毎フレーム
    // ------------------------------------------------------------

    // 上に重ねた画面（対戦モード・ボス選択・編成・ルール説明）が開いている間は、キーを読まない
    bool IsCovered => ModeSelectView.IsOpen || BossSelectView.IsOpen || FormationView.IsOpen || RulesOverlayView.IsBlockingInput;

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        time += dt;
        Animate(dt);

        Keyboard k = Keyboard.current;
        if (k == null || Time.frameCount == openedFrame || IsCovered) return;
        int n = modes.Count;
        if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) { focus = (focus + n - 1) % n; Refresh(); }
        if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) { focus = (focus + 1) % n; Refresh(); }
        if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.spaceKey.wasPressedThisFrame) Choose(focus);
    }

    // 火の粉・ロゴの光・選んでいるカードの光・開く演出
    void Animate(float dt)
    {
        if (group != null && group.alpha < 1f) group.alpha = Mathf.MoveTowards(group.alpha, 1f, dt * 4f);
        if (logoRect != null)
        {
            // ロゴが少し大きいところから落ち着く
            float t = Mathf.Clamp01(time / 0.55f);
            float s = 1f + 0.5f * (1f - t) * (1f - t);
            logoRect.localScale = new Vector3(s, s, 1f);
        }
        if (logoGlow != null) logoGlow.color = RuntimeUi.WithAlpha(RuntimeUi.Hex(0xff6f00), 0.26f + 0.1f * Mathf.Sin(time * 2f));

        float h = 1080f;
        foreach (Ember e in embers)
        {
            e.Y += e.Speed * dt;
            if (e.Y > h * 0.5f + 20f) e.Y -= h + 40f;
            e.Rect.anchoredPosition = new Vector2(e.BaseX + Mathf.Sin(time * 0.8f + e.Phase) * 26f, e.Y);
            float fade = Mathf.Clamp01((h * 0.5f - e.Y) / 180f); // 上の端へ近づくと消えていく
            Color c = e.Image.color;
            c.a = e.Peak * (0.55f + 0.45f * Mathf.Sin(time * 3f + e.Phase)) * fade;
            e.Image.color = c;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            Card card = cards[i];
            bool on = i == focus;
            // 開く演出：カードが下から現れる。選んでいるカードは少し大きい。決定した瞬間は一度ふくらむ
            float appear = Mathf.Clamp01((time - 0.15f - i * 0.1f) / 0.45f);
            float ease = 1f - (1f - appear) * (1f - appear);
            float x = CardX(i);
            card.Rect.anchoredPosition = new Vector2(x, CardY - 70f * (1f - ease));
            float target = on ? 1.035f : 1f;
            if (on && chosenTime >= 0f) target += 0.07f * Mathf.Clamp01(1f - (time - chosenTime) / 0.25f);
            float scale = Mathf.MoveTowards(card.Rect.localScale.x, target, dt * 0.6f);
            card.Rect.localScale = new Vector3(scale, scale, 1f);
            Color glow = modes[i].Color;
            glow.a = on ? 0.3f + 0.15f * Mathf.Sin(time * 4f) : 0f;
            card.Glow.color = glow;
        }
    }

    float CardX(int index)
    {
        int n = modes.Count;
        return -(CardW * n + CardGap * (n - 1)) * 0.5f + CardW * 0.5f + index * (CardW + CardGap);
    }

    void Refresh()
    {
        for (int i = 0; i < cards.Count; i++)
        {
            bool on = i == focus;
            cards[i].Frame.color = on ? CardFocusColor : CardColor;
            cards[i].Line.effectColor = on ? Color.white : RuntimeUi.WithAlpha(modes[i].Color, 0.8f);
            cards[i].Line.effectDistance = on ? new Vector2(5f, -5f) : new Vector2(3f, -3f);
        }
        RuntimeUi.ClearUiSelection();
    }

    // ------------------------------------------------------------
    //  決定
    // ------------------------------------------------------------

    void Choose(int index)
    {
        if (IsCovered) return;
        focus = index;
        chosenTime = time;
        Refresh();
        Mode mode = modes[index];
        LastMode = mode.Id;
        if (message != null) message.text = "";
        // 上に重ねて開く（この画面は下に残る。「◀ タイトルに戻る」で戻ってくる）
        if (mode.Id == "raid") BossSelectView.Open();
        else ModeSelectView.Open();
    }

    void OpenRules()
    {
        RuntimeUi.ClearUiSelection();
        if (rules != null && !IsCovered) rules.Open();
    }

    void ReplayKifu()
    {
        RuntimeUi.ClearUiSelection();
        if (IsCovered) return;
        string error = KifuGameOverPanel.ReplayFromFile(); // 始められたら対局のシーンへ移る
        if (message != null) message.text = error ?? "";
    }

    // ------------------------------------------------------------
    //  画面を作る
    // ------------------------------------------------------------

    void Build()
    {
        canvas = RuntimeUi.CreateCanvas("TitleCanvas", transform, SortingOrder);
        group = canvas.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        Transform root = canvas.transform;

        // 画面全体を覆う背景（下のシーンを隠す）
        Image bg = RuntimeUi.CreatePanel("Background", root, Background, false);
        RuntimeUi.Stretch(bg.rectTransform, new Vector2(-400f, -400f), new Vector2(400f, 400f));

        // 背景の飾り：大きな「将」の透かし・立ちのぼる火の粉
        TextMeshProUGUI mark = RuntimeUi.CreateText("Mark", root, "将", 900f, RuntimeUi.WithAlpha(RuntimeUi.Hex(0xff8f00), 0.04f), TextAlignmentOptions.Center, font);
        Center(mark.rectTransform, new Vector2(0f, -40f), new Vector2(1200f, 1100f));
        Color[] emberColors = { RuntimeUi.Hex(0xffb74d), RuntimeUi.Hex(0xff7043), RuntimeUi.Hex(0xffd54a) };
        for (int i = 0; i < 42; i++)
        {
            // 火の粉ごとに決まった乱数（毎回同じ並び）
            float R(int k) => Mathf.Abs(Mathf.Sin(i * 12.9898f + k * 78.233f) * 43758.5453f) % 1f;
            Image img = RuntimeUi.CreatePanel($"Ember_{i}", root, emberColors[i % 3], false);
            img.sprite = MagicSprites.Glow;
            img.raycastTarget = false;
            float size = 10f + R(0) * 26f;
            Center(img.rectTransform, Vector2.zero, new Vector2(size, size));
            embers.Add(new Ember
            {
                Rect = img.rectTransform, Image = img, BaseX = -1020f + R(1) * 2040f, Speed = 22f + R(2) * 54f, Phase = R(3) * Mathf.PI * 2f,
                Peak = 0.25f + R(4) * 0.5f, Y = -540f + R(5) * 1080f,
            });
        }

        // ロゴ
        logoGlow = RuntimeUi.CreatePanel("LogoGlow", root, RuntimeUi.WithAlpha(RuntimeUi.Hex(0xff6f00), 0.3f), false);
        logoGlow.sprite = MagicSprites.Glow;
        logoGlow.raycastTarget = false;
        Center(logoGlow.rectTransform, new Vector2(0f, 400f), new Vector2(1700f, 300f));
        TextMeshProUGUI logo = RuntimeUi.CreateText("Logo", root, "ULTIMATE SHOGI", 130f, Color.white, TextAlignmentOptions.Center, font);
        logo.characterSpacing = 14f;
        logo.textWrappingMode = TextWrappingModes.NoWrap;
        logo.enableVertexGradient = true;
        logo.colorGradient = new VertexGradient(RuntimeUi.Hex(0xfff8e1), RuntimeUi.Hex(0xfff8e1), RuntimeUi.Hex(0xff8f00), RuntimeUi.Hex(0xff8f00));
        logo.outlineColor = new Color32(42, 10, 0, 255);
        logo.outlineWidth = 0.22f;
        logoRect = logo.rectTransform;
        Center(logoRect, new Vector2(0f, 400f), new Vector2(1800f, 170f));
        TextMeshProUGUI jp = RuntimeUi.CreateText("LogoJp", root, "アルティメット将棋　〜常識をぶち壊せ〜", 30f, RuntimeUi.Hex(0xffe0b2), TextAlignmentOptions.Center, font);
        jp.characterSpacing = 6f;
        Center(jp.rectTransform, new Vector2(0f, 492f), new Vector2(1400f, 44f));
        TextMeshProUGUI sub = RuntimeUi.CreateText("Sub", root, "◆　魔法と必殺技が飛び交う、派手な特殊ルール将棋　◆", 30f, RuntimeUi.Hex(0xffe0b2), TextAlignmentOptions.Center, font);
        sub.characterSpacing = 4f;
        Center(sub.rectTransform, new Vector2(0f, 298f), new Vector2(1600f, 44f));
        TextMeshProUGUI prompt = RuntimeUi.CreateText("Prompt", root, "遊ぶモードを選んでください", 26f, RuntimeUi.Hex(0xbdbdbd), TextAlignmentOptions.Center, font);
        prompt.fontStyle = FontStyles.Normal;
        Center(prompt.rectTransform, new Vector2(0f, 238f), new Vector2(1200f, 40f));

        // モードのカード
        for (int i = 0; i < modes.Count; i++) cards.Add(BuildCard(root, modes[i], i));

        // 下のボタン
        float by = CardY - CardH * 0.5f - 62f;
        Button rulesButton = RuntimeUi.CreateButton("Rules", root, RuntimeUi.GrayButton, "ルール", 30f, font, out _);
        Center((RectTransform)rulesButton.transform, new Vector2(-215f, by), new Vector2(240f, 70f));
        rulesButton.onClick.AddListener(OpenRules);
        Button kifuButton = RuntimeUi.CreateButton("ReplayKifu", root, RuntimeUi.Hex(0x1565c0), "棋譜を読み込んで再生", 28f, font, out _);
        Center((RectTransform)kifuButton.transform, new Vector2(140f, by), new Vector2(400f, 70f));
        kifuButton.onClick.AddListener(ReplayKifu);
        message = RuntimeUi.CreateText("Message", root, KifuSession.LastReplayResult ?? "", 22f, RuntimeUi.Hex(0xffe082), TextAlignmentOptions.Left, font);
        Center(message.rectTransform, new Vector2(700f, by), new Vector2(660f, 70f));
        TextMeshProUGUI keys = RuntimeUi.CreateText("Keys", root, "← → キーで選択 ／ Enter で決定　　Tab：ルール", 22f, RuntimeUi.Hex(0x9e9e9e), TextAlignmentOptions.Center, font);
        keys.fontStyle = FontStyles.Normal;
        Center(keys.rectTransform, new Vector2(0f, by - 58f), new Vector2(1200f, 36f));
    }

    // モードのカード（絵・名前・説明）
    Card BuildCard(Transform parent, Mode mode, int index)
    {
        GameObject obj = new GameObject($"Card_{mode.Id}", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        Center(rt, new Vector2(CardX(index), CardY), new Vector2(CardW, CardH));

        // 選んでいるときの光（カードの後ろ）
        Image glow = RuntimeUi.CreatePanel("Glow", rt, RuntimeUi.WithAlpha(mode.Color, 0f), false);
        glow.raycastTarget = false;
        RuntimeUi.Stretch(glow.rectTransform, new Vector2(-18f, -18f), new Vector2(18f, 18f));

        Image frame = RuntimeUi.CreatePanel("Frame", rt, CardColor, false);
        RuntimeUi.Stretch(frame.rectTransform, Vector2.zero, Vector2.zero);
        Outline line = frame.gameObject.AddComponent<Outline>();
        Button button = frame.gameObject.AddComponent<Button>();
        button.targetGraphic = frame;
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None; // キーボードの選択はこのクラスが行う
        button.navigation = nav;
        button.transition = Selectable.Transition.None;
        button.onClick.AddListener(() => Choose(index));
        // マウスを乗せたら選ぶ
        TitleCardHover hover = frame.gameObject.AddComponent<TitleCardHover>();
        hover.Entered = () =>
        {
            if (IsCovered || focus == index) return;
            focus = index;
            Refresh();
        };

        // 絵の帯
        Image band = RuntimeUi.CreatePanel("Band", rt, RuntimeUi.WithAlpha(mode.Color, 0.16f), false);
        band.raycastTarget = false;
        Top(band.rectTransform, 4f, BandH);
        band.gameObject.AddComponent<RectMask2D>();
        if (mode.Id == "raid") RaidArt(band.transform);
        else VersusArt(band.transform);
        Image sep = RuntimeUi.CreatePanel("BandLine", rt, new Color(0f, 0f, 0f, 0.3f), false);
        sep.raycastTarget = false;
        Top(sep.rectTransform, 4f + BandH, 4f);

        TextMeshProUGUI en = RuntimeUi.CreateText("En", rt, mode.En, 24f, mode.Light, TextAlignmentOptions.Center, font);
        en.characterSpacing = 30f;
        Top(en.rectTransform, BandH + 18f, 34f);
        TextMeshProUGUI name = RuntimeUi.CreateText("Name", rt, mode.Name, 68f, Color.white, TextAlignmentOptions.Center, font);
        name.characterSpacing = 12f;
        name.enableVertexGradient = true;
        name.colorGradient = new VertexGradient(Color.white, Color.white, mode.Light, mode.Light);
        name.outlineColor = new Color32(0, 0, 0, 255);
        name.outlineWidth = 0.2f;
        Top(name.rectTransform, BandH + 50f, 92f);
        TextMeshProUGUI tagline = RuntimeUi.CreateText("Tagline", rt, mode.Tagline, 27f, Color.white, TextAlignmentOptions.Center, font);
        Top(tagline.rectTransform, BandH + 148f, 40f);
        Image rule = RuntimeUi.CreatePanel("Rule", rt, RuntimeUi.WithAlpha(mode.Color, 0.45f), false);
        rule.raycastTarget = false;
        Top(rule.rectTransform, BandH + 200f, 2f, 44f);
        for (int i = 0; i < mode.Lines.Length; i++)
        {
            TextMeshProUGUI t = RuntimeUi.CreateText($"Line_{i}", rt, mode.Lines[i], 23f, RuntimeUi.Hex(0xe0e0e0), TextAlignmentOptions.Left, font);
            t.fontStyle = FontStyles.Normal;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.enableAutoSizing = true;
            t.fontSizeMin = 14f;
            t.fontSizeMax = 23f;
            Top(t.rectTransform, BandH + 216f + i * 36f, 34f, 44f);
        }
        return new Card { Rect = rt, Frame = frame, Line = line, Glow = glow };
    }

    // 通常対局の絵：先手の王将と後手の玉将が向かい合う
    void VersusArt(Transform parent)
    {
        Piece(parent, "王", new Vector2(-150f, -6f), 190f, -7f, RuntimeUi.Hex(0x4fc3f7));
        Piece(parent, "玉", new Vector2(150f, 2f), 190f, 187f, RuntimeUi.Hex(0xff8a65));
        TextMeshProUGUI vs = RuntimeUi.CreateText("Vs", parent, "<i>VS</i>", 76f, Color.white, TextAlignmentOptions.Center, font);
        vs.richText = true;
        vs.enableVertexGradient = true;
        vs.colorGradient = new VertexGradient(Color.white, Color.white, RuntimeUi.Hex(0xff8f00), RuntimeUi.Hex(0xff8f00));
        vs.outlineColor = new Color32(42, 10, 0, 255);
        vs.outlineWidth = 0.25f;
        Center(vs.rectTransform, new Vector2(0f, -4f), new Vector2(240f, 110f));
    }

    // 討伐任務の絵：ボス（最初のボス）に、先手の駒が挑む
    void RaidArt(Transform parent)
    {
        Image glow = RuntimeUi.CreatePanel("BossGlow", parent, RuntimeUi.WithAlpha(RuntimeUi.Hex(0xff1744), 0.45f), false);
        glow.sprite = MagicSprites.Glow;
        glow.raycastTarget = false;
        Center(glow.rectTransform, Vector2.zero, new Vector2(620f, 420f));
        BossDef first = BossDefs.All.Count > 0 ? BossDefs.All[0] : null;
        Image boss = RuntimeUi.CreatePanel("Boss", parent, Color.white, false);
        boss.sprite = MagicSprites.BossSprite(first != null ? first.Id : "");
        boss.preserveAspect = true;
        boss.raycastTarget = false;
        Center(boss.rectTransform, new Vector2(0f, 4f), new Vector2(250f, 250f));
        Piece(parent, "飛", new Vector2(-230f, -62f), 90f, 10f, RuntimeUi.Hex(0x4fc3f7));
        Piece(parent, "王", new Vector2(-150f, -86f), 90f, 5f, RuntimeUi.Hex(0x4fc3f7));
        Piece(parent, "角", new Vector2(230f, -70f), 90f, -9f, RuntimeUi.Hex(0x4fc3f7));
    }

    // 飾りの駒（駒の形に文字を重ねる。aura は陣営の光。angle は傾き：180 で相手の向き）
    void Piece(Transform parent, string label, Vector2 pos, float size, float angle, Color aura)
    {
        GameObject obj = new GameObject($"Piece_{label}", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        Center(rt, pos, new Vector2(size, size));
        rt.localRotation = Quaternion.Euler(0f, 0f, angle);
        Image glow = RuntimeUi.CreatePanel("Aura", rt, RuntimeUi.WithAlpha(aura, 0.4f), false);
        glow.sprite = MagicSprites.Glow;
        glow.raycastTarget = false;
        Center(glow.rectTransform, Vector2.zero, new Vector2(size * 1.9f, size * 1.9f));
        Image body = RuntimeUi.CreatePanel("Body", rt, Color.white, false);
        body.sprite = MagicSprites.PieceShape;
        body.preserveAspect = true;
        body.raycastTarget = false;
        RuntimeUi.Stretch(body.rectTransform, Vector2.zero, Vector2.zero);
        TextMeshProUGUI t = RuntimeUi.CreateText("Label", rt, label, size * 0.46f, RuntimeUi.Hex(0x1a0f05), TextAlignmentOptions.Center, font);
        Center(t.rectTransform, new Vector2(0f, -size * 0.06f), new Vector2(size, size * 0.7f));
    }

    // 親の中心を基準に置く
    static void Center(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }

    // 親の上端から top の位置に、高さ height の帯を置く（左右は margin だけ空ける）
    static void Top(RectTransform rt, float top, float height, float margin = 4f)
    {
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = new Vector2(margin, -top - height);
        rt.offsetMax = new Vector2(-margin, -top);
    }
}
