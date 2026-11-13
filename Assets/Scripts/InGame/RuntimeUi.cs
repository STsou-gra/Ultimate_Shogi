using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using TMPro;

// 実行時に作る UI の部品（棋譜メニュー・結果画面の棋譜ボタンで使う。見た目は RulesOverlayView に合わせた）
public static class RuntimeUi
{
    public static readonly Color DimColor = new Color(0f, 0f, 0f, 0.75f);
    public static readonly Color BoxColor = new Color(0x1a / 255f, 0x0f / 255f, 0x26 / 255f, 0.97f);
    public static readonly Color GoldColor = Hex(0xffd54a);
    public static readonly Color GrayButton = Hex(0x37474f);

    public static TMP_FontAsset LoadFont()
    {
        return Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
    }

    public static Canvas CreateCanvas(string name, Transform parent, int order)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
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

    public static Image CreatePanel(string name, Transform parent, Color color, bool outline)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        if (outline)
        {
            Outline line = obj.AddComponent<Outline>();
            line.effectColor = WithAlpha(GoldColor, 0.9f);
            line.effectDistance = new Vector2(3f, -3f);
        }
        return image;
    }

    // ボタン（label の文字つき）
    public static Button CreateButton(string name, Transform parent, Color color, string label, float fontSize, TMP_FontAsset font, out TextMeshProUGUI text)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        Image image = obj.AddComponent<Image>();
        image.color = color;
        Button button = obj.AddComponent<Button>();
        button.targetGraphic = image;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.selectedColor = Color.white;
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        button.colors = colors;
        // キーボードの W A S D・矢印で UI の選択が動かないように（盤の操作と同じキーなので）
        Navigation nav = button.navigation;
        nav.mode = Navigation.Mode.None;
        button.navigation = nav;
        Outline outline = obj.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.5f);
        outline.effectDistance = new Vector2(2f, -2f);

        text = CreateText("Label", obj.transform, label, fontSize, Color.white, TextAlignmentOptions.Center, font);
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSize * 0.6f;
        text.fontSizeMax = fontSize;
        Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
        return button;
    }

    public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment, TMP_FontAsset font)
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
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    // 親の角（anchor：0,0 = 左下、0.5,1 = 上の中央 …）を基準に、size の矩形を offset（内側が正）の位置に置く
    public static void Place(RectTransform rt, Vector2 anchor, Vector2 offset, Vector2 size)
    {
        rt.anchorMin = anchor;
        rt.anchorMax = anchor;
        rt.pivot = anchor;
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(
            Mathf.Lerp(offset.x, -offset.x, anchor.x),
            Mathf.Lerp(offset.y, -offset.y, anchor.y));
    }

    // 親いっぱいに広げる（offsetMin = 左下の余白、offsetMax = 右上の余白（負の値で内側））
    public static void Stretch(RectTransform rt, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    // ボタンを押したあと、EventSystem の選択を外す（Space・Enter でボタンがもう一度押されないように）
    public static void ClearUiSelection()
    {
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    // クリックを受けるための EventSystem（シーンになければ作る）
    public static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        GameObject obj = new GameObject("EventSystem");
        obj.AddComponent<EventSystem>();
        obj.AddComponent<InputSystemUIInputModule>();
    }

    public static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    public static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
