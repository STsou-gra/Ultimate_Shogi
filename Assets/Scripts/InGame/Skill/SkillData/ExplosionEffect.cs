using UnityEngine;

[CreateAssetMenu(menuName = "UltimateShogi/Effects/Explosion")]
public class ExplosionEffect : SkillEffect
{
  public override void Execute(SkillContext context)
  {
    GameBoard board = context.GameBoard;

    // 本来はプレイヤーが指定した座標(X, Y)を渡せるようにしますが、
    // 今回は例として「現在カーソルがある位置」を中心に爆発させます
    GameCursor cursor = Object.FindAnyObjectByType<GameCursor>();
    if (cursor == null) return;

    int centerX = cursor.X;
    int centerY = cursor.Y;

    Debug.Log($"中心位置 ({centerX}, {centerY}) で大爆発が発生！");

    // 中心から3x3マスの範囲をループ処理
    for (int x = centerX - 1; x <= centerX + 1; x++)
    {
      for (int y = centerY - 1; y <= centerY + 1; y++)
      {
        // 盤面の外（マイナス座標や9マス以上）はスルーする
        if (x < 0 || x >= 9 || y < 0 || y >= 9) continue;

        GamePiece piece = board.GetPieceAt(x, y);
        if (piece != null)
        {

          // AddPieceToHand を呼ばずに直接消去することで「誰も駒を取得できない」状態にする
          board.RemovePieceAt(x, y);

          // (ここに爆発のパーティクルエフェクトを再生するコードなどを入れると最高です)
          Debug.Log($"({x}, {y}) の駒が爆発に巻き込まれて消滅した。");
        }
      }
    }
  }
}