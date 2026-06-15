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

    /*
    public void sceneManager(String sceneName){
        SceneManager.LoadScene(sceneName);
    }
    */
}
