using UnityEngine;

public class Abillity1 : AbilityManager
{

    // ボタンが押された時に実行されるメソッド
    public override void OnButtonClick()
    {
        Debug.Log("player1のボタンが正しく押されました！");
        if (gameBoard == null) gameBoard = GameObject.FindAnyObjectByType<GameBoard>();
        gameBoard.ChangeAbilityOnOff(1);
    }
}
