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

    public void PlayGame(){
        SceneManager.LoadScene("Board");
        Debug.Log("ゲームスタート");
    }

    // タイトルのスタートボタンから呼び出し、スキル編成画面へ切り替える
    public void OrganizeDeck(){
        SceneManager.LoadScene("OrganizationScene");
    }

    /*
    public void sceneManager(String sceneName){
        SceneManager.LoadScene(sceneName);
    }
    */
}
