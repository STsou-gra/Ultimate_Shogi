using UnityEngine;

public class SkillManager : MonoBehaviour
{
    public GameBoard gameBoard;

    void State()
    {
        gameBoard = Object.FindAnyObjectByType<GameBoard>();
    }
    // ボタンが押された時に実行されるメソッド
    public virtual void OnButtonClick()
    {
        Debug.Log("Skillボタンが押されました！");
    }
}
