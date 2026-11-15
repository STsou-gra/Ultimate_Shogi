using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 魔法スキルの説明のポップアップ（Prototype の src/ui/SkillCard.js の createSkillDetailPopup）。
// 編成画面のカードの「詳細」で開く：ランク・手番（超常魔法は「1対局1回」）・名前・コスト・説明の全文・補足。
//  ・カード（anchor）の下に出し、入らなければ上に出す。左右・上下は画面に収める
//  ・「閉じる」・ポップアップの外のクリック・Close()（Esc は DeckSelectController が呼ぶ）で閉じる。外側は暗くなり、後ろのカードは押せない
//  ・同時に 1 つだけ（開き直すと前のものは閉じる）
// 文はルール説明と同じ（RulesBook.MagicTagText・MagicDescription・MagicDetailLines）。Canvas は実行時に作る
public class SkillDetailPopup : MonoBehaviour
{
    const float BoxWidth = 460f;
    const float Pad = 20f;
    const float Margin = 12f;
    const int SortingOrder = 650; // 編成画面の上、ルール説明（RulesOverlayView）・対戦モードの画面より下

    static SkillDetailPopup current;

    Action onClosed;
    bool closing;

    public static bool IsOpen => current != null;
    public static SkillDefinition OpenSkill => current != null ? current.skill : null;

    SkillDefinition skill;

    // skill の説明を anchor（カード）の近くに開く。src は補足を作るのに使う（鳥の錬成の有無など。null でもよい）。
    // onClosed：閉じたとき（「閉じる」・外側のクリック・Close）
    public static SkillDetailPopup Open(SkillDefinition skill, RectTransform anchor, RulesSources src, Action onClosed = null)
    {
        CloseCurrent();
        if (skill == null) return null;
        RuntimeUi.EnsureEventSystem();
        SkillDetailPopup popup = new GameObject("SkillDetailPopup").AddComponent<SkillDetailPopup>();
        popup.skill = skill;
        popup.onClosed = onClosed;
        popup.Build(anchor, src);
        current = popup;
        return popup;
    }

    public static void CloseCurrent()
    {
        if (current != null) current.Close();
    }

    public void Close()
    {
        if (closing) return;
        closing = true;
        if (current == this) current = null;
        RuntimeUi.ClearUiSelection();
        Destroy(gameObject);
        onClosed?.Invoke();
    }

    void OnDestroy()
    {
        if (current == this) current = null;
    }

    void Build(RectTransform anchor, RulesSources src)
    {
        TMP_FontAsset font = RuntimeUi.LoadFont();
        Canvas canvas = RuntimeUi.CreateCanvas("SkillDetailCanvas", transform, SortingOrder);
        RectTransform root = (RectTransform)canvas.transform;

        // 外側（クリックで閉じる。後ろのカードは押せない）
        Image shade = RuntimeUi.CreatePanel("Shade", root, new Color(0f, 0f, 0f, 0.35f), false);
        RuntimeUi.Stretch(shade.rectTransform, Vector2.zero, Vector2.zero);
        Button shadeButton = shade.gameObject.AddComponent<Button>();
        shadeButton.transition = Selectable.Transition.None;
        Navigation nav = shadeButton.navigation;
        nav.mode = Navigation.Mode.None;
        shadeButton.navigation = nav;
        shadeButton.onClick.AddListener(Close);

        // 本体（中のクリックでは閉じない：Image が外側のクリックを受け止める）
        Image box = RuntimeUi.CreatePanel("Box", root, RuntimeUi.BoxColor, false);
        Outline frame = box.gameObject.AddComponent<Outline>();
        frame.effectColor = skill.color;
        frame.effectDistance = new Vector2(3f, -3f);
        RectTransform boxRt = box.rectTransform;
        Image bar = RuntimeUi.CreatePanel("Bar", boxRt, skill.color, false);
        bar.raycastTarget = false;

        float inner = BoxWidth - Pad * 2f;
        float y = 14f;

        TextMeshProUGUI tag = RuntimeUi.CreateText("Tag", boxRt, RulesBook.MagicTagText(skill), 14f, SkillRanks.ColorOf(skill.rank), TextAlignmentOptions.TopLeft, font);
        tag.characterSpacing = 6f;
        RuntimeUi.Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner, 20f));
        y += 22f;

        TextMeshProUGUI cost = RuntimeUi.CreateText("Cost", boxRt, $"{skill.cost} エナジー", 22f, RuntimeUi.GoldColor, TextAlignmentOptions.TopRight, font);
        RuntimeUi.Place(cost.rectTransform, new Vector2(1f, 1f), new Vector2(Pad, y + 4f), new Vector2(150f, 30f));
        TextMeshProUGUI name = RuntimeUi.CreateText("Name", boxRt, skill.skillName, 28f, Color.white, TextAlignmentOptions.TopLeft, font);
        name.enableAutoSizing = true;
        name.fontSizeMin = 16f;
        name.fontSizeMax = 28f;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(inner - 160f, 38f));
        y += 44f;

        y = AddBlock(boxRt, "Description", RulesBook.MagicDescription(skill), 17f, RuntimeUi.Hex(0xeeeeee), FontStyles.Bold, font, inner, y);

        string details = FormatDetails(RulesBook.MagicDetailLines(skill, src));
        if (details.Length > 0)
        {
            y = AddBlock(boxRt, "Details", details, 14f, RuntimeUi.Hex(0xbdbdbd), FontStyles.Normal, font, inner, y + 10f);
        }

        Button close = RuntimeUi.CreateButton("CloseButton", boxRt, RuntimeUi.GrayButton, "閉じる", 18f, font, out _);
        RuntimeUi.Place((RectTransform)close.transform, new Vector2(1f, 1f), new Vector2(Pad, y + 14f), new Vector2(120f, 38f));
        close.onClick.AddListener(Close);
        float height = y + 14f + 38f + Pad;

        boxRt.sizeDelta = new Vector2(BoxWidth, height);
        RuntimeUi.Place(bar.rectTransform, new Vector2(0.5f, 1f), Vector2.zero, new Vector2(BoxWidth - 24f, 5f));
        PlaceNear(root, boxRt, anchor, height);
    }

    // 折り返す文を y から置き、次の y を返す
    static float AddBlock(RectTransform parent, string name, string text, float size, Color color, FontStyles style, TMP_FontAsset font, float width, float y)
    {
        TextMeshProUGUI t = RuntimeUi.CreateText(name, parent, text, size, color, TextAlignmentOptions.TopLeft, font);
        t.fontStyle = style;
        t.lineSpacing = -4f;
        float h = Mathf.Ceil(t.GetPreferredValues(text, width, 0f).y) + 2f;
        RuntimeUi.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(Pad, y), new Vector2(width, h));
        return y + h;
    }

    // 補足の行（RulesLine）を 1 項目 1 行に。字下げ 1 は「・」、2 は 1 字下げて「－」（折り返した行もそろえる）
    public static string FormatDetails(List<RulesLine> lines)
    {
        StringBuilder sb = new StringBuilder();
        if (lines == null) return "";
        foreach (RulesLine line in lines)
        {
            if (line == null || string.IsNullOrEmpty(line.Text)) continue;
            if (sb.Length > 0) sb.Append('\n');
            if (line.Kind == RulesLineKind.Sub) sb.Append(line.Text);
            else if (line.Indent >= 2) sb.Append("<indent=1em>－<indent=2em>").Append(line.Text).Append("</indent>");
            else sb.Append("・<indent=1em>").Append(line.Text).Append("</indent>");
        }
        return sb.ToString();
    }

    // anchor（カード）の下に置く（入らなければ上）。画面からはみ出さないように寄せる
    static void PlaceNear(RectTransform root, RectTransform box, RectTransform anchor, float height)
    {
        Canvas.ForceUpdateCanvases();
        Rect area = root.rect; // 中心が原点
        box.anchorMin = box.anchorMax = new Vector2(0.5f, 0.5f);
        box.pivot = new Vector2(0f, 1f); // 左上
        float minX = area.xMin + Margin, maxX = area.xMax - Margin - BoxWidth;
        float minTop = area.yMin + Margin + height, maxTop = area.yMax - Margin;

        Vector2 left = Vector2.zero, right = Vector2.zero;
        bool hasAnchor = anchor != null && CardRect(root, anchor, out left, out right);
        float x, top;
        if (!hasAnchor)
        {
            x = -BoxWidth / 2f;
            top = height / 2f;
        }
        else
        {
            // left = カードの左下、right = 右上（root のローカル座標）
            x = (left.x + right.x) / 2f - BoxWidth / 2f;
            top = left.y - 10f;                                 // カードの下
            if (top - height < area.yMin + Margin) top = right.y + 10f + height; // 入らなければカードの上
        }
        x = Mathf.Clamp(x, minX, Mathf.Max(minX, maxX));
        top = Mathf.Clamp(top, Mathf.Min(minTop, maxTop), maxTop);
        box.anchoredPosition = new Vector2(x, top);
    }

    // カードの左下・右上を root のローカル座標で
    static bool CardRect(RectTransform root, RectTransform anchor, out Vector2 bottomLeft, out Vector2 topRight)
    {
        bottomLeft = topRight = Vector2.zero;
        Canvas cardCanvas = anchor.GetComponentInParent<Canvas>();
        if (cardCanvas == null) return false;
        Canvas rootCanvas = cardCanvas.rootCanvas;
        Camera cam = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
        Vector3[] corners = new Vector3[4];
        anchor.GetWorldCorners(corners); // 左下・左上・右上・右下
        Vector2 s0 = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
        Vector2 s2 = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
        bool ok0 = RectTransformUtility.ScreenPointToLocalPointInRectangle(root, s0, null, out bottomLeft);
        bool ok2 = RectTransformUtility.ScreenPointToLocalPointInRectangle(root, s2, null, out topRight);
        return ok0 && ok2;
    }
}
