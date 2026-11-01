using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 毎フレームの演出（プロトタイプ index.html の Game.tick の移植）
// ・sin(t*5) のパルスで 必殺技オーラ・スキルパネルの枠・使えるスキルボタン・手番テキストを明滅
// ・オーラ中の駒から光の粒、火薬の導火線から火花、背景を漂う火の粉
// ・盤の外周の発光 sin(t*1.5)、移動先ハイライトの明滅 sin(t*6)
// 粒の発生は Random.value < dt * N の確率方式（フレームレートに依存しない）
// シーンに置かなくても GameSystem が自動で追加する。参照を指定しなければシーンから探す
public class GameTickEffects : MonoBehaviour
{
    // プロトタイプの1マス(66px)を Unity の1マス(1unit)に合わせて、ピクセル値をそのまま使う
    private const float PX = 1f / 66f;
    // 光の粒の画像の大きさ（プロトタイプの glow テクスチャは 64px）
    private const float GLOW_TEX_PX = 64f;

    // 描画順（駒は 10、盤は 0）
    private const int ORDER_BG = -20;         // 背景の火の粉
    private const int ORDER_BOARD_GLOW = -10; // 盤の外周の発光
    private const int ORDER_FX = 20;          // 駒の上に出る光の粒・火花

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private SpriteRenderer boardImage;
    [SerializeField] private Graphic skillPanelFrame; // 手番中に枠が光るパネル
    [SerializeField] private TMP_Text turnBadgeText;   // 明滅させる手番表示

    [Header("Colors")]
    [SerializeField] private Color player1Color = Hex(0x4fc3ff);
    [SerializeField] private Color player2Color = Hex(0xff5277);

    private static readonly Color AuraColor = Hex(0xffc400);
    private static readonly Color AuraRingColor = Hex(0xfff59d);
    private static readonly Color[] AuraParticleColors = { Hex(0xffd54a), Hex(0xfff59d) };
    private static readonly Color SparkColor = Hex(0xffa000);
    private static readonly Color[] SparkParticleColors = { Hex(0xffa000), Hex(0xff3d00) };
    private static readonly Color[] EmberColors = { Hex(0xff8a3d), Hex(0xffd54a), Hex(0xb388ff), Hex(0xff5277) };
    private static readonly Color SkillGlowColor = Hex(0xffd54a);
    private static readonly Color BoardGlowColor = Hex(0xffb74d);

    // 駒ごとの演出オブジェクト（駒の子に作るので、駒と一緒に破棄される）
    private class PieceFx
    {
        public Transform aura;
        public SpriteRenderer auraGlow;
        public SpriteRenderer auraRing;
        public SpriteRenderer spark;
    }

    private GameSystem gameSystem;
    private GameBoard gameBoard;
    private Camera cam;
    private GlowParticles particles;
    private Sprite glowSprite;
    private Sprite ringSprite;

    private Transform fxLayer;
    private Transform bgLayer;
    private SpriteRenderer boardGlow;

    private readonly Dictionary<GamePiece, PieceFx> pieceFx = new Dictionary<GamePiece, PieceFx>();
    private readonly List<GamePiece> deadPieces = new List<GamePiece>();

    // 明滅させるハイライト（元の色のアルファに掛ける）
    private readonly List<SpriteRenderer> highlightSprites = new List<SpriteRenderer>();
    private readonly List<float> highlightBaseAlpha = new List<float>();

    private Outline panelOutline;
    private readonly List<Button> skillButtons = new List<Button>();
    private readonly List<Outline> skillButtonOutlines = new List<Outline>();

    private float time;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();
        gameBoard = FindAnyObjectByType<GameBoard>();
        cam = Camera.main;

        glowSprite = CreateGlowSprite(64);
        ringSprite = CreateRingSprite(64);

        GameObject particleObj = new GameObject("GlowParticles");
        particles = particleObj.AddComponent<GlowParticles>();
        particles.Init(glowSprite);

        fxLayer = new GameObject("TickFxLayer").transform;
        if (cam != null)
        {
            // 背景の火の粉はカメラの子にして、盤が回転しても常に画面の下から上へ漂わせる
            bgLayer = new GameObject("TickBgLayer").transform;
            bgLayer.SetParent(cam.transform, false);
            float depth = Mathf.Max(cam.nearClipPlane + 0.01f, -cam.transform.position.z);
            bgLayer.localPosition = new Vector3(0f, 0f, depth);
        }

        SetupBoardGlow();
        SetupHighlights();
        SetupUI();
    }

    void LateUpdate()
    {
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        time += dt;
        float t = time;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f);
        bool inTurn = gameSystem != null && gameSystem.IsInTurn;

        UpdatePieces(dt, pulse);

        // 移動先ハイライト・カーソルの明滅
        float hlAlpha = 0.75f + 0.25f * Mathf.Sin(t * 6f);
        for (int i = 0; i < highlightSprites.Count; i++)
        {
            SpriteRenderer sr = highlightSprites[i];
            if (sr == null || !sr.gameObject.activeInHierarchy) continue;
            Color c = sr.color;
            c.a = highlightBaseAlpha[i] * hlAlpha;
            sr.color = c;
        }

        // 盤の外周の発光
        if (boardGlow != null)
        {
            Color c = BoardGlowColor;
            c.a = 0.4f * (0.6f + 0.4f * Mathf.Sin(t * 1.5f));
            boardGlow.color = c;
        }

        UpdateUI(inTurn, pulse);

        // 背景を漂う火の粉
        if (bgLayer != null && Random.value < dt * 10f)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector2 pos = new Vector2(Random.Range(-halfW, halfW), -halfH - 10f * PX);
            particles.Spawn(bgLayer, pos, LocalVelocity(Random.Range(-15f, 15f), Random.Range(-70f, -25f)),
                Pick(EmberColors), Random.Range(5f, 10f), GlowScale(Random.Range(0.08f, 0.25f)), ORDER_BG,
                drag: 1f, alpha: 0.5f, shrink: false);
        }
    }

    void UpdatePieces(float dt, float pulse)
    {
        if (gameBoard == null) return;

        foreach (GamePiece piece in gameBoard.GetBoardData())
        {
            if (piece == null) continue;
            bool needsFx = piece.isSpecialReady || piece.hasFuse;
            PieceFx fx;
            if (!pieceFx.TryGetValue(piece, out fx))
            {
                if (!needsFx) continue;
                fx = new PieceFx();
                pieceFx.Add(piece, fx);
            }

            // 必殺技発動可能：パルス発光＋立ち上る光の粒
            if (piece.isSpecialReady)
            {
                if (fx.aura == null) CreateAura(piece, fx);
                fx.aura.gameObject.SetActive(true);
                float a = 0.45f + 0.55f * pulse;
                fx.auraGlow.color = WithAlpha(AuraColor, 0.95f * a);
                fx.auraRing.color = WithAlpha(AuraRingColor, a);
                fx.aura.localScale = Vector3.one * (1f + 0.07f * pulse);

                if (Random.value < dt * 7f)
                {
                    Vector2 pos = (Vector2)piece.transform.position + ScreenOffset(Random.Range(-22f, 22f), Random.Range(-10f, 25f));
                    particles.Spawn(fxLayer, pos, ScreenVelocity(Random.Range(-10f, 10f), Random.Range(-70f, -30f)),
                        Pick(AuraParticleColors), Random.Range(0.5f, 0.9f), GlowScale(Random.Range(0.1f, 0.2f)), ORDER_FX,
                        drag: 1f);
                }
            }
            else if (fx.aura != null)
            {
                fx.aura.gameObject.SetActive(false);
            }

            // 火薬の導火線がチリチリ燃える
            if (piece.hasFuse)
            {
                if (fx.spark == null) CreateSpark(piece, fx);
                fx.spark.gameObject.SetActive(true);
                fx.spark.transform.localPosition = piece.fuseOffset;
                fx.spark.color = WithAlpha(SparkColor, Random.Range(0.5f, 1f));
                fx.spark.transform.localScale = Vector3.one * GlowScale(Random.Range(0.2f, 0.38f));

                if (Random.value < dt * 5f)
                {
                    particles.Spawn(fxLayer, fx.spark.transform.position, ScreenVelocity(Random.Range(-30f, 30f), Random.Range(-60f, -10f)),
                        Pick(SparkParticleColors), 0.4f, GlowScale(0.08f), ORDER_FX,
                        gravity: ScreenVelocity(0f, 120f));
                }
            }
            else if (fx.spark != null)
            {
                fx.spark.gameObject.SetActive(false);
            }
        }

        // 盤から消えた駒の記録を捨てる
        deadPieces.Clear();
        foreach (var kvp in pieceFx)
        {
            if (kvp.Key == null) deadPieces.Add(kvp.Key);
        }
        foreach (GamePiece dead in deadPieces) pieceFx.Remove(dead);
    }

    void UpdateUI(bool inTurn, float pulse)
    {
        // 手番中のパネルの枠（手番プレイヤーの色で光る）
        if (panelOutline != null)
        {
            panelOutline.enabled = inTurn;
            if (inTurn)
            {
                Color c = gameSystem.IsPlayer1Turn() ? player1Color : player2Color;
                panelOutline.effectColor = WithAlpha(c, 0.5f + 0.5f * pulse);
            }
        }

        // 使えるスキルボタンを光らせる
        for (int i = 0; i < skillButtons.Count; i++)
        {
            Button button = skillButtons[i];
            Outline outline = skillButtonOutlines[i];
            if (button == null || outline == null) continue;
            bool glowing = inTurn && button.interactable && button.gameObject.activeInHierarchy;
            outline.enabled = glowing;
            if (glowing) outline.effectColor = WithAlpha(SkillGlowColor, 0.4f + 0.6f * pulse);
        }

        // 手番表示
        if (turnBadgeText != null)
        {
            turnBadgeText.alpha = inTurn ? 0.6f + 0.4f * pulse : 1f;
        }
    }

    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------
    void SetupBoardGlow()
    {
        if (boardImage == null)
        {
            GameObject obj = GameObject.Find("BoardImage");
            if (obj != null) boardImage = obj.GetComponent<SpriteRenderer>();
        }

        // 盤の大きさ（見つからなければ 9マス＋外枠）
        Vector2 center = Vector2.zero;
        Vector2 size = Vector2.one * (9f + 2f * 22f * PX);
        if (boardImage != null)
        {
            Bounds b = boardImage.bounds;
            center = b.center;
            size = b.size;
        }

        // 盤より一回り大きい角丸の四角を、ぼかして後ろに置く
        float margin = 8f * PX;
        float blur = 36f * PX;
        GameObject glowObj = new GameObject("BoardGlow");
        glowObj.transform.position = new Vector3(center.x, center.y, 0f);
        boardGlow = glowObj.AddComponent<SpriteRenderer>();
        boardGlow.sprite = CreateBoardGlowSprite(size + Vector2.one * (margin * 2f), 20f * PX, blur);
        boardGlow.sortingOrder = ORDER_BOARD_GLOW;
        boardGlow.color = WithAlpha(BoardGlowColor, 0f);
    }

    void SetupHighlights()
    {
        if (gameBoard != null)
        {
            foreach (GameSelectPanel panel in gameBoard.GetComponentsInChildren<GameSelectPanel>(true))
            {
                foreach (SpriteRenderer sr in panel.GetComponentsInChildren<SpriteRenderer>(true)) AddHighlight(sr);
            }
        }

        GameCursor cursor = FindAnyObjectByType<GameCursor>();
        if (cursor != null)
        {
            foreach (SpriteRenderer sr in cursor.GetComponentsInChildren<SpriteRenderer>(true)) AddHighlight(sr);
        }
    }

    void AddHighlight(SpriteRenderer sr)
    {
        highlightSprites.Add(sr);
        highlightBaseAlpha.Add(sr.color.a);
    }

    void SetupUI()
    {
        if (gameSystem != null)
        {
            foreach (Button button in gameSystem.SkillSlotButtons)
            {
                if (button == null) continue;
                Outline outline = AddOutline(button.gameObject, new Vector2(4f, -4f));
                skillButtons.Add(button);
                skillButtonOutlines.Add(outline);

                // スキルボタンが並んでいるパネルを、手番中に光る枠として使う
                if (skillPanelFrame == null && button.transform.parent != null)
                {
                    skillPanelFrame = button.transform.parent.GetComponent<Graphic>();
                }
            }
        }
        if (skillPanelFrame != null) panelOutline = AddOutline(skillPanelFrame.gameObject, new Vector2(6f, -6f));

        if (turnBadgeText == null && HandUIManager.Instance != null)
        {
            turnBadgeText = HandUIManager.Instance.ActivePlayerText;
        }
    }

    Outline AddOutline(GameObject target, Vector2 distance)
    {
        Outline outline = target.AddComponent<Outline>();
        outline.effectDistance = distance;
        outline.useGraphicAlpha = false;
        outline.enabled = false;
        return outline;
    }

    void CreateAura(GamePiece piece, PieceFx fx)
    {
        int order = MinSortingOrder(piece) - 1;
        fx.aura = new GameObject("SpecialAura").transform;
        fx.aura.SetParent(piece.transform, false);

        // プロトタイプの駒は 幅0.8×高さ0.9マス。オーラはぼかした発光（約1.3倍）と輪（約1.15倍）
        fx.auraGlow = CreateChildSprite(fx.aura, "Glow", glowSprite, order, new Vector2(1.45f, 1.55f));
        fx.auraRing = CreateChildSprite(fx.aura, "Ring", ringSprite, order, new Vector2(0.95f, 1.03f));
    }

    void CreateSpark(GamePiece piece, PieceFx fx)
    {
        fx.spark = CreateChildSprite(piece.transform, "FuseSpark", glowSprite, MinSortingOrder(piece) + 1, Vector2.one);
        fx.spark.transform.localPosition = piece.fuseOffset;
    }

    SpriteRenderer CreateChildSprite(Transform parent, string name, Sprite sprite, int order, Vector2 scale)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    static int MinSortingOrder(GamePiece piece)
    {
        int order = int.MaxValue;
        foreach (SpriteRenderer sr in piece.GetComponentsInChildren<SpriteRenderer>(true)) order = Mathf.Min(order, sr.sortingOrder);
        return order == int.MaxValue ? 10 : order;
    }

    // ------------------------------------------------------------
    //  座標変換（プロトタイプは画面のピクセル値・y下向き）
    // ------------------------------------------------------------
    // 画面上の向きでのずらし量をワールド座標にする（カメラが回転していても画面基準）
    Vector2 ScreenOffset(float x, float y)
    {
        if (cam == null) return new Vector2(x, -y) * PX;
        return (Vector2)(cam.transform.right * x - cam.transform.up * y) * PX;
    }

    Vector2 ScreenVelocity(float vx, float vy) => ScreenOffset(vx, vy);

    // カメラの子のレイヤー用（ローカル座標がそのまま画面の向き）
    static Vector2 LocalVelocity(float vx, float vy) => new Vector2(vx, -vy) * PX;

    // プロトタイプの sprite.scale を Unity のスケールにする（光の粒の画像は1unit）
    static float GlowScale(float size) => size * GLOW_TEX_PX * PX;

    static Color Pick(Color[] colors) => colors[Random.Range(0, colors.Length)];

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }

    // ------------------------------------------------------------
    //  画像の生成
    // ------------------------------------------------------------
    // 中心が白く、外側へ透明になる丸い光（プロトタイプの makeGlowTexture と同じグラデーション）
    static Sprite CreateGlowSprite(int size)
    {
        return CreateSprite(size, size, size, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
            if (d >= 1f) return 0f;
            return d < 0.25f ? Mathf.Lerp(1f, 0.85f, d / 0.25f) : Mathf.Lerp(0.85f, 0f, (d - 0.25f) / 0.75f);
        });
    }

    // オーラの輪
    static Sprite CreateRingSprite(int size)
    {
        return CreateSprite(size, size, size, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x, y), new Vector2(size * 0.5f, size * 0.5f)) / (size * 0.5f);
            return Mathf.Clamp01(1f - Mathf.Abs(d - 0.9f) / 0.08f);
        });
    }

    // 角丸の四角をぼかした光（size は内側の四角の大きさ、blur の分だけ外側に広がる）
    static Sprite CreateBoardGlowSprite(Vector2 size, float radius, float blur)
    {
        const float pixelsPerUnit = 32f;
        Vector2 total = size + Vector2.one * (blur * 2f);
        int w = Mathf.CeilToInt(total.x * pixelsPerUnit);
        int h = Mathf.CeilToInt(total.y * pixelsPerUnit);
        Vector2 half = size * 0.5f - Vector2.one * radius;
        return CreateSprite(w, h, pixelsPerUnit, (x, y) =>
        {
            Vector2 p = new Vector2(x / pixelsPerUnit - total.x * 0.5f, y / pixelsPerUnit - total.y * 0.5f);
            Vector2 q = new Vector2(Mathf.Max(Mathf.Abs(p.x) - half.x, 0f), Mathf.Max(Mathf.Abs(p.y) - half.y, 0f));
            float outside = q.magnitude - radius;
            return 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(outside / blur));
        });
    }

    static Sprite CreateSprite(int w, int h, float pixelsPerUnit, System.Func<float, float, float> alphaAt)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                byte a = (byte)Mathf.RoundToInt(Mathf.Clamp01(alphaAt(x + 0.5f, y + 0.5f)) * 255f);
                pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), pixelsPerUnit);
    }
}
