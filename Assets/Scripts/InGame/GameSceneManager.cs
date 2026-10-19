using UnityEngine;
using UnityEngine.SceneManagement; // シーン管理
using TMPro;

// 対局結果を GameOver シーンへ受け渡す
public static class GameResult
{
    public static string Message { get; set; } = "";
}

public class GameSceneManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI resultText; // GameOver シーンの勝敗表示（他のシーンでは未設定でよい）

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (resultText != null && !string.IsNullOrEmpty(GameResult.Message))
        {
            resultText.text = GameResult.Message;
        }
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
