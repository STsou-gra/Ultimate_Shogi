using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class DeckBuildController : MonoBehaviour
{
    [Header("Skill Database Reference")]
    [SerializeField] private SkillDatabase database;

    [Header("Player 1 UI Slot References")]
    [SerializeField] private Button[] player1SlotButtons; // 要素数5
    [SerializeField] private TextMeshProUGUI[] player1SlotTexts; // 要素数5

    [Header("Player 2 UI Slot References")]
    [SerializeField] private Button[] player2SlotButtons; // 要素数5
    [SerializeField] private TextMeshProUGUI[] player2SlotTexts; // 要素数5

    [Header("Selection Panel UI References")]
    [SerializeField] private GameObject allSkillsSelectionPanel; // 全スキル選択パネル
    [SerializeField] private Button[] selectionButtons; // パネル内の選択肢ボタン（あらかじめエディタ上で配置した固定数）
    [SerializeField] private TextMeshProUGUI[] selectionButtonTexts; // 選択肢ボタンのテキスト
    [SerializeField] private Button removeSkillButton; // 「空（なし）」に設定するクリアボタン

    [Header("Scene Manager Reference")]
    [SerializeField] private GameSceneManager sceneManager;

    // 現在の選択状態を保持する変数
    private PlayerType currentSelectingPlayer;
    private int currentSelectingSlotIndex;

    // プレイヤーが選んだスキルの仮リスト
    private SkillDefinition[] player1SelectedSkills = new SkillDefinition[5];
    private SkillDefinition[] player2SelectedSkills = new SkillDefinition[5];

    void Start()
    {
        InitializeSelectionPanel();
        UpdateAllSlotDisplays();
    }

    // 選択パネル内のボタンにデータベースのスキルをアサインする
    private void InitializeSelectionPanel()
    {
        if (database == null || selectionButtons == null)
        {
            Debug.LogError("SkillDatabase または選択ボタン配列がアサインされていません。");
            return;
        }

        // データベースの全スキルを選択パネル内のボタンに紐付ける
        for (int i = 0; i < selectionButtons.Length; i++)
        {
            if (i < database.allSkills.Count)
            {
                SkillDefinition skill = database.allSkills[i];
                if (skill != null)
                {
                    selectionButtons[i].gameObject.SetActive(true);
                    if (selectionButtonTexts != null && i < selectionButtonTexts.Length && selectionButtonTexts[i] != null)
                    {
                        selectionButtonTexts[i].text = $"{skill.skillName} (コスト:{skill.cost})";
                    }
                }
                else
                {
                    selectionButtons[i].gameObject.SetActive(false);
                }
            }
            else
            {
                // データベースのスキル数を超えた分の静的配置ボタンは非表示にする
                selectionButtons[i].gameObject.SetActive(false);
            }
        }

        // 選択パネルは初期状態で非表示にしておく
        if (allSkillsSelectionPanel != null)
        {
            allSkillsSelectionPanel.SetActive(false);
        }
    }

    // ========================================================
    // 1. 各プレイヤーのスロットボタンが押された時の処理
    // ========================================================
    public void OnSlotButtonClicked(int playerNum, int slotIndex)
    {
        currentSelectingPlayer = (playerNum == 1) ? PlayerType.Player1 : PlayerType.Player2;
        currentSelectingSlotIndex = slotIndex;

        // 全スキル選択パネルを開く
        if (allSkillsSelectionPanel != null)
        {
            allSkillsSelectionPanel.SetActive(true);
        }
    }

    // ========================================================
    // 2. 選択パネル内でスキルが選ばれた時の処理
    // ========================================================
    public void OnSkillSelected(int skillIndex)
    {
        if (database == null || skillIndex < 0 || skillIndex >= database.allSkills.Count) return;

        SkillDefinition selectedSkill = database.allSkills[skillIndex];
        AssignSkillToCurrentSlot(selectedSkill);
    }

    // ========================================================
    // 3. 選択パネル内で「空（なし）」にするボタンが押された時の処理
    // ========================================================
    public void OnClearSelected()
    {
        AssignSkillToCurrentSlot(null);
    }

    private void AssignSkillToCurrentSlot(SkillDefinition skill)
    {
        if (currentSelectingPlayer == PlayerType.Player1)
        {
            player1SelectedSkills[currentSelectingSlotIndex] = skill;
        }
        else
        {
            player2SelectedSkills[currentSelectingSlotIndex] = skill;
        }

        // UI表示を更新し、選択パネルを閉じる
        UpdateAllSlotDisplays();
        if (allSkillsSelectionPanel != null)
        {
            allSkillsSelectionPanel.SetActive(false);
        }
    }

    // スロットの表示テキストを現在選択されているスキル名に更新する
    private void UpdateAllSlotDisplays()
    {
        System.Action<SkillDefinition[], TextMeshProUGUI[]> updateGroup = (skills, texts) =>
        {
            if (texts == null) return;
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null)
                {
                    SkillDefinition skill = skills[i];
                    texts[i].text = (skill != null) ? skill.skillName : "なし";
                }
            }
        };

        updateGroup(player1SelectedSkills, player1SlotTexts);
        updateGroup(player2SelectedSkills, player2SlotTexts);
    }

    // ========================================================
    // 4. 決定（ゲーム開始）ボタンが押された時の処理
    // ========================================================
    public void OnPlayButtonClicked()
    {
        // プレイヤー1用のランタイムデッキアセット生成
        SkillDeck deck1 = ScriptableObject.CreateInstance<SkillDeck>();
        deck1.skills = new List<SkillDefinition>(player1SelectedSkills);

        // プレイヤー2用のランタイムデッキアセット生成
        SkillDeck deck2 = ScriptableObject.CreateInstance<SkillDeck>();
        deck2.skills = new List<SkillDefinition>(player2SelectedSkills);

        // シーン遷移用マネージャーへ引き渡し
        DeckTransferManager.Player1SelectedDeck = deck1;
        DeckTransferManager.Player2SelectedDeck = deck2;

        // シーンをロード
        if (sceneManager != null)
        {
            sceneManager.PlayGame();
        }
        else
        {
            UnityEngine.SceneManagement.SceneManager.LoadScene("Board");
        }
    }
}
