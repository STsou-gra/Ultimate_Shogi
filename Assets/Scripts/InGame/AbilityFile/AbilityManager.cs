using UnityEngine;

public class AbilityManager : MonoBehaviour
{
    public GameBoard gameBoard;

    void State(){
        gameBoard = Object.FindObjectsByType<GameBoard>(FindObjectsSortMode.None)[0];
    }
    // ボタンが押された時に実行されるメソッド
    public virtual void OnButtonClick()
    {
        Debug.Log("Abilityボタンが押されました！");
    }
}
