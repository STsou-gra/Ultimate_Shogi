/*using UnityEngine;
using DG.Tweening; // DOTweenで演出を付ける場合

public class SelectionManager : MonoBehaviour
{
    [SerializeField] private GameObject selectionPanel; // 選択肢パネル
    [SerializeField] private CanvasGroup canvasGroup; // パネルのCanvasGroup（フェード用）

    void Start()
    {
        // 最初はパネルを完全に消しておく
        selectionPanel.SetActive(false);
        if (canvasGroup != null) canvasGroup.alpha = 0;
    }

    // 1. 最初のボタン（「メニューを開く」など）から呼ばれるメソッド
    public void OpenSelection()
    {
        selectionPanel.SetActive(true);
        
        // DOTweenでふわっと表示
        if (canvasGroup != null)
        {
            canvasGroup.DOFade(1f, 0.3f).SetUpdate(true); // SetUpdate(true)はゲーム停止中でも動く設定
        }
    }

    // 2. 選択肢ボタン（「Yes」「No」など）から呼ばれるメソッド
    public void OnSelect(string choiceName)
    {
        Debug.Log(choiceName + " が選ばれました！");

        // パネルを閉じる
        if (canvasGroup != null)
        {
            canvasGroup.DOFade(0f, 0.2f).OnComplete(() => selectionPanel.SetActive(false));
        }
        else
        {
            selectionPanel.SetActive(false);
        }
    }
}*/

using UnityEngine;
using TMPro;
using DG.Tweening;

public class SelectionManager : MonoBehaviour
{
    [SerializeField] private GameObject selectionPanel; // パネル本体
    [SerializeField] private CanvasGroup canvasGroup;    // 透明度制御用
    public Abillity1 ab;

    // パネルを表示する
    public void OpenSelection()
    {
        selectionPanel.SetActive(true); // オブジェクトを出現させる
        
        // DOTweenで0から1へふわっと表示（時間は0.3秒）
        canvasGroup.alpha = 0;
        canvasGroup.DOFade(1f, 0.3f).SetUpdate(true); // TimeScale=0でも動くように設定
    }

    // 各ボタンが押された時の処理
    public void OnClickOption(int index)
    {
        if (index == 1)
        {
            Debug.Log("選択肢1が選ばれました");
            ab.OnButtonClick();
        }
        if (index == 2)
        {
            Debug.Log("選択肢2が選ばれました");
        }

        CloseSelection(); // 最後にパネルを閉じる
    }

    private void CloseSelection()
    {
        canvasGroup.DOFade(0f, 0.2f).OnComplete(() => {
            selectionPanel.SetActive(false); // 消えるアニメが終わったら非アクティブに
        });
    }
}