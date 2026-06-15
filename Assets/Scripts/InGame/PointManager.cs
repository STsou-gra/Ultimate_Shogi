using UnityEngine;
using TMPro; // TextMeshProを使うために必要
using DG.Tweening; // DOTweenを使うために追加

public class PointManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI scoreText; // インスペクターからTextを紐付け
    private int _currentPoint = 0;

    // ポイントを変更するためのプロパティ
   public int CurrentPoint
    {
        get => _currentPoint;
        set 
        { 
            _currentPoint = value;
            UpdateDisplay();// 値が変わった時に呼び出される
            AnimateScore(); // 値が変わった時にアニメーションを実行
        }
    }

    void Start()
    {
        UpdateDisplay(); // 開始時に0を表示
    }

    // 表示を更新するメソッド
    private void UpdateDisplay()
    {
        if (scoreText != null)
        {
            scoreText.text = $"ポイント: {_currentPoint}";
        }
    }

    // 外部からポイントを加算したい時に呼ぶメソッド
    public void AddPoint(int amount)
    {
        CurrentPoint += amount;
    }

    private void AnimateScore()
    {
        // 1. スケール（大きさ）のアニメーション
        // 一瞬1.2倍に膨らんでから元のサイズ(1.0)に戻る「パンチ」演出
        scoreText.transform.DOPunchScale(new Vector3(0.2f, 0.2f, 0.2f), 0.3f);

        // 2. 色のアニメーション（例：一瞬黄色にしてから白に戻す）
        scoreText.DOColor(Color.yellow, 0.1f).OnComplete(() => {
            scoreText.DOColor(Color.white, 0.2f);
        });
    }
}