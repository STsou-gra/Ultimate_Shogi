using UnityEngine;
using TMPro;

// 結果画面の勝者テキストに付けて使う
[RequireComponent(typeof(TextMeshProUGUI))]
public class GameResultDisplay : MonoBehaviour
{
    void Start()
    {
        // 棋譜の保存・読み込んで再生（画面の右下。KifuGameOverPanel）
        KifuGameOverPanel.Ensure();

        TextMeshProUGUI resultText = GetComponent<TextMeshProUGUI>();
        // 討伐任務：討伐成功（クリアタイム）／討伐失敗
        RaidResultInfo raid = GameResult.Raid;
        if (raid != null)
        {
            resultText.richText = true;
            resultText.text = raid.Cleared
                ? $"討伐成功!\n<size=55%>{raid.BossName}を倒した　クリアタイム {raid.TimeLabel}</size>"
                : $"討伐失敗…\n<size=55%>{(raid.Outcome == RaidOutcome.KingLost ? "王将が倒れた" : "王将がボスの攻撃に耐えきれなかった")}" +
                  $"（{raid.BossName}の残り HP {raid.BossHp} / {raid.BossMaxHp}・{raid.TimeLabel}）</size>";
            return;
        }
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
