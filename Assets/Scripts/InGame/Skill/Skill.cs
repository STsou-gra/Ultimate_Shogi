using UnityEngine;

public class Skill : SkillManager
{
    // ボタンが押された時に実行されるメソッド
    public override void OnButtonClick()
    {
        Debug.Log("player1のボタンが正しく押されました！");
        GameSystem gameSystem = GameObject.FindAnyObjectByType<GameSystem>();
        if (gameSystem != null)
        {
            gameSystem.UseSkill(PlayerType.Player1, 0);
        }
    }
}
