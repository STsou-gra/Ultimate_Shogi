using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Skill Effect/Replicate King")]
public class ReplicateKingEffect : SkillEffect
{
    public override void Execute(SkillContext context)
    {
        GameBoard board = context.GameBoard;
        PlayerType self = context.Player;

        // 例：自陣の特定の空いているマス（例: x=0, y=4 など）に2枚目の王将をスポーンさせる
        // (本来は空いているマスを検索して配置するロジックにするのが安全です)
        int targetX = (self == PlayerType.Player1) ? 0 : 8;
        int targetY = 4;

        // 破壊マス・落とし穴のマスには出せない
        if (board.GetPieceAt(targetX, targetY) == null && board.Cells.CanDropOn(targetX, targetY))
        {
            board.SpawnPiece(PieceType.King, self, targetX, targetY);
            board.EnterCell(board.GetPieceAt(targetX, targetY), targetX, targetY); // 炎上マスなら消失する
            Debug.Log($"{self} はスキルによって2枚目の王将を召喚した！");
        }
    }
}
