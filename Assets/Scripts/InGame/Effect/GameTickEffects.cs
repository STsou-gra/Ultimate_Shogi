using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// 毎フレームの演出（プロトタイプ index.html の Game.tick の移植）
// ・sin(t*5) のパルスで 必殺技オーラ・スキルパネルの枠・使えるスキルボタン・手番テキストを明滅
// ・オーラ中の駒から光の粒、火薬の導火線から火花、狙撃手のスコープの赤い光の明滅、背景を漂う火の粉
// ・盤上の駒は自軍の色の淡い光（陣営オーラ。先手：青 0x2f7bff ／ 後手：赤 0xff2a4a）をまとう。駒の形にぼかした光が駒の後ろで sin(t*2) でゆっくり息づく
//   （盾のバリアは水色 0x4fc3f7 の六角形を駒の手前に重ねるので、濃い青の駒形の光とは形・色・前後で見分けられる）
// ・盾状態の駒に水色のバリア（六角形。現れるときは大きく出て縮み、sin(t*3) でゆらめく）
// ・ファランクス状態の駒に金色の槍（前方 1×3 へ構えた穂先。現れるときは小さく出て広がり、sin(t*4) で光りながら前へ小さく突き出す）
// ・アンデッドの駒に紫の瘴気（後ろでゆらめく。sin(t*2.2)）と緑の鬼火（2 つが周りを回り、明滅しながら漂う）、立ち上る緑の火の粉
//   （駒の本体を紫がかった半透明にするのは GamePiece.MakeUndead）
// ・盤の外周の発光 sin(t*1.5)、移動先ハイライトの明滅 sin(t*6)
// ・世界の終わり（超常魔法）の進行中は、盤の外周の発光が赤く速く sin(t*4) 脈打ち、背景の火の粉が赤くなる
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
    private static readonly Color ScopeColor = Hex(0xff1744);
    private static readonly Color[] EmberColors = { Hex(0xff8a3d), Hex(0xffd54a), Hex(0xb388ff), Hex(0xff5277) };
    private static readonly Color SkillGlowColor = Hex(0xffd54a);
    private static readonly Color BoardGlowColor = Hex(0xffb74d);
    private static readonly Color DoomGlowColor = Hex(0xff2a2a);
    private static readonly Color[] DoomEmberColors = { Hex(0xff1744), Hex(0xd50000), Hex(0xff6d00), Hex(0x8d6e63) };
    private static readonly Color BarrierColor = Hex(0x4fc3f7);
    private static readonly Color SpearColor = Hex(0xffc400);
    // 陣営オーラ（Prototype の config.js の SIDE_GLOW_COLORS）。盾のバリア（水色）と紛れないよう、パネルの色より濃い青にしている
    public static readonly Color[] SideGlowColors = { Hex(0x2f7bff), Hex(0xff2a4a) };
    // アンデッド（Prototype の config.js の UNDEAD_COLORS）
    public static readonly Color UndeadMiasmaColor = Hex(0x9c4dff);
    public static readonly Color UndeadVeilColor = Hex(0x4a148c);
    public static readonly Color[] UndeadFireColors = { Hex(0x76ff03), Hex(0xb2ff59), Hex(0x69f0ae), Hex(0xccff90) };

    // 駒ごとの演出オブジェクト（駒の子に作るので、駒と一緒に破棄される）
    private class PieceFx
    {
        public SpriteRenderer side;        // 陣営オーラ（駒の後ろ）
        public float sideAge;              // 陣営オーラが付いてからの秒数（ふわっと現れる演出用）
        public Transform aura;
        public SpriteRenderer auraGlow;
        public SpriteRenderer auraRing;
        public SpriteRenderer spark;
        public SpriteRenderer scope;
        public Transform barrier;
        public SpriteRenderer barrierGlow;
        public SpriteRenderer barrierFace;
        public float barrierAge; // バリアが現れてからの秒数（現れる演出用）
        public Transform spear;
        public SpriteRenderer spearGlow;
        public SpriteRenderer spearFace;
        public float spearAge;   // 槍が現れてからの秒数（現れる演出用）
        public SpriteRenderer miasma;      // アンデッドの瘴気（駒の後ろ）
        public SpriteRenderer veil;        // 〃 足元の暗い紫のもや
        public Transform wisps;            // 〃 鬼火（2 つ）
        public SpriteRenderer[] wispSprites;
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
    private float worldTime; // 盤の外周の発光の時刻（時間停止中は進まない）

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
        // 時間停止中（超常魔法「停止世界」）は、駒のオーラ・盤の外周の発光・背景の火の粉も止まる（Prototype の tick と同じ）。
        // 移動先ハイライトの明滅・スキルボタンなどの UI は動かす
        bool frozen = gameSystem != null && gameSystem.IsTimeStopped;
        if (!frozen) worldTime += dt;
        time += dt;
        float t = time;
        float pulse = 0.5f + 0.5f * Mathf.Sin(t * 5f);
        bool inTurn = gameSystem != null && gameSystem.IsInTurn;

        // 陣営オーラは時間停止中も新しく置かれた駒に付ける（ゆらぎだけ止める）
        UpdateSideGlows(frozen, dt);
        if (!frozen) UpdatePieces(dt, pulse);

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

        // 盤の外周の発光（世界の終わりの進行中は赤く速く脈打つ）
        bool doom = gameSystem != null && gameSystem.IsWorldEndActive;
        if (boardGlow != null)
        {
            Color c = doom ? DoomGlowColor : BoardGlowColor;
            c.a = 0.4f * (0.6f + 0.4f * Mathf.Sin(worldTime * (doom ? 4f : 1.5f)));
            boardGlow.color = c;
        }

        UpdateUI(inTurn, pulse);

        // 背景を漂う火の粉
        if (!frozen && bgLayer != null && Random.value < dt * 10f)
        {
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;
            Vector2 pos = new Vector2(Random.Range(-halfW, halfW), -halfH - 10f * PX);
            particles.Spawn(bgLayer, pos, LocalVelocity(Random.Range(-15f, 15f), Random.Range(-70f, -25f)),
                Pick(doom ? DoomEmberColors : EmberColors), Random.Range(5f, 10f), GlowScale(Random.Range(0.08f, 0.25f)), ORDER_BG,
                drag: 1f, alpha: 0.5f, shrink: false);
        }
    }

    void UpdatePieces(float dt, float pulse)
    {
        if (gameBoard == null) return;

        foreach (GamePiece piece in gameBoard.GetBoardData())
        {
            if (piece == null) continue;
            bool shielded = piece.hasShield || Time.time < piece.barrierHoldUntil;
            bool needsFx = piece.isSpecialReady || piece.hasFuse || piece.hasScope || shielded || piece.hasPhalanx || piece.isUndead;
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

            // 狙撃手のスコープの赤い光（ゆっくり明滅し、ときどき鋭く光る）
            if (piece.hasScope)
            {
                if (fx.scope == null) CreateScope(piece, fx);
                fx.scope.gameObject.SetActive(true);
                fx.scope.transform.localPosition = piece.scopeOffset;
                float glint = 0.35f + 0.45f * (0.5f + 0.5f * Mathf.Sin(time * 3f + piece.X * 0.7f + piece.Y));
                if (Random.value < dt * 0.6f) glint = 1f;
                fx.scope.color = WithAlpha(ScopeColor, glint);
                fx.scope.transform.localScale = Vector3.one * GlowScale(0.16f + 0.08f * glint);
            }
            else if (fx.scope != null)
            {
                fx.scope.gameObject.SetActive(false);
            }

            // 盾のバリア（Prototype の pieceView.js の barrier・Game.tick のゆらめき・events.js の shieldGiven）
            if (shielded)
            {
                if (fx.barrier == null) CreateBarrier(piece, fx);
                if (!fx.barrier.gameObject.activeSelf)
                {
                    fx.barrier.gameObject.SetActive(true);
                    fx.barrierAge = 0f;
                }
                fx.barrierAge += dt;
                // 大きく出て縮む（0.45 秒・back.out(2.5)）、0.3 秒でフェードイン
                float k = Mathf.Clamp01(fx.barrierAge / 0.45f);
                float back = 1f + 3.5f * Mathf.Pow(k - 1f, 3f) + 2.5f * Mathf.Pow(k - 1f, 2f);
                fx.barrier.localScale = Vector3.one * Mathf.LerpUnclamped(1.8f, 1f, back);
                float appear = Mathf.Clamp01(fx.barrierAge / 0.3f);
                float flicker = 0.55f + 0.45f * Mathf.Sin(time * 3f + piece.GetInstanceID() * 0.37f);
                fx.barrierGlow.color = WithAlpha(BarrierColor, appear * flicker);
                fx.barrierFace.color = WithAlpha(Color.white, appear);
            }
            else if (fx.barrier != null && fx.barrier.gameObject.activeSelf)
            {
                fx.barrier.gameObject.SetActive(false);
            }

            // ファランクスの槍（Prototype の pieceView.js の spear・Game.tick・events.js の phalanxGiven / phalanxStep）。
            // 駒のローカルの +Y が前（後手の駒は 180°回っている）なので、槍はいつも駒の前方を向く
            if (piece.hasPhalanx)
            {
                if (fx.spear == null) CreateSpear(piece, fx);
                if (!fx.spear.gameObject.activeSelf)
                {
                    fx.spear.gameObject.SetActive(true);
                    fx.spearAge = 0f;
                }
                fx.spearAge += dt;
                // 小さく出て広がる（0.5 秒・back.out(2.5)）、0.3 秒でフェードイン
                float k = Mathf.Clamp01(fx.spearAge / 0.5f);
                float back = 1f + 3.5f * Mathf.Pow(k - 1f, 3f) + 2.5f * Mathf.Pow(k - 1f, 2f);
                float scale = Mathf.LerpUnclamped(0.3f, 1f, back);
                // 自動行動のときに弾む（1.3 倍から 0.35 秒・back.out(3)）
                float pulseAge = Time.time - piece.spearPulseAt;
                if (pulseAge >= 0f && pulseAge < 0.35f)
                {
                    float pk = pulseAge / 0.35f;
                    float pback = 1f + 4f * Mathf.Pow(pk - 1f, 3f) + 3f * Mathf.Pow(pk - 1f, 2f);
                    scale *= Mathf.LerpUnclamped(1.3f, 1f, pback);
                }
                fx.spear.localScale = Vector3.one * scale;
                // 穂先が光りながら、前へ小さく突き出す
                float s = Mathf.Sin(time * 4f + piece.GetInstanceID() * 0.37f);
                fx.spear.localPosition = new Vector3(0f, (1.5f + 1.5f * s) * PX, 0f);
                float appear = Mathf.Clamp01(fx.spearAge / 0.3f);
                fx.spearGlow.color = WithAlpha(SpearColor, appear * (0.5f + 0.5f * s));
                fx.spearFace.color = WithAlpha(Color.white, appear);
            }
            else if (fx.spear != null && fx.spear.gameObject.activeSelf)
            {
                fx.spear.gameObject.SetActive(false);
            }

            // アンデッド（Prototype の pieceView.js の miasma・veil・wisps と Game.tick）：
            // 紫の瘴気がゆらめき、緑の鬼火が周りを漂いながら立ち上る
            if (piece.isUndead) UpdateUndead(piece, fx, dt);
        }

        // 盤から消えた駒の記録を捨てる
        deadPieces.Clear();
        foreach (var kvp in pieceFx)
        {
            if (kvp.Key == null) deadPieces.Add(kvp.Key);
        }
        foreach (GamePiece dead in deadPieces) pieceFx.Remove(dead);
    }

    // 陣営オーラ（Prototype の pieceView.js の side と Game.tick の side.alpha）：盤上のすべての駒の後ろに、自軍の色の駒形の光
    void UpdateSideGlows(bool frozen, float dt)
    {
        if (gameBoard == null) return;
        foreach (GamePiece piece in gameBoard.GetBoardData())
        {
            if (piece == null) continue;
            PieceFx fx;
            if (!pieceFx.TryGetValue(piece, out fx))
            {
                // 移動の途中は表示順が一時的に上がっているので、着いてから付ける
                if (DG.Tweening.DOTween.IsTweening(piece.transform)) continue;
                fx = new PieceFx();
                pieceFx.Add(piece, fx);
            }
            if (fx.side == null)
            {
                if (DG.Tweening.DOTween.IsTweening(piece.transform)) continue;
                CreateSideGlow(piece, fx);
                if (fx.side == null) continue;
                // 時間停止中はゆらがないので、そのまま出す
                fx.sideAge = frozen ? 1f : 0f;
                if (frozen) fx.side.color = WithAlpha(SideGlowColors[piece.player == PlayerType.Player1 ? 0 : 1], 0.85f);
            }
            if (frozen) continue;
            // ゆっくり息づく（0.85 + 0.15 * sin(t*2 + id)）。付いた直後は 0.3 秒でふわっと現れる
            fx.sideAge += dt;
            float phase = (piece.GetInstanceID() % 1000) * 0.37f;
            float appear = Mathf.Clamp01(fx.sideAge / 0.3f);
            fx.side.color = WithAlpha(SideGlowColors[piece.player == PlayerType.Player1 ? 0 : 1], appear * (0.85f + 0.15f * Mathf.Sin(worldTime * 2f + phase)));
        }
    }

    // 陣営オーラ：駒の本体の画像の大きさに合わせた駒形の光を、駒のすべての表示より奥に置く
    void CreateSideGlow(GamePiece piece, PieceFx fx)
    {
        SpriteRenderer body = null;
        foreach (SpriteRenderer sr in piece.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (sr.sprite == null || IsPieceOverlay(sr.transform) || sr.transform == piece.transform) continue;
            body = sr;
            break;
        }
        if (body == null) return;

        // 本体の画像の大きさ・中心（駒のローカル座標）
        Vector3 ls = body.transform.localScale;
        Vector2 size = Vector2.Scale(body.sprite.bounds.size, new Vector2(Mathf.Abs(ls.x), Mathf.Abs(ls.y)));
        Vector2 center = (Vector2)body.transform.localPosition + Vector2.Scale(body.sprite.bounds.center, new Vector2(ls.x, ls.y));

        // ぼかしの幅はプロトタイプの 1マスの 0.12 倍（盤上の 1マス = ワールドの 1）を駒のローカルの長さに直す
        float worldPerLocal = Mathf.Max(0.0001f, Mathf.Abs(piece.transform.lossyScale.x));
        float blur = 0.12f / worldPerLocal;

        fx.side = CreateChildSprite(piece.transform, SideName, GetSideGlowSprite(size, blur), MinSortingOrder(piece) - 2, Vector2.one);
        fx.side.transform.localPosition = new Vector3(center.x, center.y, 0f);
        fx.side.color = WithAlpha(SideGlowColors[piece.player == PlayerType.Player1 ? 0 : 1], 0f);
    }

    private static readonly Dictionary<Vector3Int, Sprite> sideGlowSprites = new Dictionary<Vector3Int, Sprite>();

    // 駒の形にぼかした白い光（Prototype の textures.js の makePieceGlowTexture。tint で色を付ける）。中心は駒の中心
    static Sprite GetSideGlowSprite(Vector2 size, float blur)
    {
        Vector3Int key = new Vector3Int(Mathf.RoundToInt(size.x * 100f), Mathf.RoundToInt(size.y * 100f), Mathf.RoundToInt(blur * 100f));
        Sprite cached;
        if (sideGlowSprites.TryGetValue(key, out cached) && cached != null) return cached;

        // 駒の画像の輪郭（先端・肩・底の角）。プロトタイプと同じく、本体より 1.14×1.1 倍に広げて光らせる
        float w = size.x * 1.14f, h = size.y * 1.1f;
        Vector2[] poly =
        {
            new Vector2(0f, h * 0.5f), new Vector2(w * 0.40f, h * 0.34f), new Vector2(w * 0.5f, -h * 0.5f),
            new Vector2(-w * 0.5f, -h * 0.5f), new Vector2(-w * 0.40f, h * 0.34f),
        };
        // canvas の shadowBlur は標準偏差がその半分のぼかし。3 回重ね塗りして光を濃くしている
        float sigma = Mathf.Max(0.001f, blur * 0.5f);
        float margin = sigma * 4f;
        const float ppu = 96f;
        int tw = Mathf.CeilToInt((w + margin * 2f) * ppu);
        int th = Mathf.CeilToInt((h + margin * 2f) * ppu);
        Texture2D tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[tw * th];
        for (int y = 0; y < th; y++)
        {
            for (int x = 0; x < tw; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / ppu - tw / (2f * ppu), (y + 0.5f) / ppu - th / (2f * ppu));
                float d = SignedPolygonDistance(p, poly);
                float a0 = 0.5f * Erfc(d / (sigma * 1.41421356f));
                float a = 1f - Mathf.Pow(1f - a0, 3f);
                pixels[y * tw + x] = new Color(1f, 1f, 1f, a);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        Sprite sprite = Sprite.Create(tex, new Rect(0, 0, tw, th), new Vector2(0.5f, 0.5f), ppu);
        sideGlowSprites[key] = sprite;
        return sprite;
    }

    // 多角形までの符号付き距離（内側が負）
    static float SignedPolygonDistance(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            best = Mathf.Min(best, (p - (a + ab * t)).magnitude);
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside ? -best : best;
    }

    // 相補誤差関数（Abramowitz–Stegun 7.1.26 の近似）
    static float Erfc(float x)
    {
        float z = Mathf.Abs(x);
        float t = 1f / (1f + 0.3275911f * z);
        float y = t * (0.254829592f + t * (-0.284496736f + t * (1.421413741f + t * (-1.453152027f + t * 1.061405429f)))) * Mathf.Exp(-z * z);
        return x >= 0f ? y : 2f - y;
    }

    void UpdateUndead(GamePiece piece, PieceFx fx, float dt)
    {
        if (fx.miasma == null) CreateUndead(piece, fx);
        int seed = piece.GetInstanceID();
        float phase = (seed % 1000) * 0.37f;
        fx.miasma.color = WithAlpha(UndeadMiasmaColor, 0.35f + 0.3f * Mathf.Sin(time * 2.2f + phase));
        fx.veil.color = WithAlpha(UndeadVeilColor, 0.5f + 0.15f * Mathf.Sin(time * 1.6f + phase));
        for (int i = 0; i < fx.wispSprites.Length; i++)
        {
            SpriteRenderer s = fx.wispSprites[i];
            float a = time * 1.8f + phase + i * Mathf.PI;
            // プロトタイプの駒は幅 0.8・高さ 0.9 マス。鬼火は横に w*0.62・縦に h*0.28 の楕円を回る
            s.transform.localPosition = new Vector3(Mathf.Cos(a) * 0.8f * 0.62f, Mathf.Sin(a) * 0.9f * 0.28f, 0f);
            s.color = WithAlpha(UndeadFireColors[i % UndeadFireColors.Length], 0.55f + 0.45f * Mathf.Sin(time * 6f + i * 2.1f + phase));
            s.transform.localScale = Vector3.one * GlowScale(0.22f + 0.05f * Mathf.Sin(time * 5f + i));
        }

        // 立ち上る緑の火の粉（地面から這い上がっている途中など、駒が小さいときは出さない）
        if (piece.transform.lossyScale.x > 0.5f && Random.value < dt * 6f)
        {
            Vector2 pos = (Vector2)piece.transform.position + ScreenOffset(Random.Range(-20f, 20f), Random.Range(-6f, 22f));
            particles.Spawn(fxLayer, pos, ScreenVelocity(Random.Range(-10f, 10f), Random.Range(-55f, -25f)),
                Pick(UndeadFireColors), Random.Range(0.6f, 1.1f), GlowScale(Random.Range(0.1f, 0.22f)), ORDER_FX,
                drag: 1f);
        }
    }

    // アンデッドの瘴気（駒の後ろ）と鬼火（駒の手前）
    void CreateUndead(GamePiece piece, PieceFx fx)
    {
        int back = MinSortingOrder(piece) - 1;
        int front = MaxSortingOrder(piece) + 2;
        Transform root = new GameObject(UndeadName).transform;
        root.SetParent(piece.transform, false);
        // 瘴気：駒より一回り大きい紫の光（プロトタイプは駒の 1.25×1.2 倍をぼかしたもの）
        fx.miasma = CreateChildSprite(root, "Miasma", glowSprite, back, new Vector2(1.6f, 1.75f));
        // 足元のもや：暗い紫を少し下にずらして重ねる
        fx.veil = CreateChildSprite(root, "Veil", glowSprite, back, new Vector2(1.3f, 0.7f));
        fx.veil.transform.localPosition = new Vector3(0f, -0.3f, 0f);
        fx.wisps = new GameObject("Wisps").transform;
        fx.wisps.SetParent(root, false);
        fx.wispSprites = new SpriteRenderer[2];
        for (int i = 0; i < fx.wispSprites.Length; i++)
        {
            fx.wispSprites[i] = CreateChildSprite(fx.wisps, "Wisp", glowSprite, front, Vector2.one * GlowScale(0.24f));
        }
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
        fx.aura = new GameObject(AuraName).transform;
        fx.aura.SetParent(piece.transform, false);

        // プロトタイプの駒は 幅0.8×高さ0.9マス。オーラはぼかした発光（約1.3倍）と輪（約1.15倍）
        fx.auraGlow = CreateChildSprite(fx.aura, "Glow", glowSprite, order, new Vector2(1.45f, 1.55f));
        fx.auraRing = CreateChildSprite(fx.aura, "Ring", ringSprite, order, new Vector2(0.95f, 1.03f));
    }

    // 盾のバリア：駒の上に重ねる（面はうっすらしているので駒は透けて見える）
    void CreateBarrier(GamePiece piece, PieceFx fx)
    {
        int order = MaxSortingOrder(piece) + 1;
        fx.barrier = new GameObject(BarrierName).transform;
        fx.barrier.SetParent(piece.transform, false);
        fx.barrierGlow = CreateChildSprite(fx.barrier, "Glow", MagicSprites.BarrierGlow, order, Vector2.one);
        fx.barrierFace = CreateChildSprite(fx.barrier, "Face", MagicSprites.BarrierFace, order + 1, Vector2.one);
        fx.barrier.gameObject.SetActive(false);
    }

    // ファランクスの槍：駒の上に重ねる（バリアよりさらに手前）
    void CreateSpear(GamePiece piece, PieceFx fx)
    {
        int order = MaxSortingOrder(piece) + 3;
        fx.spear = new GameObject(SpearName).transform;
        fx.spear.SetParent(piece.transform, false);
        fx.spearGlow = CreateChildSprite(fx.spear, "Glow", MagicSprites.SpearGlow, order, Vector2.one);
        fx.spearFace = CreateChildSprite(fx.spear, "Face", MagicSprites.SpearFace, order + 1, Vector2.one);
        fx.spear.gameObject.SetActive(false);
    }

    void CreateSpark(GamePiece piece, PieceFx fx)
    {
        fx.spark = CreateChildSprite(piece.transform, "FuseSpark", glowSprite, MinSortingOrder(piece) + 1, Vector2.one);
        fx.spark.transform.localPosition = piece.fuseOffset;
    }

    void CreateScope(GamePiece piece, PieceFx fx)
    {
        fx.scope = CreateChildSprite(piece.transform, "ScopeGlint", glowSprite, MinSortingOrder(piece) + 1, Vector2.one);
        fx.scope.transform.localPosition = piece.scopeOffset;
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

    private const string SideName = "SideGlow";
    private const string AuraName = "SpecialAura";
    private const string BarrierName = "ShieldBarrier";
    private const string SpearName = "PhalanxSpear";
    private const string UndeadName = "UndeadAura";

    // 駒に重ねる表示（陣営オーラ・必殺技オーラ・バリア・槍・アンデッドの瘴気と鬼火）。駒の描画順を調べるときは数えない
    static bool IsOverlay(Transform t)
    {
        return t.name == SideName || t.name == AuraName || t.name == BarrierName || t.name == SpearName || t.name == UndeadName;
    }

    // t が駒に重ねた表示（またはその中）か。駒の本体のスプライトだけを触りたいとき（MagicFx の這い上がる演出など）に使う
    public static bool IsPieceOverlay(Transform t)
    {
        for (Transform p = t; p != null; p = p.parent)
        {
            if (IsOverlay(p)) return true;
            if (p.GetComponent<GamePiece>() != null) return false;
        }
        return false;
    }

    static int MaxSortingOrder(GamePiece piece)
    {
        int order = int.MinValue;
        foreach (SpriteRenderer sr in piece.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (IsPieceOverlay(sr.transform)) continue;
            order = Mathf.Max(order, sr.sortingOrder);
        }
        return order == int.MinValue ? 10 : order;
    }

    static int MinSortingOrder(GamePiece piece)
    {
        int order = int.MaxValue;
        foreach (SpriteRenderer sr in piece.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (IsPieceOverlay(sr.transform)) continue; // 先に付けたオーラなどの分だけ奥へずれていかないように
            order = Mathf.Min(order, sr.sortingOrder);
        }
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
