using UnityEngine;
using UnityEngine.SceneManagement; // シーン管理

public class GameSceneManager : MonoBehaviour
{
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void GameTitle(){
        //GameTitle という名前のシーンに切り替える
        SceneManager.LoadScene("GameTitle");
    }

    // ゲームオーバー時にこれを呼び出す
    public void GameOver()
    {
        // "GameOverScene" という名前のシーンに切り替える
        SceneManager.LoadScene("GameOver");
    }

    // タイトルの「ゲームスタート」から呼び出す：対戦モードを選ぶ画面（2人で対戦／CPU と対戦）を開く。
    // 選んだら ModeSelectView が MatchSetup を決めてスキル編成画面へ移る
    public void PlayGame(){
        ModeSelectView.Open();
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

    /*
    public void sceneManager(String sceneName){
        SceneManager.LoadScene(sceneName);
    }
    */
}
