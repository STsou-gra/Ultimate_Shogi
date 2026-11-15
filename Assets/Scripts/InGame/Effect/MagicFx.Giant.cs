using DG.Tweening;
using UnityEngine;

// 錬成駒「巨人」が攻撃を受けたときの演出（Prototype の view/events.js の giantHit）。MagicFx の続き（partial）
//  HP が残った … 光の粒・衝撃波・画面シェイク・駒が揺れる・「耐えた！ 残り N」
//  倒れた     … 同じ演出に「巨人 撃沈！」（続けて取り・消失の演出）
public partial class MagicFx
{
    private static readonly Color GiantHitColor = Hex(0xffab40);
    private static readonly Color GiantDustColor = Hex(0x8d6e63);
    private static readonly Color GiantOutlineColor = Hex(0x3e2723);

    void OnGiantHit(GiantHitInfo info)
    {
        GamePiece piece = info.Piece;
        if (piece == null) return;
        At(info.Delay, () =>
        {
            if (piece == null) return;
            Vector3 pos = piece.transform.position;
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 26; burst.colors = new[] { Hex(0xffcc80), GiantDustColor, Color.white };
            burst.speed = 260f * PX; burst.life = 0.55f; burst.size = 0.26f;
            fx.Burst(pos, burst);
            fx.Shockwave(pos, GiantHitColor, 3.2f, 0.4f);
            fx.Shake(7f, 0.22f);
            Transform t = piece.transform;
            if (!DOTween.IsTweening(t))
            {
                // 動いている途中（移動・跳ね返り）でなければ、横に揺れる
                Vector3 home = t.localPosition;
                DOTween.Sequence().SetLink(t.gameObject)
                    .Append(t.DOLocalMove(home + new Vector3(0.12f, 0f, 0f), 0.05f))
                    .Append(t.DOLocalMove(home, 0.4f).SetEase(Ease.OutElastic));
            }
            Vector3 textPos = pos + ScreenUp * (66f * 1.1f * PX);
            if (info.Hp > 0) PopText($"耐えた！ 残り {info.Hp}", textPos, 34f, Color.white, GiantHitColor, GiantOutlineColor, 40f, 0.9f);
            else PopText("巨人 撃沈！", textPos, 38f, Color.white, Hex(0xff5252), GiantOutlineColor, 40f, 1.0f);
        });
    }
}

// 巨人の HP の ♥ の画像（PieceGiant が駒の下側に並べる）
public static partial class MagicSprites
{
    private static Sprite heart;

    public static Sprite Heart => heart != null ? heart : (heart = Make(64, 0.5f, (ux, uy) =>
    {
        // ハートの陰関数 (x² + y² - 1)³ - x² y³ ≤ 0（少し下げて中央に寄せる）
        float x = ux * 1.25f, y = uy * 1.25f + 0.15f;
        float a = x * x + y * y - 1f;
        float v = a * a * a - x * x * y * y * y;
        float edge = Mathf.Clamp01(-v * 6f);
        return new Color(1f, 1f, 1f, edge);
    }, FilterMode.Bilinear));
}
