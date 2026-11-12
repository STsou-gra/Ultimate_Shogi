using UnityEngine;
using TMPro;

// 結果画面の勝者テキストに付けて使う
[RequireComponent(typeof(TextMeshProUGUI))]
public class GameResultDisplay : MonoBehaviour
{
    void Start()
    {
        TextMeshProUGUI resultText = GetComponent<TextMeshProUGUI>();
        if (GameResult.IsDraw)
        {
            resultText.text = "引き分け!";
            return;
        }

        // 対局を経由せずに結果画面を開いた場合は、シーンに書かれたテキストのままにする
        if (GameResult.Winner == null) return;

        PlayerType winner = GameResult.Winner.Value;
        if (MatchSetup.IsVsCpu)
        {
            // CPU と対戦：「あなたの勝利!」「CPU の勝利…」
            resultText.text = MatchSetup.IsCpu(winner) ? $"CPU（{CpuLevels.Label(MatchSetup.CpuLevel)}）の勝利…" : "あなたの勝利!";
            return;
        }
        string winnerName = (winner == PlayerType.Player1) ? "Player 1" : "Player 2";
        resultText.text = $"{winnerName} の勝利!";
    }
}
