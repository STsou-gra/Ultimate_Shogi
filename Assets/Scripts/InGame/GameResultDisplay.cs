using UnityEngine;
using TMPro;

// 結果画面の勝者テキストに付けて使う
[RequireComponent(typeof(TextMeshProUGUI))]
public class GameResultDisplay : MonoBehaviour
{
    void Start()
    {
        // 対局を経由せずに結果画面を開いた場合は、シーンに書かれたテキストのままにする
        if (GameResult.Winner == null) return;

        TextMeshProUGUI resultText = GetComponent<TextMeshProUGUI>();
        string winnerName = (GameResult.Winner == PlayerType.Player1) ? "Player 1" : "Player 2";
        resultText.text = $"{winnerName} の勝利!";
    }
}
