using UnityEngine;
using UnityEngine.SceneManagement; // シーン管理

// シーンの切り替えと、タイトル画面の入口。
// GameTitle シーンでは、Start でタイトル画面（TitleView。Prototype の src/scenes/TitleScene.js）を開く：
//  「通常対局」→ 対戦モードの画面（ModeSelectView）／「討伐任務」→ ボス選択の画面（BossSelectView）／「ルール」／「棋譜を読み込んで再生」。
// タイトル画面は実行時に Canvas を作る（シーンに元からある「ゲームスタート」のボタンの Canvas は TitleView が隠す）
public class GameSceneManager : MonoBehaviour
{
    void Start()
    {
        // タイトルのシーンなら、タイトル画面を開く（結果画面の「タイトルへ」「ボス選択へ」で戻ってきたときも）
        if (SceneManager.GetActiveScene().name == TitleView.SceneName) TitleView.Open();
    }

    public void GameTitle(){
        //GameTitle という名前のシーンに切り替える
        SceneManager.LoadScene(TitleView.SceneName);
    }

    // ゲームオーバー時にこれを呼び出す
    public void GameOver()
    {
        // "GameOverScene" という名前のシーンに切り替える
        SceneManager.LoadScene("GameOver");
    }

    // 通常対局：対戦モードを選ぶ画面（2人で対戦／CPU と対戦）を開く。
    // 選んだら ModeSelectView が MatchSetup を決めてスキル編成画面へ移る（タイトル画面の「通常対局」と同じ。古い「ゲームスタート」のボタンからも呼べる）
    public void PlayGame(){
        TitleView.LastMode = "versus";
        ModeSelectView.Open();
    }

    // 討伐任務：ボス選択の画面を開く（タイトル画面の「討伐任務」と同じ）
    public void PlayRaid(){
        TitleView.LastMode = "raid";
        BossSelectView.Open();
    }

    // スキル編成画面へ（対戦モードを選ばずに。MatchSetup は前回のまま）
    public void Organization(){
        SceneManager.LoadScene("OrganizationScene");
    }

    // スキル編成画面の「対局」から呼び出す（対局画面へ）
    public void StartMatch(){
        SceneManager.LoadScene("Board");
        Debug.Log("ゲームスタート");
    }
}
