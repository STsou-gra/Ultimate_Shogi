using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

// 編成画面の魔法スキルカード（Prototype の src/ui/SkillCard.js の SkillCard）。
// 選択パネルの選択肢ボタン（SkillOptionButton のプレハブ）を、実行時にカードの見た目にする：
//  ・上から ランク・名前・説明の書き出し（入りきらなければ「…」で切る）、下の段に コスト（左）と「詳細」ボタン（右）
//  ・説明の全文と補足は「詳細」（onDetail）で SkillDetailPopup に出す。「詳細」を押してもカードの選択は切り替わらない
//  ・この枠に入っている魔法は「選択中」、同じプレイヤーのほかの枠に入っている魔法は「選択済み」（押せない）
//  ・キーボードで選んでいるカードは金色の枠（DeckSelectController が SetFocused で付ける）
// 説明の文は RulesBook.MagicDescription（ルール説明の一覧と同じ文）
public class SkillCardView : MonoBehaviour, IPointerEnterHandler
{
    public const float Width = 250f;
    public const float Height = 160f;

    static readonly Color CardColor = RuntimeUi.Hex(0x241736);
    static readonly Color SelectedColor = RuntimeUi.Hex(0x3a2a58);
    static readonly Color DetailColor = RuntimeUi.Hex(0x4527a0);
    static readonly Color FocusColor = RuntimeUi.Hex(0xffd54a);

    public SkillDefinition Skill { get; private set; }
    public Button Button { get; private set; }
    public Button DetailButton { get; private set; }

    Image background;
    Outline outline;
    TextMeshProUGUI badge;
    Action<SkillCardView> onHover;
    bool selected, usedElsewhere, focused;

    // obj（選択肢ボタン）をカードにする。onDetail：「詳細」が押されたとき、onHover：マウスが乗ったとき（キーボードの選択を合わせる）
    public static SkillCardView Decorate(GameObject obj, SkillDefinition skill, Action<SkillCardView> onDetail, Action<SkillCardView> onHover)
    {
        SkillCardView card = obj.GetComponent<SkillCardView>();
        if (card == null) card = obj.AddComponent<SkillCardView>();
        card.Build(skill, onDetail, onHover);
        return card;
    }

    void Build(SkillDefinition skill, Action<SkillCardView> onDetail, Action<SkillCardView> onHover)
    {
        Skill = skill;
        this.onHover = onHover;
        Button = GetComponent<Button>();
        if (Button != null)
        {
            Navigation nav = Button.navigation;
            nav.mode = Navigation.Mode.None; // キーボードの選択は DeckSelectController が行う（W A S D・矢印で uGUI の選択が動かないように）
            Button.navigation = nav;
        }
        RectTransform rt = (RectTransform)transform;
        rt.sizeDelta = new Vector2(Width, Height);

        background = GetComponent<Image>();
        if (background == null) background = gameObject.AddComponent<Image>();
        background.sprite = null;
        background.type = Image.Type.Simple;
        outline = GetComponent<Outline>();
        if (outline == null) outline = gameObject.AddComponent<Outline>();

        // プレハブの文字（名前に使う）とフォント
        TextMeshProUGUI name = GetComponentInChildren<TextMeshProUGUI>(true);
        TMP_FontAsset font = name != null && name.font != null ? name.font : RuntimeUi.LoadFont();
        if (name == null) name = RuntimeUi.CreateText("Name", transform, "", 22f, Color.white, TextAlignmentOptions.TopLeft, font);

        Color rankColor = SkillRanks.ColorOf(skill.rank);
        TextMeshProUGUI tag = RuntimeUi.CreateText("Rank", transform, SkillRanks.Label(skill.rank), 13f, rankColor, TextAlignmentOptions.TopLeft, font);
        tag.characterSpacing = 8f;
        RuntimeUi.Place(tag.rectTransform, new Vector2(0f, 1f), new Vector2(12f, 9f), new Vector2(Width - 110f, 18f));

        badge = RuntimeUi.CreateText("Badge", transform, "", 13f, FocusColor, TextAlignmentOptions.TopRight, font);
        RuntimeUi.Place(badge.rectTransform, new Vector2(1f, 1f), new Vector2(12f, 9f), new Vector2(96f, 18f));

        name.text = skill.skillName;
        name.fontSize = 22f;
        name.fontStyle = FontStyles.Bold;
        name.color = Color.white;
        name.alignment = TextAlignmentOptions.TopLeft;
        name.enableAutoSizing = true;
        name.fontSizeMin = 14f;
        name.fontSizeMax = 22f;
        name.textWrappingMode = TextWrappingModes.NoWrap;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.raycastTarget = false;
        RuntimeUi.Place(name.rectTransform, new Vector2(0f, 1f), new Vector2(12f, 28f), new Vector2(Width - 24f, 30f));

        // 説明の書き出し（入りきらなければ「…」で切る。全文は「詳細」）
        TextMeshProUGUI desc = RuntimeUi.CreateText("Description", transform, RulesBook.MagicDescription(skill), 13f, RuntimeUi.Hex(0xdddddd), TextAlignmentOptions.TopLeft, font);
        desc.fontStyle = FontStyles.Normal;
        desc.lineSpacing = -8f;
        desc.overflowMode = TextOverflowModes.Ellipsis;
        RuntimeUi.Place(desc.rectTransform, new Vector2(0f, 1f), new Vector2(12f, 60f), new Vector2(Width - 24f, Height - 60f - 40f));

        // 下の段：コスト（左）と「詳細」（右）
        TextMeshProUGUI cost = RuntimeUi.CreateText("Cost", transform, $"<size=24>{skill.cost}</size> エナジー", 12f, RuntimeUi.GoldColor, TextAlignmentOptions.BottomLeft, font);
        RuntimeUi.Place(cost.rectTransform, new Vector2(0f, 0f), new Vector2(12f, 8f), new Vector2(Width - 100f, 30f));

        DetailButton = RuntimeUi.CreateButton("DetailButton", transform, DetailColor, "詳細", 15f, font, out _);
        RuntimeUi.Place((RectTransform)DetailButton.transform, new Vector2(1f, 0f), new Vector2(12f, 9f), new Vector2(60f, 26f));
        // 子のボタンがクリックを受けるので、カードの選択（親の Button）は切り替わらない
        DetailButton.onClick.AddListener(() => { RuntimeUi.ClearUiSelection(); onDetail?.Invoke(this); });

        Redraw();
    }

    public void SetState(bool selected, bool usedElsewhere)
    {
        this.selected = selected;
        this.usedElsewhere = usedElsewhere;
        if (Button != null) Button.interactable = !usedElsewhere;
        Redraw();
    }

    public void SetFocused(bool focused)
    {
        this.focused = focused;
        Redraw();
    }

    public bool CanChoose => !usedElsewhere;

    void Redraw()
    {
        if (Skill == null) return;
        if (background != null) background.color = selected ? SelectedColor : CardColor;
        if (outline != null)
        {
            outline.effectColor = focused ? FocusColor : selected ? Skill.color : RuntimeUi.WithAlpha(Skill.color, 0.55f);
            outline.effectDistance = focused || selected ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
        }
        if (badge != null)
        {
            badge.text = selected ? "● 選択中" : usedElsewhere ? "選択済み" : "";
            badge.color = selected ? RuntimeUi.GoldColor : RuntimeUi.Hex(0x9e9e9e);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        onHover?.Invoke(this);
    }
}
