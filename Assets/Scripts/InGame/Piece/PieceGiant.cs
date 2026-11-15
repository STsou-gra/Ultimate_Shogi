using UnityEngine;

// 巨人（Prototype の src/defs/pieces.js の KYOJIN。錬成駒）
// 3×3 マスを占める巨大な駒（X・Y は中央のマス。GameBoard は占めるマス全部に同じ駒を入れる）。持ち駒から打つときは中央のマスを選ぶ。
// 前へまっすぐ 1〜5 マスだけ動く（移動先は先頭の 1 列の中央のマス。判定は GiantRules・CheckRules.GiantMoves、動かすのは GameSystem.MoveGiant）。
// 先頭の 1 列の相手の駒をまとめて取る。成らない。HP 3：攻撃を受けるたびに 1 減り、3 回目で倒れる（GameSystem.HitGiant。2 回目までは攻撃した駒が跳ね返る）。
// 盾・ファランクス・カウンターは付けられず、自分の魔法の起点にもできない（MagicKit.IsOwnPiece）。
// 見た目：本体はプレハブ（3×3 マスの大きさ）。HP を駒の下側に ♥ で出し、減るほど本体を暗くする（Prototype の pieceView.js の giantHp と Game.updatePieceView）
public class PieceGiant : GamePiece
{
    static readonly Color FullHeart = new Color(1f, 0.32f, 0.32f, 1f);
    static readonly Color EmptyHeart = new Color(0.25f, 0.12f, 0.12f, 0.55f);
    static readonly Color[] BodyTint = { new Color(0.63f, 0.53f, 0.5f), new Color(0.84f, 0.8f, 0.78f), Color.white }; // HP 1・2・3

    private SpriteRenderer[] hearts;
    private SpriteRenderer[] bodies;
    private int shownHp = -1;

    protected override bool CanMoveByPattern(int toX, int toY)
    {
        return false; // 動きは GiantRules（GamePiece.CanMove を使う判定からは動けない駒として扱う）
    }

    void LateUpdate()
    {
        if (hearts == null) CreateHearts();
        if (hp == shownHp) return;
        shownHp = hp;
        int max = GiantRules.MaxHp;
        for (int i = 0; i < hearts.Length; i++) hearts[i].color = i < hp ? FullHeart : EmptyHeart;
        Color tint = BodyTint[Mathf.Clamp(hp, 1, max) - 1];
        foreach (SpriteRenderer sr in bodies)
        {
            if (sr != null) sr.color = tint;
        }
    }

    // HP の ♥（駒のローカル座標で下側に横に並べる。駒と一緒に回る）
    void CreateHearts()
    {
        int max = GiantRules.MaxHp;
        hearts = new SpriteRenderer[max];
        bodies = GetComponentsInChildren<SpriteRenderer>(true);
        int order = 0;
        foreach (SpriteRenderer sr in bodies) order = Mathf.Max(order, sr.sortingOrder);
        for (int i = 0; i < max; i++)
        {
            SpriteRenderer sr = new GameObject("GiantHp").AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.transform.localPosition = new Vector3((i - (max - 1) * 0.5f) * 0.2f, -0.42f, -0.01f);
            sr.transform.localScale = Vector3.one * 0.17f;
            sr.sprite = MagicSprites.Heart;
            sr.sortingOrder = order + 1;
            hearts[i] = sr;
        }
    }
}
