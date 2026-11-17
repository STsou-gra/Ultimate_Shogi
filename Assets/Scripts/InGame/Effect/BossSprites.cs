using UnityEngine;

// 討伐任務のボス・手下の絵（実行時に作る。Prototype の src/view/bossView.js が Pixi の Graphics で描くのと同じく、図形だけで描いたオリジナルの鬼）。
// 画像のアセット・プレハブを持たないので、シーンやプレハブを変えずに使える（MagicSprites の続き）
public static partial class MagicSprites
{
    private static Sprite kooni, akaoni, club, bossArrow;
    private static Sprite daishogun, gunFu, gunJu, bouheki, greatsword, reticle, guardRing;

    // ボスの絵（ボスの Id ごと。知らない Id は赤鬼王）
    public static Sprite BossSprite(string bossId) => bossId == "daishogun" ? Daishogun : Akaoni;
    // ボスの向きの印（赤鬼王は金棒・大将軍は大剣）
    public static Sprite BossWeapon(string bossId) => bossId == "daishogun" ? Greatsword : Club;

    // 大将軍（3×3 マスぶん。兜と甲冑の武将。Prototype の src/view/shogunArt.js と同じく、図形だけで描いたオリジナルの絵）
    public static Sprite Daishogun => daishogun != null ? daishogun : (daishogun = Make(256, 1.5f, ShogunFace, FilterMode.Bilinear));

    // 軍歩兵・軍銃兵（駒 1 マスぶん。陣笠の兵。銃兵は銃を担いでいる）
    public static Sprite GunFu => gunFu != null ? gunFu : (gunFu = Make(96, 0.44f, (ux, uy) => SoldierFace(ux, uy, false), FilterMode.Bilinear));
    public static Sprite GunJu => gunJu != null ? gunJu : (gunJu = Make(96, 0.44f, (ux, uy) => SoldierFace(ux, uy, true), FilterMode.Bilinear));

    // 防壁（駒 1 マスぶん。石積みの壁。残りの HP の数字は PieceSoldier が重ねる）
    public static Sprite Bouheki => bouheki != null ? bouheki : (bouheki = Make(96, 0.46f, (ux, uy) =>
    {
        Vector2 p = new Vector2(ux, uy);
        Color c = new Color(0f, 0f, 0f, 0f);
        float d = BoxDistance(p, Vector2.zero, new Vector2(0.86f, 0.80f));
        c = Over(c, Hex(0x3e2723), Mathf.Clamp01((0.06f - d) * 30f));
        float inside = Mathf.Clamp01(-d * 30f);
        // 石積み（段ごとに半分ずらす）
        float row = Mathf.Floor((uy + 0.8f) / 0.4f);
        float bx = (ux + 0.86f) / 0.58f + (((int)row & 1) == 0 ? 0f : 0.5f);
        float fx = bx - Mathf.Floor(bx), fy = (uy + 0.8f) / 0.4f - row;
        float joint = Mathf.Min(Mathf.Min(fx, 1f - fx) * 0.58f, Mathf.Min(fy, 1f - fy) * 0.4f);
        Color stone = Color.Lerp(Hex(0x8d6e63), Hex(0xa1887f), Mathf.Clamp01(0.5f + 0.5f * Mathf.Sin(row * 2.3f + Mathf.Floor(bx) * 1.7f)));
        c = Over(c, stone, inside);
        c = Over(c, Hex(0x4e342e), inside * Mathf.Clamp01((0.035f - joint) * 40f));
        // 上の縁（狭間）
        c = Over(c, Hex(0xbcaaa4), inside * Mathf.Clamp01((uy - 0.66f) * 30f) * 0.6f);
        return c;
    }, FilterMode.Bilinear));

    // 大剣（+X 向き。基準点は柄の端。長さ 1unit × 太さ 0.5unit。大将軍の向きの印・大剣斬りの演出に使う）
    public static Sprite Greatsword => greatsword != null ? greatsword : (greatsword = Make(128, 0.5f, (ux, uy) =>
    {
        float t = (ux + 1f) * 0.5f; // 0 = 柄の端・1 = 切っ先
        float v = uy * 0.5f;
        Color c = new Color(0f, 0f, 0f, 0f);
        if (t < 0f || t > 1f) return c;
        // 柄
        if (t < 0.2f) c = Over(c, Hex(0x4e342e), Mathf.Clamp01((0.045f - Mathf.Abs(v)) * 60f));
        // 鍔
        c = Over(c, Hex(0xffc400), Mathf.Clamp01((0.03f - Mathf.Abs(t - 0.22f)) * 80f) * Mathf.Clamp01((0.16f - Mathf.Abs(v)) * 60f));
        // 刃（切っ先へ細くなる）
        if (t > 0.24f)
        {
            float half = Mathf.Lerp(0.10f, 0.0f, Mathf.Clamp01((t - 0.82f) / 0.18f));
            float a = Mathf.Clamp01((half - Mathf.Abs(v)) * 60f);
            c = Over(c, Hex(0x263238), Mathf.Clamp01((half + 0.02f - Mathf.Abs(v)) * 60f));
            c = Over(c, Color.Lerp(Hex(0xcfd8dc), Hex(0xeceff1), Mathf.Clamp01(v / Mathf.Max(0.01f, half) * 0.5f + 0.5f)), a);
            c = Over(c, Hex(0x90a4ae), a * Mathf.Clamp01((0.012f - Mathf.Abs(v)) * 90f));
        }
        return c;
    }, FilterMode.Bilinear, new Vector2(0f, 0.5f)));

    // 照準（1 マスぶん。白い輪と十字。色は SpriteRenderer で付ける：追っている間は黄・固まったら赤。弓・軍銃兵）
    public static Sprite Reticle => reticle != null ? reticle : (reticle = Make(96, 0.5f, (ux, uy) =>
    {
        float r = Mathf.Sqrt(ux * ux + uy * uy);
        float ring = Mathf.Clamp01((0.07f - Mathf.Abs(r - 0.72f)) * 28f);
        float inner = Mathf.Clamp01((0.045f - Mathf.Abs(r - 0.34f)) * 28f) * 0.8f;
        float cross = (Mathf.Abs(ux) < 0.06f || Mathf.Abs(uy) < 0.06f) && r > 0.46f && r < 0.98f ? 1f : 0f;
        float dot = Mathf.Clamp01((0.09f - r) * 28f);
        return new Color(1f, 1f, 1f, Mathf.Max(Mathf.Max(ring, inner), Mathf.Max(cross, dot)));
    }, FilterMode.Bilinear));

    // 守りの輪（3×3 マスぶん。白い二重の輪。色は SpriteRenderer で付ける。大将軍の「守備」のバリア）
    public static Sprite GuardRing => guardRing != null ? guardRing : (guardRing = Make(192, 1.7f, (ux, uy) =>
    {
        float r = Mathf.Sqrt(ux * ux + uy * uy);
        float outer = Mathf.Clamp01((0.045f - Mathf.Abs(r - 0.92f)) * 40f);
        float fill = Mathf.Clamp01((0.92f - r) * 40f) * (0.10f + 0.22f * Mathf.Clamp01((r - 0.5f) * 2f));
        // 六角の模様
        float ang = Mathf.Atan2(uy, ux);
        float hex = Mathf.Clamp01((0.03f - Mathf.Abs(Mathf.Sin(ang * 6f)) * r * 0.25f) * 30f) * Mathf.Clamp01((0.92f - r) * 20f) * Mathf.Clamp01((r - 0.55f) * 6f) * 0.5f;
        return new Color(1f, 1f, 1f, Mathf.Max(outer, Mathf.Max(fill, hex)));
    }, FilterMode.Bilinear));

    // 中心 c・半分の大きさ half の長方形までの距離（内側は負）
    static float BoxDistance(Vector2 p, Vector2 c, Vector2 half)
    {
        float dx = Mathf.Abs(p.x - c.x) - half.x, dy = Mathf.Abs(p.y - c.y) - half.y;
        float outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        return outside + Mathf.Min(Mathf.Max(dx, dy), 0f);
    }

    // 大将軍の絵（±1 の座標。兜・前立て・面頬・肩の甲冑）
    static Color ShogunFace(float ux, float uy)
    {
        Vector2 p = new Vector2(ux, uy);
        Color c = new Color(0f, 0f, 0f, 0f);
        const float soft = 90f;
        Color armor = Hex(0x283593), armorDark = Hex(0x1a237e), gold = Hex(0xffc400), edge = Hex(0x0d1335);

        // 肩の甲冑（左右の大袖）と胴
        foreach (float sx in new[] { -1f, 1f })
        {
            float ds = BoxDistance(p, new Vector2(sx * 0.62f, -0.50f), new Vector2(0.26f, 0.30f));
            c = Over(c, edge, Mathf.Clamp01((0.035f - ds) * soft));
            c = Over(c, armor, Mathf.Clamp01(-ds * soft));
            // 小札（横の筋）
            float band = Mathf.Abs(Mathf.Sin((uy + 0.5f) * 22f));
            c = Over(c, gold, Mathf.Clamp01(-ds * soft) * Mathf.Clamp01((0.18f - band) * 6f) * 0.7f);
        }
        float body = BoxDistance(p, new Vector2(0f, -0.62f), new Vector2(0.40f, 0.26f));
        c = Over(c, edge, Mathf.Clamp01((0.035f - body) * soft));
        c = Over(c, armorDark, Mathf.Clamp01(-body * soft));
        c = Over(c, Hex(0xc62828), Mathf.Clamp01(-body * soft) * Mathf.Clamp01((0.05f - Mathf.Abs(ux)) * soft)); // 胴の紐

        // 兜のしころ（左右に広がる）
        Vector2[] shikoro = { new Vector2(-0.70f, -0.02f), new Vector2(-0.52f, 0.46f), new Vector2(0.52f, 0.46f), new Vector2(0.70f, -0.02f), new Vector2(0.44f, -0.16f), new Vector2(-0.44f, -0.16f) };
        float dk = PolygonDistance(p, shikoro);
        c = Over(c, edge, Mathf.Clamp01((0.035f - dk) * soft));
        c = Over(c, armor, Mathf.Clamp01(-dk * soft));

        // 顔（面頬の上に目だけ見える）
        Vector2 q = new Vector2(ux, (uy - 0.04f) * 1.1f);
        float r = q.magnitude;
        c = Over(c, edge, Mathf.Clamp01((0.44f - r) * soft));
        c = Over(c, Hex(0xd7a98c), Mathf.Clamp01((0.41f - r) * soft));
        // 面頬（顔の下半分）
        float mask = Mathf.Clamp01((0.41f - r) * soft) * Mathf.Clamp01((-0.02f - uy) * soft);
        c = Over(c, Hex(0x8e0000), mask);
        float teeth = SegmentDistance(p, new Vector2(-0.16f, -0.18f), new Vector2(0.16f, -0.18f));
        c = Over(c, Hex(0x1b0000), mask * Mathf.Clamp01((0.045f - teeth) * soft));
        // ひげ
        foreach (float sx in new[] { -1f, 1f })
        {
            float m = SegmentDistance(p, new Vector2(sx * 0.04f, -0.08f), new Vector2(sx * 0.30f, -0.14f));
            c = Over(c, Color.white, Mathf.Clamp01((0.035f - m) * soft) * 0.9f);
        }
        // 眉と目
        foreach (float sx in new[] { -1f, 1f })
        {
            float brow = SegmentDistance(p, new Vector2(sx * 0.08f, 0.14f), new Vector2(sx * 0.30f, 0.22f));
            c = Over(c, Hex(0x1b1b1b), Mathf.Clamp01((0.04f - brow) * soft));
            float eye = new Vector2(p.x - sx * 0.19f, (p.y - 0.07f) * 1.8f).magnitude;
            c = Over(c, Color.white, Mathf.Clamp01((0.08f - eye) * soft));
            float pupil = (p - new Vector2(sx * 0.18f, 0.06f)).magnitude;
            c = Over(c, Hex(0x1b1b1b), Mathf.Clamp01((0.035f - pupil) * soft));
        }

        // 兜の鉢（上半分の丸）
        Vector2 hq = new Vector2(ux, (uy - 0.30f) * 1.25f);
        float hr = hq.magnitude;
        float bowl = Mathf.Clamp01((0.50f - hr) * soft) * Mathf.Clamp01((uy - 0.26f) * soft);
        c = Over(c, edge, Mathf.Clamp01((0.535f - hr) * soft) * Mathf.Clamp01((uy - 0.235f) * soft));
        c = Over(c, armorDark, bowl);
        c = Over(c, gold, bowl * Mathf.Clamp01((0.03f - Mathf.Abs(uy - 0.30f)) * soft)); // 眉庇の縁
        // 前立て（金の三日月）
        float moonOuter = (p - new Vector2(0f, 0.78f)).magnitude;
        float moonInner = (p - new Vector2(0f, 0.90f)).magnitude;
        float moon = Mathf.Clamp01((0.36f - moonOuter) * soft) * Mathf.Clamp01((moonInner - 0.34f) * soft);
        c = Over(c, Hex(0x6d4c00), Mathf.Clamp01((0.39f - moonOuter) * soft) * Mathf.Clamp01((moonInner - 0.31f) * soft));
        c = Over(c, gold, moon);
        return c;
    }

    // 兵の絵（±1 の座標。陣笠をかぶった兵。gunner なら銃を担ぐ）
    static Color SoldierFace(float ux, float uy, bool gunner)
    {
        Vector2 p = new Vector2(ux, uy);
        Color c = new Color(0f, 0f, 0f, 0f);
        const float soft = 34f;
        Color cloth = gunner ? Hex(0x455a64) : Hex(0x3949ab), edge = Hex(0x10162f);

        // 胴
        float body = BoxDistance(p, new Vector2(0f, -0.52f), new Vector2(0.46f, 0.28f));
        c = Over(c, edge, Mathf.Clamp01((0.06f - body) * soft));
        c = Over(c, cloth, Mathf.Clamp01(-body * soft));
        c = Over(c, Hex(0xffc400), Mathf.Clamp01(-body * soft) * Mathf.Clamp01((0.05f - Mathf.Abs(uy + 0.36f)) * soft) * 0.8f);

        // 銃（左下から右上へ担ぐ）
        if (gunner)
        {
            float gun = SegmentDistance(p, new Vector2(-0.62f, -0.70f), new Vector2(0.70f, 0.50f));
            c = Over(c, Hex(0x1b1b1b), Mathf.Clamp01((0.085f - gun) * soft));
            c = Over(c, Hex(0x8d6e63), Mathf.Clamp01((0.05f - gun) * soft) * Mathf.Clamp01((-0.1f - ux) * soft));
            c = Over(c, Hex(0x90a4ae), Mathf.Clamp01((0.04f - gun) * soft) * Mathf.Clamp01((ux + 0.1f) * soft));
        }
        else
        {
            // 槍（右に立てる）
            float spear = SegmentDistance(p, new Vector2(0.66f, -0.80f), new Vector2(0.66f, 0.62f));
            c = Over(c, Hex(0x5d4037), Mathf.Clamp01((0.05f - spear) * soft));
            Vector2[] tip = { new Vector2(0.56f, 0.56f), new Vector2(0.76f, 0.56f), new Vector2(0.66f, 0.94f) };
            c = Over(c, Hex(0xcfd8dc), Mathf.Clamp01(-PolygonDistance(p, tip) * soft));
        }

        // 顔
        float r = new Vector2(ux, (uy - 0.02f) * 1.05f).magnitude;
        c = Over(c, edge, Mathf.Clamp01((0.40f - r) * soft));
        c = Over(c, Hex(0xe0b090), Mathf.Clamp01((0.35f - r) * soft));
        foreach (float sx in new[] { -1f, 1f })
        {
            float eye = SegmentDistance(p, new Vector2(sx * 0.10f, 0.02f), new Vector2(sx * 0.22f, 0.06f));
            c = Over(c, Hex(0x1b1b1b), Mathf.Clamp01((0.045f - eye) * soft));
        }
        float mouth = SegmentDistance(p, new Vector2(-0.10f, -0.18f), new Vector2(0.10f, -0.18f));
        c = Over(c, Hex(0x4e342e), Mathf.Clamp01((0.035f - mouth) * soft));

        // 陣笠（平たい三角）
        Vector2[] hat = { new Vector2(-0.66f, 0.16f), new Vector2(0.66f, 0.16f), new Vector2(0f, 0.66f) };
        float dh = PolygonDistance(p, hat);
        c = Over(c, edge, Mathf.Clamp01((0.06f - dh) * soft));
        c = Over(c, gunner ? Hex(0x263238) : Hex(0x1a237e), Mathf.Clamp01(-dh * soft));
        c = Over(c, gunner ? Hex(0xe53935) : Hex(0xffc400), Mathf.Clamp01(-dh * soft) * Mathf.Clamp01((0.045f - Mathf.Abs(uy - 0.24f)) * soft));
        return c;
    }

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
