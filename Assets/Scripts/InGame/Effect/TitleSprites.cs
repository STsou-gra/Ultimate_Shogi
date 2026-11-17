using UnityEngine;

// タイトル画面（TitleView）の飾りの絵（実行時に作る。画像のアセットを持たない。MagicSprites の続き）
public static partial class MagicSprites
{
    private static Sprite pieceShape;

    // 将棋の駒の形（五角形。木の色に縁取り。文字は TitleView が重ねる）
    public static Sprite PieceShape => pieceShape != null ? pieceShape : (pieceShape = Make(128, 0.5f, (ux, uy) =>
    {
        Vector2 p = new Vector2(ux, uy);
        Vector2[] outline =
        {
            new Vector2(0f, 0.92f), new Vector2(0.56f, 0.62f), new Vector2(0.74f, -0.88f), new Vector2(-0.74f, -0.88f), new Vector2(-0.56f, 0.62f),
        };
        float d = PolygonDistance(p, outline);
        Color c = new Color(0f, 0f, 0f, 0f);
        c = Over(c, Hex(0x6d4413), Mathf.Clamp01((0.03f - d) * 50f));
        c = Over(c, Hex(0xf3d08f), Mathf.Clamp01((-0.03f - d) * 50f));
        // 内側を少し明るく・木目
        c = Over(c, Hex(0xfff3d6), Mathf.Clamp01((-0.16f - d) * 50f) * 0.35f);
        float grain = Mathf.Abs(Mathf.Sin((ux * 3f + uy * 0.4f) * 9f));
        c = Over(c, Hex(0xc49a52), Mathf.Clamp01((-0.05f - d) * 50f) * Mathf.Clamp01((0.12f - grain) * 5f) * 0.25f);
        return c;
    }, FilterMode.Bilinear));
}
