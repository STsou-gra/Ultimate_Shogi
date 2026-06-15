using UnityEngine;

public class Ability2 : AbilityManager
{

    // ボタンが押された時に実行されるメソッド
    public override void OnButtonClick()
    {
        Debug.Log("player2のボタンが正しく押されました！");
        if (gameBoard == null) gameBoard = GameObject.FindAnyObjectByType<GameBoard>();
        gameBoard.ChangeAbilityOnOff(1);
    }
}
