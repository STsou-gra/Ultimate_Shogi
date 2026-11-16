using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using TMPro;

// 魔法スキルの編成画面（Prototype の src/scenes/LoadoutScene.js）。対局の前に、各プレイヤーがランクごとに魔法を選ぶ。
//  ・枠は 1 人 5 つ：0〜2 = 通常魔法（3 つ選ぶ）、3 = 必殺魔法（1 つ）、4 = 超常魔法（1 つ）（SkillRanks）
//  ・枠を押すと、その枠のランクの魔法だけが選択肢に並ぶ（同じ魔法は 2 つの枠に入れられない）
//  ・候補が選ぶ数以下のランクは、全部を選んだ状態で固定（枠は押せない）。候補がないランクの枠は「候補なし」
//  ・すべての枠が埋まると「対局開始」を押せる
//  ・選択肢はカード（SkillCardView。Prototype の SkillCard）：ランク・名前・説明の書き出し・コスト・「詳細」ボタン。
//    「詳細」で説明の全文と補足を小さなポップアップ（SkillDetailPopup）で出す。「閉じる」・外側のクリック・Esc で閉じる。
//    説明の文は SkillDefinition の description から取り、ルール説明（RulesBook）と同じ文を使う
//  ・キーボード：選択パネルを開いている間、← → ↑ ↓（W A S D）でカードを選ぶ・Enter（F）で決定・I で「詳細」・Esc で閉じる
//  ・デバッグ用の魔法（SkillDefinition.debugOnly。魔力の火花）は候補に出さない（Prototype の RETIRED_SKILLS）
// 選んだ魔法は枠の順に SkillDeck に入れて、DeckTransferManager で対局（GameSystem）に渡す
// CPU と対戦（MatchSetup.IsVsCpu）のときは、人の分の枠だけを出し、CPU の分は CpuLoadout が選ぶ（Prototype と同じ）
public class DeckSelectController : MonoBehaviour
{
    [Header("Skill Database Reference")]
    [SerializeField] private SkillDatabase database;

    [Header("Player1 UI Slot References")]
    [SerializeField] private Button[] player1SlotButtons; //要素数5（通常・通常・通常・必殺・超常）
    [SerializeField] private TextMeshProUGUI[] player1SlotTexts; //要素数5

    [Header("Player2 UI Slot References")]
    [SerializeField] private Button[] player2SlotButtons; //要素数5
    [SerializeField] private TextMeshProUGUI[] player2SlotTexts; //要素数5

    [Header("Selection Panel UI References")]
    [SerializeField] private GameObject skillSelectionPanel; //スキル選択パネル
    [SerializeField] private RectTransform selectionButtonsContainer; //生成したボタンを並べる親（ScrollViewのContent等）
    [SerializeField] private GameObject skillButtonPrefab; //生成する選択肢ボタンのプレハブ
    [SerializeField] private Button removeSkillButton; //スキルなしに設定するクリアボタン

    [Header("Start")]
    [SerializeField] private Button playButton; //「対局開始」ボタン（未設定なら "StartButton" を探す）。枠が埋まるまで押せない

    [Header("Scene Manager Reference")]
    [SerializeField] private GameSceneManager sceneManager;

    // 現在の選択状態を保持する変数
    private PlayerType currentSelectingPlayer;
    private int currentSelectingSlotIndex;

    //プレイヤーが選んだスキルの仮リスト（枠の順）
    private SkillDefinition[] player1SelectedSkills = new SkillDefinition[SkillRanks.SlotCount];
    private SkillDefinition[] player2SelectedSkills = new SkillDefinition[SkillRanks.SlotCount];

    //生成した選択肢ボタン（スキルごと）
    private readonly List<GameObject> spawnedButtons = new List<GameObject>();
    private readonly List<SkillDefinition> spawnedSkills = new List<SkillDefinition>();
    private readonly List<SkillCardView> spawnedCards = new List<SkillCardView>();

    // 選択パネルに今並んでいるカード（押した枠のランクの魔法）と、キーボードで選んでいるカード
    private readonly List<SkillCardView> visibleCards = new List<SkillCardView>();
    private int focusIndex = -1;
    private int cardColumns = 3;
    private int selectionOpenedFrame = -1;
    private RulesSources detailSources; // 「詳細」の補足を作るのに使う（ルール説明と同じ）

    const float CardSpacing = 14f;
    const float CardPadding = 10f;
    const float ScrollbarWidth = 17f;

    void Start()
    {
        if (playButton == null)
        {
            GameObject start = GameObject.Find("StartButton");
            if (start != null) playButton = start.GetComponent<Button>();
        }
        // 戦闘型（本将棋型・合戦将棋型）の選択（画面の右上。シーンに置かれていなければ自動で追加する）。
        // CPU と対戦でも出す（合戦将棋型の CPU は RealtimeCpuPacer で一定の間隔ごとに動く。GameSystem.Cpu.cs）
        if (FindAnyObjectByType<BattleRulesetPicker>() == null) gameObject.AddComponent<BattleRulesetPicker>();
        InitializeSelectionPanel();
        RegisterSlotButtons();
        AutoFillLockedRanks(player1SelectedSkills);
        AutoFillLockedRanks(player2SelectedSkills);
        SetupCpuSlots();
        UpdateSlotDisplays();
    }

    // CPU と対戦：CPU の分の魔法を選んで、CPU の枠を隠す（代わりに「CPU が自分で選びます」と出す）
    void SetupCpuSlots()
    {
        if (!MatchSetup.IsVsCpu) return;
        PlayerType cpu = MatchSetup.CpuSide;
        SkillDefinition[] picked = CpuLoadout.Choose(database != null ? database.allSkills : null, GameRng.NewSeed());
        System.Array.Copy(picked, SlotsOf(cpu), SkillRanks.SlotCount);
        Debug.Log($"CPU（{(cpu == PlayerType.Player1 ? "先手" : "後手")}）の編成：{CpuLoadout.Describe(picked)}");

        Button[] buttons = cpu == PlayerType.Player1 ? player1SlotButtons : player2SlotButtons;
        Button first = null;
        if (buttons != null)
        {
            foreach (Button b in buttons)
            {
                if (b == null) continue;
                if (first == null) first = b;
                b.gameObject.SetActive(false);
            }
        }
        if (first == null || first.transform.parent == null) return;

        GameObject label = new GameObject("CpuLoadoutLabel", typeof(RectTransform));
        label.transform.SetParent(first.transform.parent, false);
        RectTransform src = (RectTransform)first.transform, dst = (RectTransform)label.transform;
        dst.anchorMin = src.anchorMin;
        dst.anchorMax = src.anchorMax;
        dst.pivot = src.pivot;
        dst.anchoredPosition = src.anchoredPosition;
        dst.sizeDelta = new Vector2(Mathf.Max(src.sizeDelta.x, 360f), Mathf.Max(src.sizeDelta.y, 60f));
        TextMeshProUGUI text = label.AddComponent<TextMeshProUGUI>();
        TextMeshProUGUI like = first.GetComponentInChildren<TextMeshProUGUI>(true);
        if (like != null)
        {
            text.font = like.font;
            text.fontSize = like.fontSize;
            text.color = like.color;
        }
        text.alignment = TextAlignmentOptions.Center;
        text.text = $"{MatchSetup.SideLabel(cpu)}\nCPU は魔法を自分で選びます";
    }

    // ------------------------------------------------------------
    //  候補（データベースの魔法をランクごとに分ける）
    // ------------------------------------------------------------

    List<SkillDefinition> Candidates(SkillRank rank)
    {
        List<SkillDefinition> result = new List<SkillDefinition>();
        if (database == null) return result;
        foreach (SkillDefinition skill in database.allSkills)
        {
            if (skill != null && skill.Selectable && skill.rank == rank && !result.Contains(skill)) result.Add(skill);
        }
        return result;
    }

    // そのランクで選ぶ数（候補が少なければ候補の数）
    int Need(SkillRank rank)
    {
        return Mathf.Min(SkillRanks.Pick(rank), Candidates(rank).Count);
    }

    // 候補が選ぶ数以下のランクは、全部を選んだ状態で固定（Prototype の locked）
    bool IsLocked(SkillRank rank)
    {
        return Candidates(rank).Count <= SkillRanks.Pick(rank);
    }

    void AutoFillLockedRanks(SkillDefinition[] slots)
    {
        foreach (SkillRank rank in SkillRanks.All)
        {
            if (!IsLocked(rank)) continue;
            List<SkillDefinition> candidates = Candidates(rank);
            int first = SkillRanks.FirstSlot(rank);
            for (int i = 0; i < SkillRanks.Pick(rank); i++)
            {
                slots[first + i] = i < candidates.Count ? candidates[i] : null;
            }
        }
    }

    // ------------------------------------------------------------
    //  画面の準備
    // ------------------------------------------------------------

    //スロットボタンのクリックイベントを登録する（インスペクターの配列に入れるだけで動くようにする）
    private void RegisterSlotButtons()
    {
        for (int i = 0; i < player1SlotButtons.Length; i++)
        {
            int index = i; //ラムダ式のクロージャ用
            if (player1SlotButtons[i] != null)
            {
                player1SlotButtons[i].onClick.AddListener(() => Player1OnSlotButtonClicked(index));
            }
        }
        for (int i = 0; i < player2SlotButtons.Length; i++)
        {
            int index = i; //ラムダ式のクロージャ用
            if (player2SlotButtons[i] != null)
            {
                player2SlotButtons[i].onClick.AddListener(() => Player2OnSlotButtonClicked(index));
            }
        }
    }

    private void InitializeSelectionPanel()
    {
        if (database == null || selectionButtonsContainer == null || skillButtonPrefab == null)
        {
            Debug.LogError("必要なコンポーネント（データベース、コンテナ、またはプレハブ）が設定されていません。");
            return;
        }

        //既存の生成済みボタンをクリア
        foreach (var button in spawnedButtons)
        {
            if (button != null)
            {
                Destroy(button);
            }
        }
        spawnedButtons.Clear();
        spawnedSkills.Clear();
        spawnedCards.Clear();
        PrepareCardLayout();

        //データベースに登録されている魔法の数だけボタンを生成（表示するのは、押した枠のランクの魔法だけ）
        foreach (SkillRank rank in SkillRanks.All)
        {
            foreach (SkillDefinition skill in Candidates(rank))
            {
                //プレハブからボタンを生成してコンテナに入れる
                GameObject buttonObject = Instantiate(skillButtonPrefab, selectionButtonsContainer);
                spawnedButtons.Add(buttonObject);
                spawnedSkills.Add(skill);
                spawnedCards.Add(SkillCardView.Decorate(buttonObject, skill, OpenDetail, OnCardHover));

                //クリックイベントの登録
                Button buttonComponent = buttonObject.GetComponent<Button>();
                if (buttonComponent != null)
                {
                    SkillDefinition picked = skill; //ラムダ式のクロージャ用
                    buttonComponent.onClick.AddListener(() => OnSkillSelected(picked));
                }
                buttonObject.SetActive(false);
            }
        }

        //なしボタンのイベント設定
        if (removeSkillButton != null)
        {
            removeSkillButton.onClick.RemoveAllListeners();
            removeSkillButton.onClick.AddListener(OnClearSelected);
            removeSkillButton.gameObject.SetActive(false); //「なし」ボタンは初期状態で非表示にする
        }

        //選択パネルは初期状態で非表示にする
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(false);
        }
    }

    // ------------------------------------------------------------
    //  枠を押す → 選択肢を選ぶ
    // ------------------------------------------------------------

    //Player1のスロットボタンが押された時（UIから呼び出す）
    public void Player1OnSlotButtonClicked(int slotIndex)
    {
        OpenSelection(PlayerType.Player1, slotIndex);
    }

    //Player2のスロットボタンが押された時（UIから呼び出す）
    public void Player2OnSlotButtonClicked(int slotIndex)
    {
        OpenSelection(PlayerType.Player2, slotIndex);
    }

    void OpenSelection(PlayerType player, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= SkillRanks.SlotCount) return;
        if (MatchSetup.IsCpu(player)) return; // CPU の分は CPU が選ぶ
        SkillRank rank = SkillRanks.RankOfSlot(slotIndex);
        if (IsLocked(rank)) return; // 候補が少ないランクは自動で選ばれていて、変えられない

        currentSelectingSlotIndex = slotIndex;
        currentSelectingPlayer = player;
        SkillDefinition[] slots = SlotsOf(player);

        // その枠のランクの魔法だけを並べる。同じプレイヤーのほかの枠に入っている魔法は選べない
        SkillDetailPopup.CloseCurrent();
        visibleCards.Clear();
        focusIndex = -1;
        for (int i = 0; i < spawnedButtons.Count; i++)
        {
            GameObject buttonObject = spawnedButtons[i];
            SkillDefinition skill = spawnedSkills[i];
            if (buttonObject == null) continue;
            bool sameRank = skill.rank == rank;
            buttonObject.SetActive(sameRank);
            if (!sameRank) continue;

            bool usedElsewhere = false;
            for (int s = 0; s < slots.Length; s++)
            {
                if (s != slotIndex && slots[s] == skill) usedElsewhere = true;
            }
            SkillCardView card = spawnedCards[i];
            card.SetState(slots[slotIndex] == skill, usedElsewhere);
            card.SetFocused(false);
            if (slots[slotIndex] == skill) focusIndex = visibleCards.Count;
            visibleCards.Add(card);
        }
        if (focusIndex < 0) focusIndex = visibleCards.FindIndex(c => c.CanChoose);
        if (focusIndex < 0 && visibleCards.Count > 0) focusIndex = 0;
        SetFocus(focusIndex, true);
        selectionOpenedFrame = Time.frameCount;

        //スキル選択パネルを表示
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(true);
        }
        //「なし」ボタンを表示する
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(true);
        }
    }

    public void OnSkillSelected(int skillIndex)
    {
        if (skillIndex < 0 || skillIndex >= spawnedSkills.Count) return;
        OnSkillSelected(spawnedSkills[skillIndex]);
    }

    void OnSkillSelected(SkillDefinition skill)
    {
        if (skill == null || skill.rank != SkillRanks.RankOfSlot(currentSelectingSlotIndex)) return;
        AssignSkillToCurrentSlot(skill);
    }

    public void OnClearSelected()
    {
        AssignSkillToCurrentSlot(null);
    }

    private void AssignSkillToCurrentSlot(SkillDefinition skill)
    {
        SkillDefinition[] slots = SlotsOf(currentSelectingPlayer);
        if (currentSelectingSlotIndex >= 0 && currentSelectingSlotIndex < slots.Length)
        {
            // 同じ魔法がほかの枠にあれば外す（選び直し）
            if (skill != null)
            {
                for (int s = 0; s < slots.Length; s++)
                {
                    if (s != currentSelectingSlotIndex && slots[s] == skill) slots[s] = null;
                }
            }
            slots[currentSelectingSlotIndex] = skill;
        }

        UpdateSlotDisplays();
        CloseSelection();
    }

    // 選択パネルを閉じる（枠の中身は変えない）
    void CloseSelection()
    {
        SkillDetailPopup.CloseCurrent();
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(false);
        }
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(false);
        }
        visibleCards.Clear();
        focusIndex = -1;
        RuntimeUi.ClearUiSelection();
    }

    bool IsSelectionOpen => skillSelectionPanel != null && skillSelectionPanel.activeInHierarchy;

    // ------------------------------------------------------------
    //  カード・「詳細」・キーボード
    // ------------------------------------------------------------

    // 選択パネルをカードが並ぶ大きさにする（シーンの選択パネル・Content の GridLayoutGroup を実行時に直す）。
    // 列の数は画面の幅に入るだけ（最大 3）。縦はスクロール
    void PrepareCardLayout()
    {
        if (selectionButtonsContainer == null) return;
        RectTransform canvasRect = null;
        Canvas canvas = selectionButtonsContainer.GetComponentInParent<Canvas>();
        if (canvas != null) canvasRect = canvas.rootCanvas.transform as RectTransform;
        float canvasW = canvasRect != null && canvasRect.rect.width > 0f ? canvasRect.rect.width : 1920f;
        float canvasH = canvasRect != null && canvasRect.rect.height > 0f ? canvasRect.rect.height : 1080f;

        float extra = CardPadding * 2f + ScrollbarWidth;
        cardColumns = Mathf.Clamp(Mathf.FloorToInt((canvasW - 40f - extra + CardSpacing) / (SkillCardView.Width + CardSpacing)), 1, 3);
        float panelW = cardColumns * SkillCardView.Width + (cardColumns - 1) * CardSpacing + extra;
        float panelH = Mathf.Min(canvasH - 220f, 2.5f * (SkillCardView.Height + CardSpacing) + extra);
        panelH = Mathf.Max(panelH, SkillCardView.Height + extra);

        GridLayoutGroup grid = selectionButtonsContainer.GetComponent<GridLayoutGroup>();
        if (grid != null)
        {
            grid.cellSize = new Vector2(SkillCardView.Width, SkillCardView.Height);
            grid.spacing = new Vector2(CardSpacing, CardSpacing);
            grid.padding = new RectOffset((int)CardPadding, (int)CardPadding, (int)CardPadding, (int)CardPadding);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = cardColumns;
            grid.childAlignment = TextAnchor.UpperLeft;
        }
        ContentSizeFitter fitter = selectionButtonsContainer.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = selectionButtonsContainer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        if (skillSelectionPanel == null) return;
        RectTransform panelRt = skillSelectionPanel.transform as RectTransform;
        if (panelRt != null) panelRt.sizeDelta = new Vector2(panelW, panelH);
        ScrollRect scroll = skillSelectionPanel.GetComponentInChildren<ScrollRect>(true);
        if (scroll != null)
        {
            scroll.horizontal = false;
            if (scroll.horizontalScrollbar != null) scroll.horizontalScrollbar.gameObject.SetActive(false);
            scroll.horizontalScrollbar = null;
            scroll.scrollSensitivity = Mathf.Max(scroll.scrollSensitivity, 30f);
        }

        // 操作の案内（パネルの上の左）と「なし」ボタン（パネルの上の右）
        if (panelRt != null)
        {
            TextMeshProUGUI hint = RuntimeUi.CreateText("KeyHint", panelRt, "クリックで選ぶ・「詳細」で説明　｜　← → ↑ ↓：選ぶ　Enter：決定　I：詳細　Esc：閉じる",
                15f, Color.white, TextAlignmentOptions.BottomLeft, RuntimeUi.LoadFont());
            hint.enableAutoSizing = true;
            hint.fontSizeMin = 10f;
            hint.fontSizeMax = 15f;
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            RuntimeUi.Place(hint.rectTransform, new Vector2(0f, 1f), new Vector2(0f, -34f), new Vector2(panelW - 130f, 30f));
            if (removeSkillButton != null && removeSkillButton.transform.parent == panelRt.parent)
            {
                RectTransform removeRt = (RectTransform)removeSkillButton.transform;
                removeRt.anchorMin = removeRt.anchorMax = panelRt.anchorMin;
                removeRt.pivot = new Vector2(1f, 0f);
                removeRt.sizeDelta = new Vector2(110f, 30f);
                removeRt.anchoredPosition = panelRt.anchoredPosition + new Vector2(panelW * (1f - panelRt.pivot.x), panelH * (1f - panelRt.pivot.y) + 4f);
            }
        }
    }

    void OnCardHover(SkillCardView card)
    {
        if (SkillDetailPopup.IsOpen) return;
        int index = visibleCards.IndexOf(card);
        if (index >= 0) SetFocus(index, false);
    }

    void SetFocus(int index, bool scrollTo)
    {
        if (visibleCards.Count == 0) { focusIndex = -1; return; }
        focusIndex = Mathf.Clamp(index, 0, visibleCards.Count - 1);
        for (int i = 0; i < visibleCards.Count; i++) visibleCards[i].SetFocused(i == focusIndex);
        if (scrollTo) ScrollToFocus();
    }

    // キーボードで選んだカードが見えるように縦にスクロールする
    void ScrollToFocus()
    {
        if (skillSelectionPanel == null || focusIndex < 0) return;
        ScrollRect scroll = skillSelectionPanel.GetComponentInChildren<ScrollRect>(true);
        if (scroll == null) return;
        int rows = Mathf.CeilToInt(visibleCards.Count / (float)Mathf.Max(1, cardColumns));
        int row = focusIndex / Mathf.Max(1, cardColumns);
        scroll.verticalNormalizedPosition = rows <= 1 ? 1f : 1f - row / (float)(rows - 1);
    }

    // 「詳細」：説明の全文と補足をカードの近くにポップアップで出す（同時に 1 つだけ）
    void OpenDetail(SkillCardView card)
    {
        if (card == null || card.Skill == null) return;
        int index = visibleCards.IndexOf(card);
        if (index >= 0) SetFocus(index, false);
        SkillDetailPopup.Open(card.Skill, (RectTransform)card.transform, DetailSources());
    }

    RulesSources DetailSources()
    {
        if (detailSources != null) return detailSources;
        detailSources = new RulesSources();
        if (database != null) detailSources.Magics = database.Selectable;
        ForgeRecipeBook book = ForgeRecipeBook.LoadDefault();
        if (book != null) detailSources.ForgeRecipes = book.recipes;
        return detailSources;
    }

    void Update()
    {
        if (!IsSelectionOpen) return;
        Keyboard k = Keyboard.current;
        if (k == null || Time.frameCount == selectionOpenedFrame) return; // 枠を押した Enter を決定として読まない
        bool esc = k.escapeKey.wasPressedThisFrame;
        bool info = k.iKey.wasPressedThisFrame;
        bool enter = k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame || k.fKey.wasPressedThisFrame;

        // 「詳細」を開いている間は、Esc（と I・Enter）で閉じるだけ
        if (SkillDetailPopup.IsOpen)
        {
            if (esc || info || enter) SkillDetailPopup.CloseCurrent();
            return;
        }
        if (RulesOverlayView.IsBlockingInput) return;

        int cols = Mathf.Max(1, cardColumns);
        if (k.leftArrowKey.wasPressedThisFrame || k.aKey.wasPressedThisFrame) SetFocus(focusIndex - 1, true);
        else if (k.rightArrowKey.wasPressedThisFrame || k.dKey.wasPressedThisFrame) SetFocus(focusIndex + 1, true);
        else if (k.upArrowKey.wasPressedThisFrame || k.wKey.wasPressedThisFrame) SetFocus(focusIndex - cols < 0 ? focusIndex : focusIndex - cols, true);
        else if (k.downArrowKey.wasPressedThisFrame || k.sKey.wasPressedThisFrame) SetFocus(focusIndex + cols >= visibleCards.Count ? focusIndex : focusIndex + cols, true);

        SkillCardView focused = focusIndex >= 0 && focusIndex < visibleCards.Count ? visibleCards[focusIndex] : null;
        if (info && focused != null) OpenDetail(focused);
        else if (enter && focused != null && focused.CanChoose) OnSkillSelected(focused.Skill);
        else if (esc) CloseSelection();
    }

    void OnDisable()
    {
        SkillDetailPopup.CloseCurrent();
    }

    SkillDefinition[] SlotsOf(PlayerType player)
    {
        return player == PlayerType.Player1 ? player1SelectedSkills : player2SelectedSkills;
    }

    // ------------------------------------------------------------
    //  表示
    // ------------------------------------------------------------

    private void UpdateSlotDisplays()
    {
        if (!MatchSetup.IsCpu(PlayerType.Player1)) UpdateGroup(player1SelectedSkills, player1SlotButtons, player1SlotTexts);
        if (!MatchSetup.IsCpu(PlayerType.Player2)) UpdateGroup(player2SelectedSkills, player2SlotButtons, player2SlotTexts);

        if (playButton != null)
        {
            playButton.interactable = IsReady(player1SelectedSkills) && IsReady(player2SelectedSkills);
        }
    }

    void UpdateGroup(SkillDefinition[] skills, Button[] buttons, TextMeshProUGUI[] texts)
    {
        for (int i = 0; i < SkillRanks.SlotCount; i++)
        {
            SkillRank rank = SkillRanks.RankOfSlot(i);
            int indexInRank = i - SkillRanks.FirstSlot(rank);
            bool exists = indexInRank < Candidates(rank).Count; // 候補が足りない枠は使わない
            bool locked = IsLocked(rank);

            if (buttons != null && i < buttons.Length && buttons[i] != null)
            {
                buttons[i].interactable = exists && !locked;
            }
            if (texts != null && i < texts.Length && texts[i] != null)
            {
                string label = SkillRanks.ShortLabel(rank);
                SkillDefinition skill = skills[i];
                if (!exists) texts[i].text = $"{label}：（候補なし）";
                else if (skill == null) texts[i].text = $"{label}：未選択";
                else texts[i].text = locked ? $"{label}：{skill.skillName}（固定）" : $"{label}：{skill.skillName}";
            }
        }
    }

    // ランクごとに選ぶ数がそろっているか
    bool IsReady(SkillDefinition[] slots)
    {
        foreach (SkillRank rank in SkillRanks.All)
        {
            int first = SkillRanks.FirstSlot(rank);
            int picked = 0;
            for (int i = 0; i < SkillRanks.Pick(rank); i++)
            {
                if (slots[first + i] != null) picked++;
            }
            if (picked < Need(rank)) return false;
        }
        return true;
    }

    // ゲーム開始ボタンがクリックされたときに呼び出す
    public void OnPlayButtonClicked()
    {
        if (!IsReady(player1SelectedSkills) || !IsReady(player2SelectedSkills))
        {
            Debug.LogWarning("魔法の編成が終わっていません（通常魔法を3つ選んでください）");
            return;
        }

        SkillDeck deck1 = ScriptableObject.CreateInstance<SkillDeck>();
        deck1.skills = new List<SkillDefinition>(player1SelectedSkills);

        SkillDeck deck2 = ScriptableObject.CreateInstance<SkillDeck>();
        deck2.skills = new List<SkillDefinition>(player2SelectedSkills);

        DeckTransferManager.Player1SelectedDeck = deck1;
        DeckTransferManager.Player2SelectedDeck = deck2;

        if (sceneManager != null)
        {
            sceneManager.StartMatch();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Board");
        }
    }
}
