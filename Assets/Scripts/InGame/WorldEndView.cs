using UnityEngine;
using DG.Tweening;
using TMPro;

// 超常魔法「世界の終わり」の進行中の表示（Prototype の Game.js の worldEndBanner・refreshWorldEnd・doomTint）。
//
//  ・盤の下の枠に「世界の終わり 進行中」と、発動したプレイヤー・1 ターンに崩れるマスの数を出す（赤い光が明滅する）
//  ・背景をうっすら赤黒く染める（盤の外周の発光が赤く速く脈打つのは GameTickEffects）
// 世界の終わりが始まったとき（GameSystem.WorldEndStarted）に出す。カメラが 180° 回っても、常に画面で盤の下に来る。
// シーンに置かなくても GameSystem が自動で追加する
public class WorldEndView : MonoBehaviour
{
    private const float PX = 1f / 66f; // プロトタイプの1マス(66px) = Unity の1マス

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む

    [Header("Sorting")]
    [SerializeField] private int bannerOrder = 220;  // 駒・魔法の演出より手前
    [SerializeField] private int tintOrder = -25;    // 背景の火の粉(-20)・盤の外周の発光(-10)・盤(0)より奥

    private static readonly Color TintColor = Hex(0x3a0000);
    private static readonly Color GlowColor = Hex(0xff1744);
    private static readonly Color BgColor = WithAlpha(Hex(0x1a0000), 0.95f);
    private static readonly Color RimColor = Hex(0xff1744);
    private const float TintAlpha = 0.55f;

    private Camera cam;
    private SpriteRenderer tint;
    private Transform banner;
    private SpriteRenderer bannerGlow;
    private SpriteRenderer bannerBg;
    private TextMeshPro bannerText;
    private string shownKey;
    private float time;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        cam = Camera.main;
        if (gameSystem == null) return;

        BuildTint();
        BuildBanner();
        gameSystem.WorldEndStarted += OnWorldEndStarted;
        Refresh(false);
    }

    void OnDestroy()
    {
        if (gameSystem != null) gameSystem.WorldEndStarted -= OnWorldEndStarted;
        if (tint != null) Destroy(tint.gameObject);
        if (banner != null) Destroy(banner.gameObject);
    }

    void OnWorldEndStarted(PlayerType owner)
    {
        Refresh(true);
    }

    // 発動状況を表示に反映する（Prototype の refreshWorldEnd）
    void Refresh(bool animate)
    {
        bool on = gameSystem.IsWorldEndActive;
        if (on)
        {
            MatchState match = gameSystem.Match;
            string owners = "";
            int count = 0;
            foreach (PlayerType p in new[] { PlayerType.Player1, PlayerType.Player2 })
            {
                if (!match.WorldEndSince(p).HasValue) continue;
                owners += (owners.Length > 0 ? "・" : "") + TurnEndAction.SideName(p);
                count++;
            }
            string key = $"{owners}/{count}";
            if (key != shownKey)
            {
                shownKey = key;
                bannerText.text = $"世界の終わり 進行中<size=70%><color=#ffcdd2>　｜　{owners}　1ターンに {count * MatchConfig.WorldEndCells} マス崩壊</color></size>";
                ResizeBanner();
            }
            if (!banner.gameObject.activeSelf)
            {
                banner.gameObject.SetActive(true);
                if (animate)
                {
                    banner.localScale = Vector3.one * 2f;
                    banner.DOScale(1f, 0.5f).SetEase(Ease.OutBack, 2f).SetLink(banner.gameObject);
                }
            }
        }
        else if (banner.gameObject.activeSelf)
        {
            banner.gameObject.SetActive(false);
        }

        // 背景の赤み
        tint.DOKill();
        tint.DOFade(on ? TintAlpha : 0f, on ? 1.2f : 0.3f).SetLink(tint.gameObject);
    }

    void LateUpdate()
    {
        if (banner == null || !banner.gameObject.activeSelf) return;
        time += Mathf.Min(0.05f, Time.deltaTime);

        // 画面で盤の下の枠に重ねる（カメラが回っても画面の下）
        Vector3 up = cam != null ? cam.transform.up : Vector3.up;
        Vector3 center = gameBoard != null ? gameBoard.transform.TransformPoint(GameHelper.CalcPanelLocation(4, 4)) : Vector3.zero;
        Vector3 pos = center - up * (4.5f + 11f * PX);
        banner.position = new Vector3(pos.x, pos.y, center.z - 0.3f);
        banner.rotation = Quaternion.Euler(0f, 0f, cam != null ? cam.transform.eulerAngles.z : 0f);

        // 赤い光が明滅する
        bannerGlow.color = WithAlpha(GlowColor, 0.45f + 0.4f * Mathf.Sin(time * 4f));

        // 背景の赤みはカメラについていく（揺れ・回転しても画面を覆う）
        if (cam != null && tint != null)
        {
            tint.transform.position = new Vector3(cam.transform.position.x, cam.transform.position.y, center.z + 0.5f);
        }
    }

    // ------------------------------------------------------------
    //  作る
    // ------------------------------------------------------------
    void BuildTint()
    {
        float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float halfW = halfH * (cam != null ? cam.aspect : 16f / 9f);
        tint = new GameObject("WorldEndTint").AddComponent<SpriteRenderer>();
        tint.sprite = MagicSprites.Square;
        tint.sortingOrder = tintOrder;
        tint.color = WithAlpha(TintColor, 0f);
        // カメラが回っても覆えるよう、画面の対角線より大きい正方形にする
        float size = Mathf.Sqrt(halfW * halfW + halfH * halfH) * 2f + 4f;
        tint.transform.localScale = new Vector3(size, size, 1f);
    }

    void BuildBanner()
    {
        banner = new GameObject("WorldEndBanner").transform;

        bannerGlow = new GameObject("Glow").AddComponent<SpriteRenderer>();
        bannerGlow.transform.SetParent(banner, false);
        bannerGlow.sortingOrder = bannerOrder;
        bannerBg = new GameObject("Bg").AddComponent<SpriteRenderer>();
        bannerBg.transform.SetParent(banner, false);
        bannerBg.transform.localPosition = new Vector3(0f, 0f, -0.001f);
        bannerBg.sortingOrder = bannerOrder + 1;

        GameObject textObj = new GameObject("Text", typeof(RectTransform));
        textObj.transform.SetParent(banner, false);
        textObj.transform.localPosition = new Vector3(0f, 0f, -0.002f);
        bannerText = textObj.AddComponent<TextMeshPro>();
        if (font != null) bannerText.font = font;
        bannerText.fontSize = 17f * 0.153f; // NewPieceFx・BoardCellView と同じ換算（17px ≒ 2.6）
        bannerText.fontStyle = FontStyles.Bold;
        bannerText.characterSpacing = 4f;
        bannerText.alignment = TextAlignmentOptions.Center;
        bannerText.textWrappingMode = TextWrappingModes.NoWrap;
        bannerText.overflowMode = TextOverflowModes.Overflow;
        bannerText.richText = true;
        bannerText.color = Hex(0xffe4e1);
        bannerText.outlineColor = new Color32(0x1a, 0x00, 0x00, 0xff);
        bannerText.outlineWidth = 0.25f;
        bannerText.sortingOrder = bannerOrder + 2;
        bannerText.rectTransform.sizeDelta = new Vector2(12f, 1f);

        banner.gameObject.SetActive(false);
    }

    // 文字の長さに合わせて、角丸の背景と赤い光を作り直す
    void ResizeBanner()
    {
        Vector2 pref = bannerText.GetPreferredValues(bannerText.text);
        float wPx = pref.x / PX + 36f;
        const float hPx = 30f;
        DestroySprite(bannerBg.sprite);
        DestroySprite(bannerGlow.sprite);
        bannerBg.sprite = MakePill(wPx, hPx, 0f, BgColor, RimColor);
        bannerGlow.sprite = MakePill(wPx + 4f, hPx + 4f, 10f, Color.white, Color.white);
    }

    static void DestroySprite(Sprite sprite)
    {
        if (sprite == null) return;
        if (sprite.texture != null) Destroy(sprite.texture);
        Destroy(sprite);
    }

    // 角丸（両端が半円）の板。blur > 0 なら白くぼかした光（色は SpriteRenderer で付ける）、0 なら塗り＋縁 2px
    static Sprite MakePill(float wPx, float hPx, float blur, Color fill, Color rim)
    {
        const float scale = 2f; // 1px = 2 テクセル
        float margin = blur * 2f + 2f;
        int tw = Mathf.CeilToInt((wPx + margin * 2f) * scale);
        int th = Mathf.CeilToInt((hPx + margin * 2f) * scale);
        Texture2D tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        Color[] pixels = new Color[tw * th];
        float r = hPx * 0.5f;
        float halfLine = wPx * 0.5f - r;
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                float px = (x + 0.5f) / scale - tw / scale * 0.5f;
                float py = (y + 0.5f) / scale - th / scale * 0.5f;
                float dx = Mathf.Max(0f, Mathf.Abs(px) - halfLine);
                float d = Mathf.Sqrt(dx * dx + py * py) - r; // 縁までの距離（内側が負）
                Color c;
                if (blur > 0f)
                {
                    float a = d <= 0f ? 1f : Mathf.Exp(-(d * d) / (2f * blur * blur));
                    c = new Color(1f, 1f, 1f, a);
                }
                else
                {
                    c = WithAlpha(fill, fill.a * Mathf.Clamp01(0.5f - d));
                    float edge = Mathf.Clamp01(1.5f - Mathf.Abs(d + 1f)); // 内側 2px の縁
                    c = Color.Lerp(c, rim, edge * Mathf.Clamp01(0.5f - d));
                }
                pixels[y * tw + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), scale / PX);
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
