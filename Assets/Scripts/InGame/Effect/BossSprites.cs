using UnityEngine;

// 討伐任務のボス・手下の絵（実行時に作る。Prototype の src/view/bossView.js が Pixi の Graphics で描くのと同じく、図形だけで描いたオリジナルの鬼）。
// 画像のアセット・プレハブを持たないので、シーンやプレハブを変えずに使える（MagicSprites の続き）
public static partial class MagicSprites
{
    private static Sprite kooni, akaoni, club, bossArrow;

    // 小赤鬼（駒 1 マスぶん。丸い赤い顔・角 2 本・つり目・牙）
    public static Sprite Kooni => kooni != null ? kooni : (kooni = Make(96, 0.44f, (ux, uy) => OniFace(ux, uy, false), FilterMode.Bilinear));

    // 赤鬼王（3×3 マスぶん。王冠つきの大きな赤鬼）
    public static Sprite Akaoni => akaoni != null ? akaoni : (akaoni = Make(256, 1.5f, (ux, uy) => OniFace(ux, uy, true), FilterMode.Bilinear));

    // 金棒（+X 向き。基準点は持ち手の端。長さ 1unit × 太さ 0.5unit。向きの印・薙ぎ払いの演出に使う）
    public static Sprite Club => club != null ? club : (club = Make(128, 0.5f, (ux, uy) =>
    {
        // 持ち手（細い）から先（太い）へ。ux = -1（持ち手の端）〜 1（先）
        float t = (ux + 1f) * 0.5f;
        float half = Mathf.Lerp(0.07f, 0.24f, Mathf.Clamp01((t - 0.15f) / 0.85f));
        float d = Mathf.Abs(uy) * 0.5f - half; // uy は ±1 = ±0.5unit
        Color c = new Color(0f, 0f, 0f, 0f);
        if (t < 0f || t > 1f) return c;
        float a = Mathf.Clamp01(-d * 60f);
        Color body = t < 0.18f ? Hex(0x5d4037) : Hex(0x37474f);
        c = Over(c, body, a);
        // 縁
        c = Over(c, Hex(0x1b1b1b), Mathf.Clamp01(1f - Mathf.Abs(d) * 45f) * (a > 0f || d < 0.02f ? 1f : 0f));
        // いぼ（とげ）
        if (t > 0.3f)
        {
            float px = t * 12f, py = (uy * 0.5f / Mathf.Max(0.05f, half)) * 1.5f;
            float gx = px - Mathf.Floor(px) - 0.5f, gy = py - Mathf.Floor(py) - 0.5f;
            float dot = Mathf.Sqrt(gx * gx + gy * gy);
            c = Over(c, Hex(0xb0bec5), a * Mathf.Clamp01((0.22f - dot) * 12f));
        }
        return c;
    }, FilterMode.Bilinear, new Vector2(0f, 0.5f)));

    // 行き先の矢印（+X 向きの太い矢印。白。色は SpriteRenderer で付ける。小赤鬼・暴れ回りの次の 1 歩）
    public static Sprite BossArrow => bossArrow != null ? bossArrow : (bossArrow = Make(64, 0.5f, (ux, uy) =>
    {
        Vector2 p = new Vector2(ux, uy);
        Vector2[] head = { new Vector2(0.95f, 0f), new Vector2(0.2f, 0.7f), new Vector2(0.2f, -0.7f) };
        float d = PolygonDistance(p, head);
        bool shaft = ux >= -0.9f && ux <= 0.25f && Mathf.Abs(uy) <= 0.28f;
        float a = Mathf.Max(Mathf.Clamp01(-d * 24f), shaft ? 1f : 0f);
        return new Color(1f, 1f, 1f, a);
    }, FilterMode.Bilinear));

    // 鬼の顔（±1 の座標。king なら王冠・太い眉・大きな牙を足す）
    static Color OniFace(float ux, float uy, bool king)
    {
        Vector2 p = new Vector2(ux, uy);
        Color c = new Color(0f, 0f, 0f, 0f);
        float soft = king ? 90f : 34f; // 縁のぼかし（画像の大きさに合わせる）

        Color skin = king ? Hex(0xd32f2f) : Hex(0xe53935);
        Color dark = king ? Hex(0x7f0000) : Hex(0x8e1b1b);
        Color horn = Hex(0xffe082);

        // 角（左右）
        foreach (float sx in new[] { -1f, 1f })
        {
            Vector2[] tri =
            {
                new Vector2(sx * 0.30f, 0.52f), new Vector2(sx * 0.62f, 0.50f),
                new Vector2(sx * (king ? 0.66f : 0.60f), king ? 0.98f : 0.95f),
            };
            float dh = PolygonDistance(p, tri);
            c = Over(c, Hex(0x5d4037), Mathf.Clamp01((0.035f - dh) * soft));
            c = Over(c, horn, Mathf.Clamp01(-dh * soft));
        }

        // 顔（丸。下を少しつぶす）
        Vector2 q = new Vector2(ux, (uy + 0.08f) * 1.08f);
        float r = q.magnitude;
        c = Over(c, dark, Mathf.Clamp01((0.80f - r) * soft));
        c = Over(c, skin, Mathf.Clamp01((0.75f - r) * soft));
        // 頬の影
        float shade = Mathf.Clamp01((r - 0.45f) * 1.6f) * Mathf.Clamp01((0.75f - r) * soft);
        c = Over(c, dark, shade * 0.35f);

        // 王冠（赤鬼王）
        if (king)
        {
            Vector2[] crown =
            {
                new Vector2(-0.34f, 0.50f), new Vector2(-0.38f, 0.86f), new Vector2(-0.18f, 0.66f), new Vector2(0f, 0.92f),
                new Vector2(0.18f, 0.66f), new Vector2(0.38f, 0.86f), new Vector2(0.34f, 0.50f),
            };
            float dc = PolygonDistance(p, crown);
            c = Over(c, Hex(0x6d4c00), Mathf.Clamp01((0.03f - dc) * soft));
            c = Over(c, Hex(0xffc400), Mathf.Clamp01(-dc * soft));
            float gem = (p - new Vector2(0f, 0.62f)).magnitude;
            c = Over(c, Hex(0x40c4ff), Mathf.Clamp01((0.06f - gem) * soft));
        }

        // 眉（つり上がった太い線）と目
        foreach (float sx in new[] { -1f, 1f })
        {
            float brow = SegmentDistance(p, new Vector2(sx * 0.12f, 0.16f), new Vector2(sx * 0.50f, 0.36f));
            c = Over(c, Hex(0x1b0000), Mathf.Clamp01(((king ? 0.07f : 0.06f) - brow) * soft));
            Vector2 eye = new Vector2(sx * 0.30f, 0.05f);
            float de = new Vector2((p.x - eye.x), (p.y - eye.y) * 1.5f).magnitude;
            c = Over(c, Hex(0xfff59d), Mathf.Clamp01((0.15f - de) * soft));
            float pupil = (p - new Vector2(sx * 0.27f, 0.04f)).magnitude;
            c = Over(c, Hex(0x1b0000), Mathf.Clamp01((0.06f - pupil) * soft));
        }

        // 口（横に長い暗い口）と牙
        float mouth = SegmentDistance(p, new Vector2(-0.34f, -0.36f), new Vector2(0.34f, -0.36f));
        c = Over(c, Hex(0x2b0000), Mathf.Clamp01((0.10f - mouth) * soft));
        foreach (float sx in new[] { -1f, 1f })
        {
            float h = king ? 0.26f : 0.20f;
            Vector2[] fang = { new Vector2(sx * 0.30f, -0.40f), new Vector2(sx * 0.16f, -0.40f), new Vector2(sx * 0.23f, -0.40f + h) };
            float df = PolygonDistance(p, fang);
            c = Over(c, Color.white, Mathf.Clamp01(-df * soft));
        }
        return c;
    }
}
