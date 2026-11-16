using UnityEngine;

// 小赤鬼（Prototype の src/defs/pieces.js の KOONI。討伐任務のボス「赤鬼王」の手下）
// ボスの席（後手）の駒として盤に置かれ、3 秒ごとに周り 8 方向へ 1 マス動く（動かすのは Match/BossEngine.cs。自分では動けない）。
// プレイヤーの駒のマスへ動くと、その駒はボスの攻撃を受ける。プレイヤーの駒で取れる・狙撃・魔法・爆発で倒せる（持ち駒にはならない）。
// プレハブを持たず、GameBoard.SpawnPiece が Create で実行時に作る（絵は MagicSprites.Kooni。Effect/BossSprites.cs）
public class PieceKooni : GamePiece
{
    protected override bool CanMoveByPattern(int toX, int toY)
    {
        return false; // 自分では動けない（PieceMoves と同じ）
    }

    public static PieceKooni Create(Transform parent)
    {
        GameObject obj = new GameObject("Kooni");
        obj.transform.SetParent(parent, false);
        PieceKooni piece = obj.AddComponent<PieceKooni>();
        piece.type = PieceType.Kooni;

        GameObject visual = new GameObject("Normal Visual");
        visual.transform.SetParent(obj.transform, false);
        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = MagicSprites.Kooni;
        sr.sortingOrder = 10; // ほかの駒のプレハブと同じ
        piece.normalVisual = visual;
        return piece;
    }
}
