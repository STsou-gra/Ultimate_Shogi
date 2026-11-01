using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 盤面のエフェクト（衝撃波リング・パーティクル・画面シェイク）
// プロトタイプ(index.html)の FX クラスを移植したもの。
// 数値はプロトタイプの px を「1マス = 66px = Unity の 1」として換算している。
public class PieceFx : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private Material fxMaterial;             // 未設定ならスプライト標準マテリアル。加算合成にしたい場合は Additive のマテリアルを設定
    [SerializeField] private int fxSortingOrder = 200;        // 駒(10)・移動中の駒(110)より手前に描く
    [SerializeField] private float maxDeltaTime = 0.05f;      // フレーム落ち時の1フレームの上限

    [Header("Particle")]
    [SerializeField] private float particleBaseSize = 0.97f;  // size=1 のときの直径（64px / 66px）
    [SerializeField] private int glowTextureSize = 64;

    [Header("Shockwave")]
    [SerializeField] private float ringRadius = 0.30f;        // scale=1 のときの半径（20px）
    [SerializeField] private float ringLineWidth = 0.09f;     // 線の太さ（6px）
    [SerializeField] private float ringStartScale = 0.3f;
    [SerializeField] private Ease ringScaleEase = Ease.OutCubic; // GSAP power2.out
    [SerializeField] private Ease ringFadeEase = Ease.InQuad;    // GSAP power1.in
    [SerializeField] private int ringTextureSize = 128;

    [Header("Shake")]
    [SerializeField] private Transform shakeTarget;           // 未設定なら Camera.main
    [SerializeField] private float shakeUnitsPerPower = 1f / 66f; // power 1 あたりの揺れ幅（1px）

    public struct ParticleOptions
    {
        public Color color;
        public Vector2 velocity;  // ワールド単位/秒
        public float life;
        public float size;
        public float gravity;     // 画面下方向への加速度（ワールド単位/秒^2）
        public float drag;        // 1/60秒あたりの速度の減衰率
        public float alpha;
        public bool shrink;
        public float grow;

        public static ParticleOptions Default => new ParticleOptions
        {
            color = Color.white, life = 0.8f, size = 0.3f, drag = 0.95f, alpha = 1f, shrink = true,
        };
    }

    public struct BurstOptions
    {
        public int count;
        public Color[] colors;
        public float speed;       // ワールド単位/秒
        public float life;
        public float size;
        public float gravity;
        public float drag;
        public float alpha;
        public bool shrink;
        public float grow;

        public static BurstOptions Default => new BurstOptions
        {
            count = 20, colors = new[] { Color.white }, speed = 3f, life = 0.7f, size = 0.25f, drag = 0.93f, alpha = 1f, shrink = true,
        };
    }

    private class LiveParticle
    {
        public SpriteRenderer sprite;
        public Vector2 velocity;
        public Vector2 gravity;
        public float life, maxLife, drag, alpha, scale, grow;
        public bool shrink;
        public Color color;
    }

    private readonly List<LiveParticle> particles = new List<LiveParticle>();
    private readonly Stack<SpriteRenderer> particlePool = new Stack<SpriteRenderer>();
    private Sprite glowSprite;
    private Sprite ringSprite;

    private Tween shakeTween;
    private float shakePower;
    private Vector3 shakeBasePosition;

    void Awake()
    {
        glowSprite = CreateGlowSprite(glowTextureSize);
        ringSprite = CreateRingSprite(ringTextureSize);
    }

    void Update()
    {
        float dt = Mathf.Min(maxDeltaTime, Time.deltaTime);
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            LiveParticle p = particles[i];
            p.life -= dt;
            if (p.life <= 0)
            {
                ReleaseParticle(p.sprite);
                particles.RemoveAt(i);
                continue;
            }
            float drag = Mathf.Pow(p.drag, dt * 60f);
            p.velocity = p.velocity * drag + p.gravity * dt;
            p.sprite.transform.position += (Vector3)(p.velocity * dt);

            float k = p.life / p.maxLife;
            Color c = p.color;
            c.a = p.alpha * Mathf.Min(1f, k * 1.5f);
            p.sprite.color = c;
            float s = p.grow > 0 ? 1f + (1f - k) * p.grow : (p.shrink ? 0.3f + 0.7f * k : 1f);
            p.sprite.transform.localScale = Vector3.one * (p.scale * s * particleBaseSize);
        }
    }

    void OnDisable()
    {
        shakeTween?.Kill();
    }

    /// <summary>画面の上方向（カメラが180°回転していても見た目の「上」）</summary>
    public Vector2 ScreenUp
    {
        get
        {
            Camera cam = Camera.main;
            return cam != null ? (Vector2)cam.transform.up : Vector2.up;
        }
    }

    /// <summary>パーティクルを1つ生成</summary>
    public void Particle(Vector3 position, ParticleOptions o)
    {
        SpriteRenderer sr = GetParticle();
        sr.transform.position = position;
        sr.transform.localScale = Vector3.one * (o.size * particleBaseSize);
        Color c = o.color;
        c.a = o.alpha;
        sr.color = c;
        particles.Add(new LiveParticle
        {
            sprite = sr, velocity = o.velocity, gravity = -ScreenUp * o.gravity,
            life = o.life, maxLife = o.life, drag = o.drag, alpha = o.alpha, scale = o.size,
            shrink = o.shrink, grow = o.grow, color = o.color,
        });
    }

    /// <summary>放射状に飛び散るパーティクル</summary>
    public void Burst(Vector3 position, BurstOptions o)
    {
        for (int i = 0; i < o.count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float sp = o.speed * Random.Range(0.25f, 1f);
            Particle(position, new ParticleOptions
            {
                velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp,
                color = o.colors[Random.Range(0, o.colors.Length)],
                life = o.life * Random.Range(0.6f, 1.2f),
                size = o.size * Random.Range(0.6f, 1.3f),
                gravity = o.gravity, drag = o.drag, alpha = o.alpha, shrink = o.shrink, grow = o.grow,
            });
        }
    }

    /// <summary>円形の衝撃波リング</summary>
    public void Shockwave(Vector3 position, Color color, float maxScale = 3f, float duration = 0.5f)
    {
        GameObject obj = new GameObject("Shockwave");
        obj.transform.SetParent(transform, false);
        obj.transform.position = position;
        obj.transform.localScale = Vector3.one * ringStartScale;
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = ringSprite;
        if (fxMaterial != null) sr.sharedMaterial = fxMaterial;
        sr.sortingOrder = fxSortingOrder;
        sr.color = color;

        DOTween.Sequence()
            .Join(obj.transform.DOScale(maxScale, duration).SetEase(ringScaleEase))
            .Join(sr.DOFade(0f, duration).SetEase(ringFadeEase))
            .OnComplete(() => Destroy(obj))
            .SetLink(obj);
    }

    /// <summary>画面シェイク（減衰付きのランダム揺れ）。揺れ中に呼ぶと強い方を引き継ぐ</summary>
    public void Shake(float power, float duration)
    {
        Transform target = shakeTarget != null ? shakeTarget : (Camera.main != null ? Camera.main.transform : null);
        if (target == null) return;

        float remain = 0f;
        if (shakeTween != null && shakeTween.IsActive())
        {
            remain = shakePower * (1f - shakeTween.ElapsedPercentage());
            target.localPosition = shakeBasePosition;
            shakeTween.Kill();
        }
        shakeBasePosition = target.localPosition;
        Vector3 basePosition = shakeBasePosition;
        shakePower = Mathf.Max(power, remain);

        shakeTween = DOVirtual.Float(1f, 0f, Mathf.Max(duration, 0.01f), k =>
            {
                float pw = shakePower * k * shakeUnitsPerPower;
                target.localPosition = basePosition + new Vector3(Random.Range(-pw, pw), Random.Range(-pw, pw), 0f);
            })
            .SetEase(Ease.Linear)
            .OnKill(() => { if (target != null) target.localPosition = basePosition; })
            .SetLink(gameObject);
    }

    public static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }

    private SpriteRenderer GetParticle()
    {
        SpriteRenderer sr = particlePool.Count > 0 ? particlePool.Pop() : null;
        if (sr == null)
        {
            GameObject obj = new GameObject("Particle");
            obj.transform.SetParent(transform, false);
            sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = glowSprite;
            if (fxMaterial != null) sr.sharedMaterial = fxMaterial;
            sr.sortingOrder = fxSortingOrder;
        }
        sr.gameObject.SetActive(true);
        return sr;
    }

    private void ReleaseParticle(SpriteRenderer sr)
    {
        if (sr == null) return;
        sr.gameObject.SetActive(false);
        particlePool.Push(sr);
    }

    // 中心から外側へ 1 → 0.85(25%) → 0 と薄くなる円形のぼかし
    private Sprite CreateGlowSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float a = d < 0.25f ? Mathf.Lerp(1f, 0.85f, d / 0.25f) : Mathf.Lerp(0.85f, 0f, (d - 0.25f) / 0.75f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(a)));
            }
        }
        tex.Apply();
        // テクスチャ全体の幅がワールドで 1 になるようにする（大きさは particleBaseSize で調整）
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // 半径 ringRadius・太さ ringLineWidth の輪（アンチエイリアス付き）
    private Sprite CreateRingSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float outer = ringRadius + ringLineWidth * 0.5f;
        float pxPerUnit = size / (outer * 2f);
        float half = size * 0.5f;
        float innerPx = (ringRadius - ringLineWidth * 0.5f) * pxPerUnit;
        float outerPx = half - 1f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                float a = Mathf.Clamp01(d - innerPx + 0.5f) * Mathf.Clamp01(outerPx - d + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pxPerUnit);
    }
}
