using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using DG.Tweening;

/// <summary>
/// 必殺技カットイン（斜めの帯＋集中線＋技名）。
/// プロトタイプ(index.html)の FX.cutIn を DOTween Sequence で再現したもの。
/// 使い方: await CutInEffect.Instance.PlayAsync("技名", "サブタイトル", color);
/// シーンに置かなくても Instance 参照時に自動生成される。UI は実行時に組み立てるので Prefab 不要。
/// </summary>
public class CutInEffect : MonoBehaviour
{
    // プロトタイプの論理解像度（ピクセル値をそのまま使うための基準）
    private const float W = 1280f;
    private const float H = 800f;

    [Header("References")]
    [SerializeField] private TMP_FontAsset font; // 未設定なら NotoSansJP を Resources から読む
    [SerializeField] private Camera shakeCamera;  // 未設定なら Camera.main をシェイクする
    [SerializeField] private int sortingOrder = 500;

    private static CutInEffect instance;
    public static CutInEffect Instance
    {
        get
        {
            if (instance == null)
            {
                instance = FindAnyObjectByType<CutInEffect>();
                if (instance == null) instance = new GameObject("CutInEffect", typeof(RectTransform)).AddComponent<CutInEffect>();
            }
            return instance;
        }
    }

    private Canvas canvas;
    private RectTransform canvasRect;
    private RectTransform particleLayer;
    private Sprite glowSprite;

    // パーティクル（プロトタイプの FX.particle と同じ挙動）
    private class Particle
    {
        public RectTransform rect;
        public Image image;
        public Vector2 velocity;
        public float life, maxLife, drag, scale;
    }
    private readonly List<Particle> particles = new List<Particle>();

    // 画面シェイク
    private Tween shakeTween;
    private RectTransform shakeRect;
    private Vector2 shakeRectOffset;
    private Transform shakeCamTransform;
    private Vector3 shakeCamOffset;

    void Awake()
    {
        if (instance == null) instance = this;
        else if (instance != this) { Destroy(gameObject); return; }

        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        BuildCanvas();
    }

    void OnDestroy()
    {
        if (instance == this) instance = null;
        StopShake();
        if (glowSprite != null)
        {
            Destroy(glowSprite.texture);
            Destroy(glowSprite);
        }
    }

    void Update()
    {
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        for (int i = particles.Count - 1; i >= 0; i--)
        {
            Particle p = particles[i];
            p.life -= dt;
            if (p.life <= 0f)
            {
                Destroy(p.rect.gameObject);
                particles.RemoveAt(i);
                continue;
            }
            p.velocity *= Mathf.Pow(p.drag, dt * 60f);
            p.rect.anchoredPosition += p.velocity * dt;
            float k = p.life / p.maxLife;
            Color c = p.image.color;
            c.a = Mathf.Min(1f, k * 1.5f);
            p.image.color = c;
            p.rect.localScale = Vector3.one * (p.scale * (0.3f + 0.7f * k));
        }
    }

    /// <summary>カットインを再生し、演出が終わるまで待つ（約1.4秒）</summary>
    public async Awaitable PlayAsync(string skillName, string sub, Color color, CancellationToken cancellationToken = default)
    {
        var root = CreateRect("CutIn", canvasRect);
        Stretch(root, 300f);
        var materials = new List<Material>();

        // 暗転
        var dim = CreateImage("Dim", root, new Color(0f, 0f, 0f, 0f));
        Stretch(dim.rectTransform, 300f);

        // 斜めの帯（左の画面外からスタート。UI は y 上向きなので回転の符号はプロトタイプと逆）
        var band = CreateRect("Band", root);
        band.anchoredPosition = new Vector2(-W - W / 2f, 0f);
        band.localRotation = Quaternion.Euler(0f, 0f, 0.12f * Mathf.Rad2Deg);
        CreateImage("Back", band, new Color(0f, 0f, 0f, 0.85f)).rectTransform.sizeDelta = new Vector2(W * 3f, 200f);
        CreateImage("Tint", band, new Color(color.r, color.g, color.b, 0.55f)).rectTransform.sizeDelta = new Vector2(W * 3f, 200f);
        var edgeTop = CreateImage("EdgeTop", band, new Color(1f, 1f, 1f, 0.95f)).rectTransform;
        edgeTop.sizeDelta = new Vector2(W * 3f, 6f);
        edgeTop.anchoredPosition = new Vector2(0f, 105f);
        var edgeBottom = CreateImage("EdgeBottom", band, new Color(1f, 1f, 1f, 0.95f)).rectTransform;
        edgeBottom.sizeDelta = new Vector2(W * 3f, 6f);
        edgeBottom.anchoredPosition = new Vector2(0f, -105f);

        // 流れる集中線
        var lines = CreateRect("SpeedLines", band);
        for (int i = 0; i < 60; i++)
        {
            var line = CreateImage("Line", lines, new Color(1f, 1f, 1f, Random.Range(0.15f, 0.6f))).rectTransform;
            line.pivot = new Vector2(0f, 0.5f);
            line.sizeDelta = new Vector2(Random.Range(80f, 320f), Random.Range(1f, 3f));
            line.anchoredPosition = new Vector2(Random.Range(-W * 1.5f, W * 1.5f), Random.Range(-95f, 95f));
        }

        // サブタイトル・技名
        var subText = CreateText("Sub", band, sub, 26f, 4f);
        subText.color = Color.white;
        subText.rectTransform.anchoredPosition = new Vector2(0f, 62f);
        materials.Add(SetupTextMaterial(subText, new Color(0f, 0f, 0f, 1f), 0.3f, null));

        var nameText = CreateText("Name", band, skillName, 104f, 18f);
        nameText.color = Color.white;
        nameText.enableVertexGradient = true;
        Color top = Color.white, bottom = new Color32(0xff, 0xab, 0x00, 0xff);
        nameText.colorGradient = new VertexGradient(top, top, bottom, bottom);
        nameText.rectTransform.anchoredPosition = new Vector2(0f, -18f);
        nameText.rectTransform.localScale = Vector3.one * 2.6f;
        materials.Add(SetupTextMaterial(nameText, new Color32(0x1a, 0x00, 0x00, 0xff), 0.25f, color));

        var seq = DOTween.Sequence();
        seq.Insert(0f, dim.DOFade(0.6f, 0.15f));
        seq.Insert(0f, band.DOAnchorPosX(0f, 0.25f).SetEase(Ease.OutCubic));
        seq.Insert(0.12f, nameText.rectTransform.DOScale(1f, 0.3f).SetEase(Ease.OutBack, 2f));
        seq.Insert(0f, lines.DOAnchorPosX(-600f, 1.3f).SetEase(Ease.Linear));
        seq.InsertCallback(0.3f, () =>
        {
            Shake(root, 12f, 0.35f);
            Flash(root, color, 0.35f, 0.25f);
            Burst(new Color[] { color, Color.white, new Color32(0xff, 0xd5, 0x4a, 0xff) }, 40, 600f, 0.7f);
        });
        seq.Insert(1.15f, band.DOAnchorPosX(W * 2.2f - W / 2f, 0.25f).SetEase(Ease.InCubic));
        seq.Insert(1.2f, dim.DOFade(0f, 0.2f));

        var completion = new AwaitableCompletionSource();
        seq.OnKill(() => completion.TrySetResult());

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, destroyCancellationToken);
        using var registration = linked.Token.Register(() => seq.Kill());
        try
        {
            await completion.Awaitable;
        }
        finally
        {
            StopShake();
            if (root != null) Destroy(root.gameObject);
            foreach (var m in materials) Destroy(m);
        }
        linked.Token.ThrowIfCancellationRequested();
    }

    // ---- 演出パーツ ---------------------------------------------------------

    /// <summary>減衰付きのランダム揺れ。カットイン自体とカメラ（盤面）を同時に揺らす</summary>
    private void Shake(RectTransform target, float power, float duration)
    {
        StopShake();
        shakeRect = target;
        Camera cam = shakeCamera != null ? shakeCamera : Camera.main;
        shakeCamTransform = cam != null ? cam.transform : null;
        // 論理ピクセル → ワールド座標の換算（正射影カメラ前提）
        float worldPerPixel = cam != null && cam.orthographic ? cam.orthographicSize * 2f / H : 0.01f;

        shakeTween = DOVirtual.Float(1f, 0f, duration, k =>
        {
            float pw = power * k;
            var offset = new Vector2(Random.Range(-pw, pw), Random.Range(-pw, pw));
            if (shakeRect != null)
            {
                shakeRect.anchoredPosition += offset - shakeRectOffset;
                shakeRectOffset = offset;
            }
            if (shakeCamTransform != null)
            {
                Vector3 camOffset = (Vector3)(offset * worldPerPixel);
                shakeCamTransform.localPosition += camOffset - shakeCamOffset;
                shakeCamOffset = camOffset;
            }
        }).SetEase(Ease.Linear).OnKill(ResetShakeOffsets);
    }

    private void StopShake()
    {
        if (shakeTween != null && shakeTween.IsActive()) shakeTween.Kill();
        shakeTween = null;
        ResetShakeOffsets();
    }

    private void ResetShakeOffsets()
    {
        if (shakeRect != null) shakeRect.anchoredPosition -= shakeRectOffset;
        if (shakeCamTransform != null) shakeCamTransform.localPosition -= shakeCamOffset;
        shakeRectOffset = Vector2.zero;
        shakeCamOffset = Vector3.zero;
    }

    /// <summary>画面フラッシュ</summary>
    private void Flash(RectTransform parent, Color color, float alpha, float duration)
    {
        var flash = CreateImage("Flash", parent, new Color(color.r, color.g, color.b, alpha));
        Stretch(flash.rectTransform, 300f);
        flash.DOFade(0f, duration).OnKill(() => { if (flash != null) Destroy(flash.gameObject); });
    }

    /// <summary>画面中央から放射状に飛び散るパーティクル</summary>
    private void Burst(Color[] colors, int count, float speed, float life)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float sp = speed * Random.Range(0.25f, 1f);
            var image = CreateImage("Particle", particleLayer, colors[Random.Range(0, colors.Length)]);
            image.sprite = glowSprite;
            image.rectTransform.sizeDelta = new Vector2(64f, 64f);
            float scale = 0.25f * Random.Range(0.6f, 1.3f);
            image.rectTransform.localScale = Vector3.one * scale;
            float l = life * Random.Range(0.6f, 1.2f);
            particles.Add(new Particle
            {
                rect = image.rectTransform,
                image = image,
                velocity = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * sp,
                life = l,
                maxLife = l,
                drag = 0.93f,
                scale = scale,
            });
        }
    }

    // ---- UI 組み立て ---------------------------------------------------------

    private void BuildCanvas()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(W, H);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // プロトタイプと同じく小さい方の比率に合わせる
        canvasRect = (RectTransform)transform;

        // パーティクルは暗転・帯より下（プロトタイプの fx レイヤー相当）
        particleLayer = CreateRect("Particles", canvasRect);
        glowSprite = CreateGlowSprite(64);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }

    private static Image CreateImage(string name, Transform parent, Color color)
    {
        var image = CreateRect(name, parent).gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Stretch(RectTransform rect, float margin)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-margin, -margin);
        rect.offsetMax = new Vector2(margin, margin);
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, float letterSpacingPx)
    {
        var tmp = CreateRect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.characterSpacing = letterSpacingPx / size * 100f; // px → em/100
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        tmp.rectTransform.sizeDelta = new Vector2(W * 2f, size * 1.5f);
        return tmp;
    }

    /// <summary>縁取り（と技色のグロー）を設定したマテリアルインスタンスを返す。破棄は呼び出し側で行う</summary>
    private static Material SetupTextMaterial(TextMeshProUGUI tmp, Color outlineColor, float outlineWidth, Color? glowColor)
    {
        Material mat = tmp.fontMaterial;
        mat.EnableKeyword(ShaderUtilities.Keyword_Outline);
        mat.SetColor(ShaderUtilities.ID_OutlineColor, outlineColor);
        mat.SetFloat(ShaderUtilities.ID_OutlineWidth, outlineWidth);
        if (glowColor.HasValue)
        {
            mat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
            mat.SetColor(ShaderUtilities.ID_UnderlayColor, glowColor.Value);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, 0f);
            mat.SetFloat(ShaderUtilities.ID_UnderlayDilate, 0.6f);
            mat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 1f);
        }
        tmp.UpdateMeshPadding();
        return mat;
    }

    /// <summary>中心が白く外側が透明な円形グラデーション（プロトタイプの makeGlowTexture）</summary>
    private static Sprite CreateGlowSprite(int size)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        var pixels = new Color32[size * size];
        float half = size / 2f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                float a = d < 0.25f ? Mathf.Lerp(1f, 0.85f, d / 0.25f) : Mathf.Lerp(0.85f, 0f, (d - 0.25f) / 0.75f);
                pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(a));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));
    }
}
