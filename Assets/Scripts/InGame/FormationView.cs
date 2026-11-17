using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using TMPro;

// 討伐任務の編成の画面（Prototype の src/scenes/FormationScene.js）：
//  ① 駒の編成と配置 … チームコスト制。自陣（手前 3 段）に駒を並べる。コストは RaidRules.TeamCost（歩 1・香 3・桂 3・銀 4・金 4・角 6・飛 6・王 0。
//                        オリジナル駒は 火薬 3・狙撃手 8・鳥 2）、上限はボスの CostLimit（赤鬼王は 50）。王将は必ず 1 枚。
//                        「おまかせ配置」（本将棋の初期配置から、上限に入るものだけ）・「クリア」。コストが上限を超えても置ける（赤字と理由を出し、先へ進めなくする）
//  ② 錬成駒           … 対局中に錬成できる駒を、初期配置に置いていない駒の種類からボスの ForgeSlots 種まで選ぶ
//                        （火薬・狙撃手・鳥・巨人と、通常の駒。初期配置に置いた駒の種類は選ばなくても錬成できる）
// 決めると MatchSetup.SetBossRaid でボスと編成を入れ（前回の編成としてボスごとに覚える）、魔法スキルの編成の画面（OrganizationScene）へ進む。
// 検査は RaidRules.Validate（対局・棋譜・テストと同じ）。ボス選択の画面（BossSelectView）から、タイトルの上に重ねて開く（Canvas は実行時に作る）。
//
// ① の操作：駒のボタンを押して選ぶ（もう一度押すと選択をやめる）→ 自陣のマスを押して置く（選んだままなので、続けて置ける。王将は 1 枚だけなので、
//   置くと前の王将は外れる）。駒のあるマスを押すと、同じ駒を選んでいれば外し、違う駒を選んでいれば置き換える。何も選んでいないときは外す。
//   キーボード：Enter で次へ・Esc で戻る
public class FormationView : MonoBehaviour
{
    const string NextScene = "OrganizationScene";

    static readonly Color Dim = new Color(0f, 0f, 0f, 0.85f);
    static readonly Color Box = new Color(0x14 / 255f, 0x0b / 255f, 0x10 / 255f, 0.98f);
    static readonly Color Accent = RuntimeUi.Hex(0xff5252);
    static readonly Color CellOwn = new Color(0.16f, 0.26f, 0.42f, 1f);
    static readonly Color CellOther = new Color(0.12f, 0.12f, 0.15f, 1f);
    static readonly Color CellBoss = new Color(0.45f, 0.1f, 0.12f, 1f);
    static readonly Color PieceColor = new Color(0.96f, 0.87f, 0.62f, 1f);
    static readonly Color ButtonOff = RuntimeUi.GrayButton;
    static readonly Color ButtonOn = RuntimeUi.Hex(0x8e24aa);

    const float Cell = 74f;
    static readonly Vector2 BoardCenter = new Vector2(-500f, -30f);

    TMP_FontAsset font;
    BossDef def;
    List<RaidPlacement> formation = new List<RaidPlacement>();
    List<PieceType> picks = new List<PieceType>();
    PieceType? selected;
    int page = 1;
    string notice = "";
    int openedFrame;

    TextMeshProUGUI titleText, stepText, guideText, opText, costText, errorText, forgeSummary;
    GameObject placePage, forgePage;
    Button backButton, nextButton;
    TextMeshProUGUI backLabel, nextLabel;
    readonly Image[] cellImages = new Image[BoardCells.Size * BoardCells.Size];
    readonly TextMeshProUGUI[] cellTexts = new TextMeshProUGUI[BoardCells.Size * BoardCells.Size];
    readonly List<(PieceType type, Image image)> paletteButtons = new List<(PieceType, Image)>();

    class ForgeCard
    {
        public PieceType Type;
        public Image Image;
        public Outline Line;
        public TextMeshProUGUI Text;
    }
    readonly List<ForgeCard> forgeCards = new List<ForgeCard>();

    static FormationView open;

    // 開いているか（タイトル画面は、この画面が開いている間キーを読まない）
    public static bool IsOpen => open != null;

    // タイトルの上に開く（前回のそのボスの編成があれば、それを並べた状態で開く。なければおまかせ配置）
    public static void Open(BossDef def)
    {
        if (open != null || def == null) return;
        FormationView v = new GameObject("FormationView").AddComponent<FormationView>();
        open = v;
        v.Setup(def);
    }

    void Setup(BossDef boss)
    {
        def = boss;
        openedFrame = Time.frameCount;
        font = RuntimeUi.LoadFont();
        RuntimeUi.EnsureEventSystem();
        RaidFormation prev = MatchSetup.LastFormation(def.Id);
        if (prev != null)
        {
            formation = prev.pieces;
            picks = RaidRules.PrunedForgePicks(formation, prev.forgePicks);
        }
        else formation = RaidRules.AutoFormation(def);
        Build();
        Refresh();
    }

    void OnDestroy()
    {
        if (open == this) open = null;
    }

    void Update()
    {
        Keyboard k = Keyboard.current;
        if (k == null || def == null || Time.frameCount == openedFrame) return;
        if (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame) Next();
        if (k.escapeKey.wasPressedThisFrame) Back();
    }

    RaidFormation CurrentSetup => new RaidFormation { pieces = formation, forgePicks = picks };

    // ① の検査（錬成駒の分を除く）
    List<string> PlacementErrors(out int cost)
    {
        List<string> all = RaidRules.Validate(new RaidFormation { pieces = formation }, def, out cost);
        return all;
    }

    // ------------------------------------------------------------
    //  操作
    // ------------------------------------------------------------

    RaidPlacement At(int x, int y) => formation.Find(f => f.x == x && f.y == y);

    void SelectPiece(PieceType type)
    {
        selected = selected.HasValue && selected.Value == type ? (PieceType?)null : type;
        notice = "";
        Refresh();
    }

    void ClickCell(int x, int y)
    {
        if (page != 1) return;
        notice = "";
        RaidPlacement here = At(x, y);
        if (!selected.HasValue)
        {
            if (here != null) formation.Remove(here);
            else notice = "先に、右の駒のボタンを押して置く駒を選んでください。";
        }
        else if (here != null && here.type == selected.Value)
        {
            formation.Remove(here);
        }
        else
        {
            PieceType type = selected.Value;
            if (here != null) formation.Remove(here);
            if (type == RaidRules.KingType)
            {
                // 王将は 1 枚だけ：前の王将を外して、ここへ置く
                formation.RemoveAll(f => f.type == RaidRules.KingType);
                selected = null;
            }
            formation.Add(new RaidPlacement(type, x, y));
        }
        picks = RaidRules.PrunedForgePicks(formation, picks);
        Refresh();
    }

    void AutoPlace()
    {
        formation = RaidRules.AutoFormation(def);
        picks = RaidRules.PrunedForgePicks(formation, picks);
        selected = null;
        notice = "おまかせ配置にしました（本将棋の初期配置から、コストの上限に入る駒）。";
        Refresh();
    }

    void Clear()
    {
        formation = new List<RaidPlacement>();
        selected = null;
        notice = "すべての駒を外しました。";
        Refresh();
    }

    void ToggleForge(PieceType type)
    {
        if (page != 2) return;
        notice = "";
        if (RaidRules.AutoForgeTypes(formation).Contains(type))
        {
            notice = $"{PieceNames.Full(type)}は初期配置にあるので、選ばなくても錬成できます。";
        }
        else if (picks.Contains(type)) picks.Remove(type);
        else if (picks.Count >= def.ForgeSlots)
        {
            notice = $"錬成駒は {def.ForgeSlots} 種までです。入れ替えるときは、選んでいる駒を先に外してください。";
        }
        else picks.Add(type);
        Refresh();
    }

    void Next()
    {
        RuntimeUi.ClearUiSelection();
        if (page == 1)
        {
            if (PlacementErrors(out _).Count > 0)
            {
                notice = "編成に問題があります（下の × を直してください）。";
                Refresh();
                return;
            }
            page = 2;
            selected = null;
            notice = "";
            Refresh();
            return;
        }
        RaidFormation setup = CurrentSetup;
        List<string> errors = RaidRules.Validate(setup, def, out _);
        if (errors.Count > 0)
        {
            notice = errors[0];
            Refresh();
            return;
        }
        MatchSetup.SetBossRaid(def.Id, setup);
        SceneManager.LoadScene(NextScene);
    }

    void Back()
    {
        RuntimeUi.ClearUiSelection();
        if (page == 2)
        {
            page = 1;
            notice = "";
            Refresh();
            return;
        }
        // 途中の編成も覚えておく（正しい編成のときだけ。ボス選択から戻ってきたときに続きから）
        if (RaidRules.IsValid(CurrentSetup, def)) MatchSetup.RememberFormation(def.Id, CurrentSetup);
        BossSelectView.Open();
        Destroy(gameObject);
    }

    // ------------------------------------------------------------
    //  表示
    // ------------------------------------------------------------

    void Refresh()
    {
        titleText.text = page == 1 ? $"討伐任務：{def.Name}　駒の編成と配置" : $"討伐任務：{def.Name}　錬成駒";
        stepText.text = page == 1 ? "STEP 2 / 3" : "STEP 2 / 3（錬成駒）";
        guideText.text = page == 1
            ? $"自陣（手前 3 段）に駒を並べます。チームコストの合計が {def.CostLimit} 以下になるように。王将は必ず 1 枚。王将はボスの攻撃に {def.KingMaxHits - 1} 回まで耐えます。"
            : $"対局中に錬成（エナジーを払って持ち駒を作る）できる駒を、{def.ForgeSlots} 種まで選びます。初期配置に置いた駒の種類は、選ばなくても錬成できます。";
        placePage.SetActive(page == 1);
        forgePage.SetActive(page == 2);

        // 盤
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                int i = GameHelper.CalcPanelNum(x, y);
                bool own = BoardZones.IsOwnZone(PlayerType.Player1, x);
                bool boss = x >= def.StartX && x < def.StartX + def.H && y >= def.StartY && y < def.StartY + def.W;
                RaidPlacement f = own ? At(x, y) : null;
                cellImages[i].color = boss ? CellBoss : own ? (f != null ? Color.Lerp(CellOwn, Color.white, 0.12f) : CellOwn) : CellOther;
                cellTexts[i].text = f != null ? PieceNames.Short(f.type) : "";
                cellTexts[i].color = f != null && f.type == RaidRules.KingType ? RuntimeUi.Hex(0xffd54a) : PieceColor;
            }
        }

        // 駒のボタン
        foreach ((PieceType type, Image image) in paletteButtons)
        {
            image.color = selected.HasValue && selected.Value == type ? ButtonOn : ButtonOff;
        }

        int cost;
        List<string> errors = PlacementErrors(out cost);
        bool over = cost > def.CostLimit;
        costText.text = $"チームコスト <color={(over ? "#FF5252" : "#FFFFFF")}>{cost}</color> / {def.CostLimit}　　駒 {formation.Count} 枚";
        StringBuilder sb = new StringBuilder();
        foreach (string e in errors) sb.Append("× ").Append(e).Append('\n');
        errorText.text = sb.ToString().TrimEnd();

        if (page == 1)
        {
            opText.text = !string.IsNullOrEmpty(notice) ? notice
                : selected.HasValue
                    ? $"選択中：{PieceNames.Full(selected.Value)}（コスト {RaidRules.TeamCost(selected.Value)}）▶ 自陣のマスを押して置く（続けて置ける。同じ駒のマスを押すと外す）"
                    : "駒のボタンを押して選び、自陣（青いマス）を押して置く。何も選ばずに駒のあるマスを押すと外す。";
        }
        else
        {
            HashSet<PieceType> auto = RaidRules.AutoForgeTypes(formation);
            foreach (ForgeCard card in forgeCards)
            {
                RaidForgeOption o = RaidRules.ForgeRecipeOf(card.Type).Value;
                bool isAuto = auto.Contains(card.Type);
                bool on = picks.Contains(card.Type);
                card.Image.color = isAuto ? new Color(0.14f, 0.2f, 0.16f, 1f) : on ? ButtonOn : ButtonOff;
                card.Line.effectColor = on ? RuntimeUi.Hex(0xffd54a) : new Color(0f, 0f, 0f, 0.5f);
                card.Line.effectDistance = on ? new Vector2(4f, -4f) : new Vector2(2f, -2f);
                string status = isAuto ? "<color=#A5D6A7>初期配置にある：自動で錬成可</color>" : on ? "<color=#FFD54A>★ 選択中</color>" : "押して選ぶ";
                card.Text.text = $"<size=130%>{PieceNames.Full(card.Type)}</size>\n錬成 {o.Cost} エナジー\n<size=80%>{status}</size>";
            }
            List<string> names = new List<string>();
            foreach (RaidForgeOption o in RaidRules.ForgeRecipes(CurrentSetup)) names.Add($"{PieceNames.Short(o.Type)}({o.Cost})");
            forgeSummary.text = $"錬成枠 {picks.Count} / {def.ForgeSlots}\n対局中に錬成できる駒：{(names.Count > 0 ? string.Join("・", names) : "なし")}";
            opText.text = !string.IsNullOrEmpty(notice) ? notice
                : picks.Count < def.ForgeSlots ? $"あと {def.ForgeSlots - picks.Count} 種選べます（選ばなくても進めます）。"
                : "錬成駒を選び終えました。「魔法スキルへ」で進みます。";
        }

        backLabel.text = page == 1 ? "◀ ボス選択へ" : "◀ 配置に戻る";
        nextLabel.text = page == 1 ? "錬成駒へ ▶" : "魔法スキルへ ▶";
        nextButton.interactable = errors.Count == 0;
        RuntimeUi.ClearUiSelection();
    }

    // ------------------------------------------------------------
    //  画面を作る
    // ------------------------------------------------------------

    void Build()
    {
        Canvas canvas = RuntimeUi.CreateCanvas("FormationCanvas", transform, 500);
        Image dim = RuntimeUi.CreatePanel("Dim", canvas.transform, Dim, false);
        RuntimeUi.Stretch(dim.rectTransform, Vector2.zero, Vector2.zero);

        Image box = RuntimeUi.CreatePanel("Box", canvas.transform, Box, false);
        Outline boxLine = box.gameObject.AddComponent<Outline>();
        boxLine.effectColor = RuntimeUi.WithAlpha(Accent, 0.9f);
        boxLine.effectDistance = new Vector2(3f, -3f);
        box.rectTransform.sizeDelta = new Vector2(1800f, 1000f);
        Transform root = box.transform;

        titleText = Label(root, "Title", 50f, new Color(1f, 0.8f, 0.78f, 1f), TextAlignmentOptions.Left, new Vector2(-330f, 440f), new Vector2(1080f, 70f));
        stepText = Label(root, "Step", 30f, Color.white, TextAlignmentOptions.Right, new Vector2(620f, 440f), new Vector2(480f, 60f));
        guideText = Label(root, "Guide", 23f, new Color(0.87f, 0.87f, 0.87f, 1f), TextAlignmentOptions.Left, new Vector2(0f, 385f), new Vector2(1720f, 40f));

        BuildBoard(root);

        // 右側（①・② 共通）：操作の案内・コスト・問題の一覧
        opText = Label(root, "Operation", 24f, RuntimeUi.Hex(0xffe082), TextAlignmentOptions.TopLeft, new Vector2(400f, 300f), new Vector2(920f, 70f));
        costText = Label(root, "Cost", 36f, Color.white, TextAlignmentOptions.Left, new Vector2(400f, -215f), new Vector2(920f, 50f));
        costText.richText = true;
        errorText = Label(root, "Errors", 22f, RuntimeUi.Hex(0xff8a80), TextAlignmentOptions.TopLeft, new Vector2(400f, -300f), new Vector2(920f, 110f));

        // ① 駒のボタン・おまかせ配置・クリア
        placePage = Page(root, "PlacePage");
        Label(placePage.transform, "PaletteTitle", 24f, Color.white, TextAlignmentOptions.Left, new Vector2(400f, 232f), new Vector2(920f, 36f)).text = "駒（名前とコスト）";
        PaletteRow(placePage.transform, RaidRules.BasicPalette, 150f);
        Label(placePage.transform, "OriginalTitle", 24f, Color.white, TextAlignmentOptions.Left, new Vector2(400f, 62f), new Vector2(920f, 36f)).text = "オリジナル駒";
        PaletteRow(placePage.transform, RaidRules.OriginalPalette, -20f);
        Button auto = RuntimeUi.CreateButton("Auto", placePage.transform, RuntimeUi.Hex(0x00695c), "おまかせ配置", 28f, font, out _);
        Place((RectTransform)auto.transform, new Vector2(90f, -135f), new Vector2(300f, 70f));
        auto.onClick.AddListener(AutoPlace);
        Button clear = RuntimeUi.CreateButton("Clear", placePage.transform, RuntimeUi.Hex(0x5d4037), "クリア", 28f, font, out _);
        Place((RectTransform)clear.transform, new Vector2(370f, -135f), new Vector2(220f, 70f));
        clear.onClick.AddListener(Clear);

        // ② 錬成駒
        forgePage = Page(root, "ForgePage");
        const float fw = 216f, fh = 120f, fgap = 12f;
        for (int i = 0; i < RaidRules.ForgePickTypes.Length; i++)
        {
            PieceType type = RaidRules.ForgePickTypes[i];
            int col = i % 4, row = i / 4;
            Button b = RuntimeUi.CreateButton($"Forge_{type}", forgePage.transform, ButtonOff, "", 22f, font, out TextMeshProUGUI text);
            Place((RectTransform)b.transform, new Vector2(400f - 1.5f * (fw + fgap) + col * (fw + fgap), 205f - row * (fh + fgap)), new Vector2(fw, fh));
            text.richText = true;
            text.enableAutoSizing = false;
            text.fontSize = 20f;
            b.onClick.AddListener(() => ToggleForge(type));
            forgeCards.Add(new ForgeCard { Type = type, Image = (Image)b.targetGraphic, Line = b.GetComponent<Outline>(), Text = text });
        }
        forgeSummary = Label(forgePage.transform, "Summary", 24f, Color.white, TextAlignmentOptions.TopLeft, new Vector2(400f, -155f), new Vector2(920f, 60f));
        forgeSummary.fontStyle = FontStyles.Normal;

        // 下：戻る・次へ
        backButton = RuntimeUi.CreateButton("Back", root, RuntimeUi.GrayButton, "", 28f, font, out backLabel);
        Place((RectTransform)backButton.transform, new Vector2(-690f, -447f), new Vector2(340f, 70f));
        backButton.onClick.AddListener(Back);
        nextButton = RuntimeUi.CreateButton("Next", root, RuntimeUi.Hex(0xc62828), "", 32f, font, out nextLabel);
        Place((RectTransform)nextButton.transform, new Vector2(640f, -447f), new Vector2(420f, 70f));
        nextButton.onClick.AddListener(Next);
        Label(root, "Keys", 22f, new Color(1f, 1f, 1f, 0.65f), TextAlignmentOptions.Center, new Vector2(-20f, -447f), new Vector2(700f, 40f)).text = "Enter：次へ　　Esc：戻る";
    }

    // 盤（9×9。手前 3 段が自陣で、押せる。奥にボスの位置を出す）
    void BuildBoard(Transform root)
    {
        Image frame = RuntimeUi.CreatePanel("Board", root, new Color(0.05f, 0.05f, 0.07f, 1f), false);
        Place(frame.rectTransform, BoardCenter, new Vector2(Cell * 9f + 12f, Cell * 9f + 12f));
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                int cx = x, cy = y;
                Vector2 pos = BoardCenter + new Vector2((y - 4) * Cell, (x - 4) * Cell);
                GameObject obj = new GameObject($"Cell_{x}_{y}", typeof(RectTransform));
                obj.transform.SetParent(root, false);
                Place((RectTransform)obj.transform, pos, new Vector2(Cell - 4f, Cell - 4f));
                Image img = obj.AddComponent<Image>();
                bool own = BoardZones.IsOwnZone(PlayerType.Player1, x);
                img.raycastTarget = own;
                if (own)
                {
                    Button b = obj.AddComponent<Button>();
                    b.targetGraphic = img;
                    Navigation nav = b.navigation;
                    nav.mode = Navigation.Mode.None;
                    b.navigation = nav;
                    b.onClick.AddListener(() => ClickCell(cx, cy));
                }
                TextMeshProUGUI t = RuntimeUi.CreateText("Piece", obj.transform, "", 44f, PieceColor, TextAlignmentOptions.Center, font);
                RuntimeUi.Stretch(t.rectTransform, Vector2.zero, Vector2.zero);
                int i = GameHelper.CalcPanelNum(x, y);
                cellImages[i] = img;
                cellTexts[i] = t;
            }
        }
        // ボスの位置
        Vector2 a = BoardCenter + new Vector2((def.StartY - 4) * Cell, (def.StartX - 4) * Cell);
        Vector2 c = BoardCenter + new Vector2((def.StartY + def.W - 1 - 4) * Cell, (def.StartX + def.H - 1 - 4) * Cell);
        TextMeshProUGUI bossLabel = Label(root, "BossLabel", 34f, new Color(1f, 0.8f, 0.78f, 1f), TextAlignmentOptions.Center, (a + c) * 0.5f, new Vector2(def.W * Cell, def.H * Cell));
        bossLabel.text = $"{def.Name}\n<size=60%>HP {def.Hp}</size>";
        Label(root, "CampNote", 20f, new Color(0.6f, 0.8f, 1f, 1f), TextAlignmentOptions.Center, BoardCenter + new Vector2(0f, -4.5f * Cell - 22f), new Vector2(Cell * 9f, 30f)).text = "▲ 自陣（手前 3 段）に並べる";
    }

    void PaletteRow(Transform parent, PieceType[] types, float y)
    {
        const float w = 104f, gap = 12f;
        for (int i = 0; i < types.Length; i++)
        {
            PieceType type = types[i];
            Button b = RuntimeUi.CreateButton($"Piece_{type}", parent, ButtonOff, "", 24f, font, out TextMeshProUGUI text);
            Place((RectTransform)b.transform, new Vector2(-8f + i * (w + gap), y), new Vector2(w, 110f));
            text.richText = true;
            text.enableAutoSizing = false;
            text.fontSize = 24f;
            text.text = $"<size=170%>{PieceNames.Short(type)}</size>\n<size=80%>コスト {RaidRules.TeamCost(type)}</size>";
            b.onClick.AddListener(() => SelectPiece(type));
            paletteButtons.Add((type, (Image)b.targetGraphic));
        }
    }

    GameObject Page(Transform parent, string name)
    {
        GameObject page = new GameObject(name, typeof(RectTransform));
        page.transform.SetParent(parent, false);
        Place((RectTransform)page.transform, Vector2.zero, Vector2.zero);
        return page;
    }

    TextMeshProUGUI Label(Transform parent, string name, float size, Color color, TextAlignmentOptions alignment, Vector2 pos, Vector2 box)
    {
        TextMeshProUGUI t = RuntimeUi.CreateText(name, parent, "", size, color, alignment, font);
        t.richText = true;
        Place(t.rectTransform, pos, box);
        return t;
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
