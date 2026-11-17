using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

// 討伐任務のボス選択の画面（Prototype の src/scenes/BossSelectScene.js）。
// ボスの定義（BossDefs.All）の数だけカードを並べ、一番右に選べない「coming soon！」のカードを 1 枚置く（ボスを足すと自動で並ぶ）。
// まだ移植していないボス（BossDef.Implemented = false。大将軍）は、カードは出すが選べない（「準備中」）。
// 対戦モードの画面（ModeSelectView）の「討伐任務」から、タイトルの上に重ねて開く（シーンに置かなくてよい。Canvas は実行時に作る）。
// ボスを選ぶと編成の画面（FormationView）へ進む。結果画面の「ボス選択へ」は、タイトルのシーンを読み込んでからここを開く（OpenAfterLoadingTitle）。
// 操作：マウス、またはキーボード（← → で選ぶ・Enter で決定・Esc で戻る）
public class BossSelectView : MonoBehaviour
{
    const string TitleScene = "GameTitle";

    static readonly Color Dim = new Color(0f, 0f, 0f, 0.85f);
    static readonly Color Box = new Color(0x14 / 255f, 0x0b / 255f, 0x10 / 255f, 0.98f);
    static readonly Color Accent = RuntimeUi.Hex(0xff5252);
    static readonly Color Focus = RuntimeUi.Hex(0xffd54a);

    TMP_FontAsset font;
    readonly List<BossDef> bosses = new List<BossDef>();
    readonly List<Outline> cardLines = new List<Outline>();
    readonly List<RectTransform> cardRects = new List<RectTransform>();
    TextMeshProUGUI notice;
    int focus;
    int openedFrame;
    float shakeTime;
    int shakeIndex = -1;

    static BossSelectView open;

    // タイトルの上に開く（もう開いていれば何もしない）
    public static void Open()
    {
        if (open != null) return;
        open = new GameObject("BossSelectView").AddComponent<BossSelectView>();
    }

    // タイトルのシーンを読み込んでから開く（結果画面の「ボス選択へ」）
    public static void OpenAfterLoadingTitle()
    {
        SceneManager.sceneLoaded += OnTitleLoaded;
        SceneManager.LoadScene(TitleScene);
    }

    static void OnTitleLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnTitleLoaded;
        if (scene.name == TitleScene) Open();
    }

    void Awake()
    {
        openedFrame = Time.frameCount;
        font = RuntimeUi.LoadFont();
        RuntimeUi.EnsureEventSystem();
        bosses.AddRange(BossDefs.All);
        focus = Mathf.Max(0, bosses.FindIndex(b => b.Id == MatchSetup.LastBossId && b.Implemented));
        if (!bosses[focus].Implemented) focus = Mathf.Max(0, bosses.FindIndex(b => b.Implemented));
        Build();
        Refresh();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    void Update()
    {
        if (shakeTime > 0f && shakeIndex >= 0 && shakeIndex < cardRects.Count)
        {
            shakeTime = Mathf.Max(0f, shakeTime - Time.unscaledDeltaTime);
            RectTransform rt = cardRects[shakeIndex];
            Vector2 p = rt.anchoredPosition;
            p.y = CardY + Mathf.Sin(shakeTime * 60f) * 8f * (shakeTime / 0.3f);
            rt.anchoredPosition = p;
        }

        Keyboard k = Keyboard.current;
        if (k == null || Time.frameCount == openedFrame) return; // 開いたキー（Enter）を決定として読まない
        int n = bosses.Count + 1; // 最後は「coming soon！」
        if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) { focus = (focus + n - 1) % n; Refresh(); }
        if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) { focus = (focus + 1) % n; Refresh(); }
        if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) Choose(focus);
        if (k.escapeKey.wasPressedThisFrame) Back();
    }

    // ------------------------------------------------------------
    //  決定
    // ------------------------------------------------------------

    void Choose(int index)
    {
        focus = index;
        Refresh();
        RuntimeUi.ClearUiSelection();
        if (index >= bosses.Count)
        {
            Shake(index, "新しいボスを準備中です……");
            return;
        }
        BossDef def = bosses[index];
        if (!def.Implemented)
        {
            Shake(index, $"{def.Name}は準備中です（まだ選べません）");
            return;
        }
        MatchSetup.LastBossId = def.Id;
        FormationView.Open(def);
        Destroy(gameObject);
    }

    void Shake(int index, string message)
    {
        shakeIndex = index;
        shakeTime = 0.3f;
        if (notice != null) notice.text = message;
    }

    // 対戦モードの画面へ戻る
    void Back()
    {
        ModeSelectView.Open();
        Destroy(gameObject);
    }

    void Refresh()
    {
        for (int i = 0; i < cardLines.Count; i++)
        {
            bool on = i == focus;
            cardLines[i].effectColor = on ? Focus : new Color(1f, 1f, 1f, 0.25f);
            cardLines[i].effectDistance = on ? new Vector2(5f, -5f) : new Vector2(2f, -2f);
        }
    }

    // ------------------------------------------------------------
    //  画面を作る
    // ------------------------------------------------------------

    const float CardW = 400f, CardH = 620f, Gap = 36f, CardY = -30f;

    void Build()
    {
        Canvas canvas = RuntimeUi.CreateCanvas("BossSelectCanvas", transform, 500);
        Image dim = RuntimeUi.CreatePanel("Dim", canvas.transform, Dim, false);
        RuntimeUi.Stretch(dim.rectTransform, Vector2.zero, Vector2.zero);

        Image box = RuntimeUi.CreatePanel("Box", canvas.transform, Box, false);
        Outline boxLine = box.gameObject.AddComponent<Outline>();
        boxLine.effectColor = RuntimeUi.WithAlpha(Accent, 0.9f);
        boxLine.effectDistance = new Vector2(3f, -3f);
        box.rectTransform.sizeDelta = new Vector2(1760f, 960f);

        TextMeshProUGUI title = RuntimeUi.CreateText("Title", box.transform, "討伐任務：ボス選択", 56f, new Color(1f, 0.8f, 0.78f, 1f), TextAlignmentOptions.Left, font);
        Place(title.rectTransform, new Vector2(-380f, 410f), new Vector2(900f, 80f));
        TextMeshProUGUI step = RuntimeUi.CreateText("Step", box.transform, "STEP 1 / 3", 32f, Color.white, TextAlignmentOptions.Right, font);
        Place(step.rectTransform, new Vector2(640f, 410f), new Vector2(400f, 60f));
        TextMeshProUGUI guide = RuntimeUi.CreateText("Guide", box.transform,
            "戦うボスを選んでください。プレイヤー 1 人（先手・盤の手前）で、ボスの HP を 0 にすれば討伐成功。", 26f, new Color(0.87f, 0.87f, 0.87f, 1f), TextAlignmentOptions.Left, font);
        Place(guide.rectTransform, new Vector2(0f, 345f), new Vector2(1660f, 50f));

        int n = bosses.Count + 1;
        float x0 = -(CardW * n + Gap * (n - 1)) * 0.5f + CardW * 0.5f;
        for (int i = 0; i < bosses.Count; i++) BossCard(box.transform, bosses[i], i, new Vector2(x0 + i * (CardW + Gap), CardY));
        ComingSoonCard(box.transform, bosses.Count, new Vector2(x0 + bosses.Count * (CardW + Gap), CardY));

        notice = RuntimeUi.CreateText("Notice", box.transform, "", 26f, RuntimeUi.Hex(0xffe082), TextAlignmentOptions.Center, font);
        Place(notice.rectTransform, new Vector2(200f, -410f), new Vector2(1100f, 50f));

        Button back = RuntimeUi.CreateButton("Back", box.transform, RuntimeUi.GrayButton, "◀ 対戦モードに戻る", 28f, font, out _);
        Place((RectTransform)back.transform, new Vector2(-670f, -410f), new Vector2(360f, 70f));
        back.onClick.AddListener(Back);

        TextMeshProUGUI keys = RuntimeUi.CreateText("Keys", canvas.transform, "← → で選んで Enter／クリック　　Esc：戻る", 26f, new Color(1f, 1f, 1f, 0.7f), TextAlignmentOptions.Center, font);
        Place(keys.rectTransform, new Vector2(0f, -510f), new Vector2(1400f, 44f));
    }

    void BossCard(Transform parent, BossDef def, int index, Vector2 pos)
    {
        Color color = RuntimeUi.Hex(def.Color);
        bool ready = def.Implemented;
        Button card = Card(parent, def.Id, index, pos, ready ? new Color(0x24 / 255f, 0x0a / 255f, 0x0c / 255f, 1f) : new Color(0.1f, 0.1f, 0.12f, 1f));

        Image band = RuntimeUi.CreatePanel("Band", card.transform, RuntimeUi.WithAlpha(color, ready ? 0.3f : 0.12f), false);
        band.raycastTarget = false;
        Place(band.rectTransform, new Vector2(0f, 180f), new Vector2(CardW - 8f, 252f));

        // 絵（赤鬼王は盤上と同じ絵。まだ絵のないボスは頭文字）
        if (def.Id == BossDefs.Akaoni.Id)
        {
            Image art = RuntimeUi.CreatePanel("Art", card.transform, Color.white, false);
            art.sprite = MagicSprites.Akaoni;
            art.preserveAspect = true;
            art.raycastTarget = false;
            Place(art.rectTransform, new Vector2(0f, 180f), new Vector2(230f, 230f));
        }
        else
        {
            TextMeshProUGUI glyph = RuntimeUi.CreateText("Glyph", card.transform, def.Name.Substring(0, 1), 150f, RuntimeUi.WithAlpha(color, ready ? 1f : 0.5f), TextAlignmentOptions.Center, font);
            Place(glyph.rectTransform, new Vector2(0f, 180f), new Vector2(260f, 220f));
        }

        Color text = ready ? Color.white : new Color(0.62f, 0.62f, 0.62f, 1f);
        TextMeshProUGUI name = RuntimeUi.CreateText("Name", card.transform, def.Name, 46f, text, TextAlignmentOptions.Center, font);
        Place(name.rectTransform, new Vector2(0f, 20f), new Vector2(CardW - 20f, 60f));
        TextMeshProUGUI sub = RuntimeUi.CreateText("Title", card.transform, def.Title ?? "", 22f, ready ? new Color(1f, 0.8f, 0.74f, 1f) : text, TextAlignmentOptions.Center, font);
        Place(sub.rectTransform, new Vector2(0f, -24f), new Vector2(CardW - 20f, 34f));

        BattleRuleset r = BattleRulesets.ById(def.RulesetId);
        string info = ready
            ? $"HP {def.Hp}　｜　{def.W}×{def.H} マス　｜　{(r != null ? r.Name : def.RulesetId)}\nチームコスト上限 {def.CostLimit}・錬成駒 {def.ForgeSlots} 種"
            : "<color=#FFD54A>準備中</color>（まだ選べません）";
        TextMeshProUGUI infoText = RuntimeUi.CreateText("Info", card.transform, info, 22f, text, TextAlignmentOptions.Center, font);
        infoText.richText = true;
        Place(infoText.rectTransform, new Vector2(0f, -82f), new Vector2(CardW - 20f, 70f));

        TextMeshProUGUI desc = RuntimeUi.CreateText("Description", card.transform, def.Description, 20f, ready ? new Color(0.88f, 0.88f, 0.88f, 1f) : text, TextAlignmentOptions.TopLeft, font);
        desc.fontStyle = FontStyles.Normal;
        Place(desc.rectTransform, new Vector2(0f, -210f), new Vector2(CardW - 44f, 170f));
    }

    void ComingSoonCard(Transform parent, int index, Vector2 pos)
    {
        Button card = Card(parent, "ComingSoon", index, pos, new Color(0x15 / 255f, 0x12 / 255f, 0x1a / 255f, 1f));
        TextMeshProUGUI q = RuntimeUi.CreateText("Question", card.transform, "？", 190f, new Color(0.26f, 0.26f, 0.26f, 1f), TextAlignmentOptions.Center, font);
        Place(q.rectTransform, new Vector2(0f, 170f), new Vector2(300f, 240f));
        TextMeshProUGUI t = RuntimeUi.CreateText("Soon", card.transform, "coming soon！", 44f, new Color(0.62f, 0.62f, 0.62f, 1f), TextAlignmentOptions.Center, font);
        Place(t.rectTransform, new Vector2(0f, 10f), new Vector2(CardW - 20f, 60f));
        TextMeshProUGUI note = RuntimeUi.CreateText("Note", card.transform, "新しいボスを準備中……", 22f, new Color(0.46f, 0.46f, 0.46f, 1f), TextAlignmentOptions.Center, font);
        Place(note.rectTransform, new Vector2(0f, -50f), new Vector2(CardW - 20f, 40f));
    }

    Button Card(Transform parent, string name, int index, Vector2 pos, Color color)
    {
        GameObject obj = new GameObject($"Card_{name}", typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        RectTransform rt = (RectTransform)obj.transform;
        Place(rt, pos, new Vector2(CardW, CardH));
        Image img = obj.AddComponent<Image>();
        img.color = color;
        Button b = obj.AddComponent<Button>();
        b.targetGraphic = img;
        Navigation nav = b.navigation;
        nav.mode = Navigation.Mode.None; // キーボードの選択はこのクラスが行う
        b.navigation = nav;
        Outline line = obj.AddComponent<Outline>();
        cardLines.Add(line);
        cardRects.Add(rt);
        b.onClick.AddListener(() => Choose(index));
        return b;
    }

    // 親の中心を基準に置く
    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
    }
}
