using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 魔法スキルの編成画面（Prototype の src/scenes/LoadoutScene.js）。対局の前に、各プレイヤーがランクごとに魔法を選ぶ。
//  ・枠は 1 人 5 つ：0〜2 = 通常魔法（3 つ選ぶ）、3 = 必殺魔法（1 つ）、4 = 超常魔法（1 つ）（SkillRanks）
//  ・枠を押すと、その枠のランクの魔法だけが選択肢に並ぶ（同じ魔法は 2 つの枠に入れられない）
//  ・候補が選ぶ数以下のランクは、全部を選んだ状態で固定（枠は押せない）。候補がないランクの枠は「候補なし」
//  ・すべての枠が埋まると「対局開始」を押せる
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

    void Start()
    {
        if (playButton == null)
        {
            GameObject start = GameObject.Find("StartButton");
            if (start != null) playButton = start.GetComponent<Button>();
        }
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
            if (skill != null && skill.rank == rank && !result.Contains(skill)) result.Add(skill);
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

        //データベースに登録されている魔法の数だけボタンを生成（表示するのは、押した枠のランクの魔法だけ）
        foreach (SkillRank rank in SkillRanks.All)
        {
            foreach (SkillDefinition skill in Candidates(rank))
            {
                //プレハブからボタンを生成してコンテナに入れる
                GameObject buttonObject = Instantiate(skillButtonPrefab, selectionButtonsContainer);
                spawnedButtons.Add(buttonObject);
                spawnedSkills.Add(skill);

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
            Button button = buttonObject.GetComponent<Button>();
            if (button != null) button.interactable = !usedElsewhere;
            TextMeshProUGUI text = buttonObject.GetComponentInChildren<TextMeshProUGUI>();
            if (text != null)
            {
                string mark = slots[slotIndex] == skill ? "● " : usedElsewhere ? "（選択済み）" : "";
                text.text = $"{mark}{skill.skillName}（コスト：{skill.cost}）";
            }
        }

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
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(false);
        }
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(false);
        }
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
