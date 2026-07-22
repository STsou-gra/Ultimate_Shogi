using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using NUnit.Framework;

public class DeckSelectController : MonoBehaviour
{
    [Header("Skill Database Reference")]
    [SerializeField] private SkillDatabase database;

    [Header("Player1 UI Slot References")]
    [SerializeField] private Button[] player1SlotButtons; //要素数5
    [SerializeField] private TextMeshProUGUI[] player1SlotTexts; //要素数5

    [Header("Player2 UI Slot References")]
    [SerializeField] private Button[] player2SlotButtons; //要素数5
    [SerializeField] private TextMeshProUGUI[] player2SlotTexts; //要素数5

    [Header("Selection Panel UI References")]
    [SerializeField] private GameObject skillSelectionPanel; //スキル選択パネル
    [SerializeField] private RectTransform selectionButtonsContainer; //生成したボタンを並べる親（ScrollViewのContent等）
    [SerializeField] private GameObject skillButtonPrefab; //生成する選択肢ボタンのプレハブ
    [SerializeField] private Button removeSkillButton; //スキルなしに設定するクリアボタン

    [Header("Scene Manager Reference")]
    [SerializeField] private GameSceneManager sceneManager;

    // 現在の選択状態を保持する変数
    private PlayerType currentSelectingPlayer;
    private int currentSelectingSlotIndex;

    //プレイヤーが選んだスキルの仮リスト
    private SkillDefinition[] player1SelectedSkills = new SkillDefinition[5];
    private SkillDefinition[] player2SelectedSkills = new SkillDefinition[5];

    //生成したボタンの管理用リスト（再初期化時の破棄用）
    private List<GameObject> spawnedButtons = new List<GameObject>();

    void Start()
    {
        InitializeSelectionPanel();
        UpdateSlotDisplays();
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

        //データベースに登録されているスキルの数だけボタンを生成
        for (int i = 0; i < database.allSkills.Count; i++)
        {
            SkillDefinition skill = database.allSkills[i];
            if (skill == null)
            {
                continue;
            }

            //プレハブからボタンを生成してコンテナに入れる
            GameObject gameObject = Instantiate(skillButtonPrefab, selectionButtonsContainer);
            spawnedButtons.Add(gameObject);

            //テキストの設定
            TextMeshProUGUI textComponent = gameObject.GetComponentInChildren<TextMeshProUGUI>();
            if (textComponent != null)
            {
                textComponent.text = $"{skill.skillName}（コスト：{skill.cost}）";
            }

            //クリックイベントの登録
            Button buttonComponent = gameObject.GetComponent<Button>();
            if (buttonComponent != null)
            {
                int index = i; //ラムダ式のクロージャ用
                buttonComponent.onClick.AddListener(() => OnSkillSelected(index));
            }
        }

        //なしボタンのイベント設定
        if (removeSkillButton != null)
        {
            removeSkillButton.onClick.RemoveAllListeners();
            removeSkillButton.onClick.AddListener(OnClearSelected);
        }

        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(false); //「なし」ボタンは初期状態で非表示にする
        }

        //選択パネルは初期状態で非表示にする
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(false);
        }
    }

    //Player1のスロットボタンが押された時（UIから呼び出す）
    public void Player1OnSlotButtonClicked(int slotIndex)
    {
        currentSelectingSlotIndex = slotIndex;
        currentSelectingPlayer = PlayerType.Player1;

        //スキル選択パネルを表示
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(true);
        }
        //「削除」ボタンを表示する
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(true); //「削除」ボタンは初期状態で非表示にする
        }
    }

    //Player2のスロットボタンが押された時（UIから呼び出す）
    public void Player2OnSlotButtonClicked(int slotIndex)
    {
        currentSelectingSlotIndex = slotIndex;
        currentSelectingPlayer = PlayerType.Player2;

        //スキル選択パネルを表示
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(true);
        }
        //「削除」ボタンを表示する
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(true); //「削除」ボタンは初期状態で非表示にする
        }
    }

    public void OnSkillSelected(int skillIndex)
    {
        if (database == null || skillIndex < 0 || skillIndex >= database.allSkills.Count)
        {
            return;
        }

        SkillDefinition selectedSkill = database.allSkills[skillIndex];
        AssignSkillToCurrentSlot(selectedSkill);
    }

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

        UpdateSlotDisplays();
        if (skillSelectionPanel != null)
        {
            skillSelectionPanel.SetActive(false);
        }
        //「削除」ボタンを表示する
        if (removeSkillButton != null)
        {
            removeSkillButton.gameObject.SetActive(false); //「削除」ボタンは初期状態で非表示にする
        }
    }


    private void UpdateSlotDisplays()
    {
        System.Action<SkillDefinition[], TextMeshProUGUI[]> updateGroup = (skills, texts) =>
        {
            if (texts == null)
            {
                return;
            }
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

    // ゲーム開始ボタンがクリックされたときに呼び出す
    public void OnPlayButtonClicked()
    {
        SkillDeck deck1 = ScriptableObject.CreateInstance<SkillDeck>();
        deck1.skills = new List<SkillDefinition>(player1SelectedSkills);

        SkillDeck deck2 = ScriptableObject.CreateInstance<SkillDeck>();
        deck2.skills = new List<SkillDefinition>(player2SelectedSkills);

        DeckTransferManager.Player1SelectedDeck = deck1;
        DeckTransferManager.Player2SelectedDeck = deck2;

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
