using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

// 対戦モードの画面の「戦闘型」の選択（Prototype の対戦モードの画面の ruleset の選択肢）。
// Unity には対戦モードだけの画面がないので、対局の直前の画面（魔法スキルの編成画面）の上に実行時にパネルを作る。
//  ・選択肢は BattleRulesets.All の順に並ぶ（戦闘型を足すと自動で増える）。選んだものは MatchSetup.RulesetId に入り、対局画面（GameSystem）が読む
//  ・選んだ戦闘型の説明（BattleRuleset.Description）を下に出す
//  ・オンライン対戦は本将棋型だけ（GameSystem がオンラインなら本将棋型にする）
// DeckSelectController が自動で追加する（シーンに置かなくてよい）
public class BattleRulesetPicker : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む
    [SerializeField] private int sortingOrder = 50;

    private static readonly Color PanelColor = new Color(0.06f, 0.05f, 0.10f, 0.85f);
    private static readonly Color ButtonColor = new Color(0.22f, 0.25f, 0.32f, 1f);
    private static readonly Color SelectedColor = new Color(0.56f, 0.14f, 0.67f, 1f);

    private readonly List<(BattleRuleset ruleset, Image image)> options = new List<(BattleRuleset, Image)>();
    private TextMeshProUGUI description;

    void Start()
    {
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        EnsureEventSystem();
        Build();
        Refresh();
    }

    void Select(BattleRuleset ruleset)
    {
        MatchSetup.RulesetId = ruleset.Id;
        Debug.Log($"戦闘型：{ruleset.Name}");
        Refresh();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    void Refresh()
    {
        BattleRuleset current = MatchSetup.Ruleset;
        foreach (var o in options) o.image.color = o.ruleset == current ? SelectedColor : ButtonColor;
        if (description != null) description.text = current.Description;
    }

    void Build()
    {
        GameObject obj = new GameObject("BattleRulesetCanvas", typeof(RectTransform));
        obj.transform.SetParent(transform, false);
        Canvas canvas = obj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = obj.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        obj.AddComponent<GraphicRaycaster>();

        // 画面の右上：「戦闘型」＋選択肢のボタン＋説明
        GameObject panelObj = new GameObject("BattleRulesetPanel", typeof(RectTransform));
        RectTransform panel = (RectTransform)panelObj.transform;
        panel.SetParent(obj.transform, false);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(1f, 1f);
        panel.anchoredPosition = new Vector2(-20f, -20f);
        float width = 560f;
        panel.sizeDelta = new Vector2(width, 170f);
        Image bg = panelObj.AddComponent<Image>();
        bg.color = PanelColor;
        Outline line = panelObj.AddComponent<Outline>();
        line.effectColor = new Color(1f, 0.84f, 0.29f, 0.7f);
        line.effectDistance = new Vector2(3f, -3f);

        TextMeshProUGUI title = CreateText("Title", panel, "戦闘型", 28f, new Color(1f, 0.84f, 0.5f, 1f), TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(16f, -10f), new Vector2(140f, 40f));

        IReadOnlyList<BattleRuleset> all = BattleRulesets.All;
        float bw = (width - 170f - 10f * all.Count) / Mathf.Max(1, all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            BattleRuleset r = all[i];
            GameObject bObj = new GameObject($"Ruleset_{r.Id}", typeof(RectTransform));
            RectTransform brt = (RectTransform)bObj.transform;
            brt.SetParent(panel, false);
            Place(brt, new Vector2(160f + i * (bw + 10f), -8f), new Vector2(bw, 52f));
            Image image = bObj.AddComponent<Image>();
            Button button = bObj.AddComponent<Button>();
            button.targetGraphic = image;
            Navigation nav = button.navigation;
            nav.mode = Navigation.Mode.None;
            button.navigation = nav;
            TextMeshProUGUI label = CreateText("Label", brt, r.Name, 26f, Color.white, TextAlignmentOptions.Center);
            label.rectTransform.anchorMin = Vector2.zero;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero;
            label.rectTransform.offsetMax = Vector2.zero;
            button.onClick.AddListener(() => Select(r));
            options.Add((r, image));
        }

        description = CreateText("Description", panel, "", 20f, Color.white, TextAlignmentOptions.TopLeft);
        description.textWrappingMode = TextWrappingModes.Normal;
        description.fontStyle = FontStyles.Normal;
        Place(description.rectTransform, new Vector2(16f, -72f), new Vector2(width - 32f, 90f));
    }

    // 親の左上から pos（右・下が正。下は負の値）に size の大きさで置く
    static void Place(RectTransform rt, Vector2 pos, Vector2 size)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = pos;
        rt.sizeDelta = size;
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
        tmp.raycastTarget = false;
        return tmp;
    }

    static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null) return;
        GameObject obj = new GameObject("EventSystem");
        obj.AddComponent<EventSystem>();
        obj.AddComponent<InputSystemUIInputModule>();
    }
}
