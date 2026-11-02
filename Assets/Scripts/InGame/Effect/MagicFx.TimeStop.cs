using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 超常魔法「停止世界」（時間停止）の演出（Prototype の view/fx.js の clock・cracks・shatter、view/events.js の timeStopStarted・timeStopResolving）。
// MagicFx の続き（partial）
//
//  発動 … 盤の中央に時計が現れ、針が猛スピードで回って止まる（揺れ・火花）→ 止まった時計から球状の衝撃波が広がり、閃光。
//         それに合わせて世界の色が反転していく（反転は TimeStopView。GameSystem.TimeStopStarted のとき）→「時よ止まれ」のカットイン（GameSystem）
//  解除 … 反転した画面に白いひびが走る（放射状と同心円状）→ ガラスが砕け散るように反転が戻る（閃光・破片・大きな揺れ）→「時は動き出す」
//
// 演出の乱数（ひびの形・破片の飛び方）は UnityEngine.Random（対局の結果に関わる乱数は GameRng）
public partial class MagicFx
{
    public static readonly Color TimeStopFxColor = Hex(0xffd54a);
    private static readonly Color[] GlassColors = { Color.white, Hex(0xe1f5fe), Hex(0xb3e5fc) };

    // 時計の針が回り始めてから止まるまで（TimeStopEffect はこのあと時間停止を始める＝色が反転し始める）
    public const float TimeStopClockTime = 0.75f;
    // 解除：ひびが走り始めてから、ガラスが砕けて色の反転が戻るまで（TimeStopView はこの時刻に反転をやめる）
    public const float TimeStopBreakTime = 0.57f;
    // 解除：ひびが走り始めてから、予定の処理（取り・爆発など）の演出を始めるまで
    public const float TimeStopResumeTime = TimeStopBreakTime + 0.5f;

    private const int TimeStopCrackOrder = 445; // 色の反転（TimeStopView の 430）より手前に、白いひびを描く

    // ------------------------------------------------------------
    //  発動：時計
    // ------------------------------------------------------------

    // 盤の中央に時計（Prototype の fx.clock。R = 60px・scale = 1.4）。time 秒後に始め、TimeStopClockTime 後に針が止まる
    public void TimeStopClock(float time)
    {
        At(time, () => PlayClock(CellWorld(4, 4), TimeStopFxColor, 1.4f));
    }

    void PlayClock(Vector3 pos, Color color, float scale)
    {
        const float R = 60f; // px
        Transform root = new GameObject("TimeStopClock").transform;
        root.position = new Vector3(pos.x, pos.y, pos.z - 0.3f);
        root.rotation = Quaternion.Euler(0f, 0f, cam != null ? cam.transform.eulerAngles.z : 0f); // 画面で上が 12 時
        root.localScale = Vector3.one * (0.3f * scale);

        int order = fxOrder + 5;
        List<SpriteRenderer> parts = new List<SpriteRenderer>();
        List<float> alphas = new List<float>();
        System.Action<SpriteRenderer, float> add = (sr, a) => { parts.Add(sr); alphas.Add(a); };

        // 文字盤：太くうっすらした輪・くっきりした輪・内側の白い細い輪
        add(CreateChild(root, RingSprite(R, 16f), WithAlpha(color, 0.25f), order), 0.25f);
        add(CreateChild(root, RingSprite(R, 4f), color, order + 1), 1f);
        add(CreateChild(root, RingSprite(R * 0.86f, 1.5f), WithAlpha(Color.white, 0.8f), order + 1), 0.8f);
        // 目盛り（12 本。3 時間ごとに太く長い）
        for (int i = 0; i < 12; i++)
        {
            float a = i / 12f * Mathf.PI * 2f;
            float r1 = R * (i % 3 != 0 ? 0.74f : 0.64f), r2 = R * 0.84f;
            SpriteRenderer tick = CreateChild(root, MagicSprites.Square, WithAlpha(Color.white, 0.9f), order + 1);
            tick.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * ((r1 + r2) * 0.5f * PX);
            tick.transform.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg);
            tick.transform.localScale = new Vector3((r2 - r1) * PX, (i % 3 != 0 ? 2f : 4f) * PX, 1f);
            add(tick, 0.9f);
        }
        // 針（根元を中心に回す）：短針は白、長針は色
        Transform hour = Hand(root, Color.white, 6f, -6f, R * 0.45f, order + 2, add);
        Transform minute = Hand(root, color, 4f, -10f, R * 0.78f, order + 3, add);
        SpriteRenderer pin = CreateChild(root, MagicSprites.Glow, Color.white, order + 4);
        pin.transform.localScale = Vector3.one * (20f / 64f); // 光の粒の画像は幅 64px
        add(pin, 1f);

        float all = 0f;
        System.Action apply = () =>
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null) parts[i].color = WithAlpha(parts[i].color, alphas[i] * all);
            }
        };
        apply();

        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Insert(0f, DOVirtual.Float(0f, 1f, 0.15f, a => { all = a; apply(); }));
        seq.Insert(0f, root.DOScale(1.7f * scale, 0.4f).SetEase(Ease.OutBack, 2f));
        // 針が猛スピードで回る（画面で時計回り）→ 止まる
        seq.Insert(0f, minute.DOLocalRotate(new Vector3(0f, 0f, -360f * 3.2f), TimeStopClockTime, RotateMode.FastBeyond360).SetEase(Ease.InQuad));
        seq.Insert(0f, hour.DOLocalRotate(new Vector3(0f, 0f, -360f * 0.55f), TimeStopClockTime, RotateMode.FastBeyond360).SetEase(Ease.InQuad));
        seq.InsertCallback(TimeStopClockTime, () =>
        {
            fx.Shake(8f, 0.2f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 30; burst.colors = new[] { color, Color.white };
            burst.speed = 320f * PX; burst.life = 0.5f;
            fx.Burst(pos, burst);
        });
        seq.Insert(TimeStopClockTime, root.DOScale(1.85f * scale, 0.001f));
        seq.Insert(TimeStopClockTime + 0.001f, root.DOScale(1.7f * scale, 0.2f));
        seq.Insert(1.05f, DOVirtual.Float(1f, 0f, 0.45f, a => { all = a; apply(); }));
        seq.Insert(1.05f, root.DOScale(2.6f * scale, 0.45f).SetEase(Ease.InQuad));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // 時計の針：根元（中心）から from〜to px（上向き）の、太さ width px の棒
    Transform Hand(Transform root, Color color, float width, float from, float to, int order, System.Action<SpriteRenderer, float> add)
    {
        Transform pivot = new GameObject("Hand").transform;
        pivot.SetParent(root, false);
        SpriteRenderer sr = CreateChild(pivot, MagicSprites.Square, color, order);
        sr.transform.localPosition = new Vector3(0f, (from + to) * 0.5f * PX, 0f);
        sr.transform.localScale = new Vector3(width * PX, (to - from) * PX, 1f);
        add(sr, 1f);
        return pivot;
    }

    // 時計が止まった：球状の衝撃波・閃光・揺れ（このとき色の反転が始まる）
    public void TimeStopFreeze(float time)
    {
        At(time, () =>
        {
            Vector3 c = CellWorld(4, 4);
            fx.Shockwave(c, Color.white, 14f, 0.8f);
            fx.Shockwave(c, TimeStopFxColor, 10f, 0.6f);
            if (newPieceFx != null) newPieceFx.Flash(Color.white, 0.5f, 0.3f);
            fx.Shake(10f, 0.4f);
        });
    }

    // ------------------------------------------------------------
    //  解除：ガラスが割れる
    // ------------------------------------------------------------

    // ひびが走る → TimeStopBreakTime に砕け散る（閃光・破片・揺れ・「時は動き出す」）
    public void TimeStopShatter(float time)
    {
        At(time, PlayShatter);
    }

    void PlayShatter()
    {
        Camera c = cam != null ? cam : Camera.main;
        float halfH = c != null && c.orthographic ? c.orthographicSize : 5f;
        float halfW = halfH * (c != null ? c.aspect : 16f / 9f);
        Vector3 center = CellWorld(4, 4);

        // ひび（Prototype の fx.cracks：放射状の折れ線 14 本と、それをつなぐ同心円状のひび）
        int texW = Mathf.Min(1024, Mathf.CeilToInt((halfW * 2f + 2f) / PX / CrackCanvas.PxPerTexel));
        int texH = Mathf.Min(1024, Mathf.CeilToInt((halfH * 2f + 2f) / PX / CrackCanvas.PxPerTexel));
        CrackCanvas cracks = new CrackCanvas(texW, texH, new[] { Hex(0xe1f5fe), Color.white, Color.white });
        float L = Mathf.Sqrt(halfW * halfW + halfH * halfH) / PX;
        const int n = 14;
        List<List<Vector2>> rays = new List<List<Vector2>>();
        for (int i = 0; i < n; i++)
        {
            float a0 = (float)i / n * Mathf.PI * 2f + Random.Range(-0.15f, 0.15f);
            List<Vector2> pts = new List<Vector2> { Vector2.zero };
            for (float r = Random.Range(10f, 30f); r < L; r += Random.Range(30f, 90f))
            {
                float a = a0 + Random.Range(-0.1f, 0.1f);
                pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
            rays.Add(pts);
            cracks.AddPolyline(pts, 2.5f, 0f);
        }
        foreach (float R0 in new[] { 70f, 165f, 300f })
        {
            float R = R0 * Random.Range(0.85f, 1.15f);
            List<Vector2> near = new List<Vector2>();
            foreach (List<Vector2> pts in rays)
            {
                Vector2 best = pts[pts.Count - 1];
                foreach (Vector2 p in pts)
                {
                    if (Mathf.Abs(p.magnitude - R) < Mathf.Abs(best.magnitude - R)) best = p;
                }
                near.Add(best);
            }
            for (int i = 0; i < n; i++)
            {
                if (Random.value >= 0.75f) continue; // ところどころ途切れさせる
                cracks.AddPolyline(new List<Vector2> { near[i], near[(i + 1) % n] }, 2f, R);
            }
        }

        Transform root = new GameObject("TimeStopCracks").transform;
        root.position = new Vector3(center.x, center.y, center.z - 0.45f);
        root.rotation = Quaternion.Euler(0f, 0f, c != null ? c.transform.eulerAngles.z : 0f);
        SpriteRenderer crackSr = CreateChild(root, cracks.Sprite, Color.white, TimeStopCrackOrder);

        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Insert(0f, DOVirtual.Float(0f, L, 0.22f, r => cracks.Reveal(r)).SetEase(Ease.OutQuad));
        seq.InsertCallback(0.22f, () => fx.Shake(5f, 0.2f));
        seq.InsertCallback(TimeStopBreakTime, () =>
        {
            if (crackSr != null) crackSr.enabled = false;
            if (newPieceFx != null) newPieceFx.Flash(Color.white, 0.85f, 0.5f);
            // ガラスが砕け散る（Prototype の fx.shatter：中心の周り一帯から大きな破片）
            for (int i = 0; i < 12; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(0f, 460f);
                Vector3 at = center + (ScreenRight * (Mathf.Cos(a) * r) + ScreenUp * (Mathf.Sin(a) * r * 0.75f)) * PX;
                Shards(at, 7, GlassColors, 0f, 170f * PX);
            }
            fx.Shake(16f, 0.5f);
            PopText("時は動き出す", center + ScreenUp * (90f * PX), 64f, Color.white, TimeStopFxColor, Hex(0x1a1200), 30f, 1.1f);
        });
        seq.OnKill(() =>
        {
            cracks.Dispose();
            if (root != null) Destroy(root.gameObject);
        });
    }

    // ------------------------------------------------------------
    //  画像
    // ------------------------------------------------------------
    private static readonly Dictionary<int, Sprite> ringCache = new Dictionary<int, Sprite>();

    // 半径 radiusPx・太さ widthPx の白い輪（Prototype の lineStyle(width).drawCircle(R) と同じ大きさ）
    static Sprite RingSprite(float radiusPx, float widthPx)
    {
        int key = Mathf.RoundToInt(radiusPx * 10f) * 1000 + Mathf.RoundToInt(widthPx * 10f);
        if (ringCache.TryGetValue(key, out Sprite cached) && cached != null) return cached;

        float half = radiusPx + widthPx * 0.5f + 2f; // px
        int size = Mathf.Clamp(Mathf.CeilToInt(half * 2f * 2f), 16, 512); // 1px = 2 テクセル
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color[] pixels = new Color[size * size];
        float texelPx = half * 2f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) * texelPx - half, (y + 0.5f) * texelPx - half);
                float d = Mathf.Abs(p.magnitude - radiusPx);
                float a = Mathf.Clamp01((widthPx * 0.5f - d) / texelPx + 0.5f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size / (half * 2f * PX));
        ringCache[key] = sprite;
        return sprite;
    }
}
