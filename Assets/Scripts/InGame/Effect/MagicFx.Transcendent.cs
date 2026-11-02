using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 超常魔法の演出（Prototype の view/skillFx.js の cataclysm・worldEnd.doom、view/events.js の collapseWarning・collapsed、
// view/fx.js の doom・dangerFlash）。MagicFx の続き（partial）
//
//  天変地異   … 地鳴り（画面が揺れ、盤から土ぼこり）→ 駒とマスの状態（炎上・落とし穴）が宙に浮く
//               → 放物線を描いて一斉に新しいマスへ飛ぶ（駒は 1 回転）→ 着地（大きな揺れ・閃光・「天変地異！」）
//  世界の終わり … 画面が赤黒く暗くなり、盤の中央から画面の外までひびが走る →「世界が崩れ始める……」
//  崩壊       … 崩れるマスが赤く点滅 →（マスの板が抜け落ちるのは BoardCellView）→ 揺れ
//               進行中の「世界の終わり 進行中」表示・背景の赤みは WorldEndView
//
// 盤面のデータはもう変わっているので、見た目だけを Timeline の時刻に合わせて再生する。
// 演出の乱数（ひびの形・粒の飛び方）は UnityEngine.Random（対局の結果に関わる乱数は GameRng）
public partial class MagicFx
{
    public static readonly Color CataclysmColor = Hex(0xffab40);
    public static readonly Color DangerColor = Hex(0xff1744);
    private static readonly Color[] DustColors = { Hex(0xd7ccc8), Hex(0xbcaaa4), Hex(0x8d6e63) };

    // 天変地異の時間割（秒。Prototype の LIFT・ARC・T_FLY・T_DROP・T_LAND の前に、地鳴りだけの時間を足した）
    private const float QuakeLead = 0.6f;                    // 地鳴りだけの時間（このあと宙に浮き始める）
    private const float LiftTime = 0.5f;                     // 浮き上がるのにかかる時間
    private const float FlyStart = QuakeLead + 0.85f;        // 一斉に飛び始める
    private const float DropStart = QuakeLead + 1.65f;       // 新しいマスの上に着いて、降り始める
    public const float CataclysmTime = QuakeLead + 1.85f;    // 着地（CataclysmEffect はこのあとも少し待つ）
    private const float LiftPx = 34f;                        // 浮く高さ
    private const float ArcPx = 70f;                         // 飛ぶときの放物線の高さ

    // 世界の終わり：暗くなってから暗幕が晴れるまで（WorldEndEffect はこのあと「進行中」にして 1 回目を崩す）
    public const float DoomTime = 2.6f;
    // 崩れるマスが赤く点滅する時間（最後に光った瞬間に崩れ始める）
    public const float CollapseWarningTime = 0.5f;

    private const int DoomOrder = 300; // 暗幕・ひび（魔法の演出(210)より手前、文字(400)より奥）
    private const int FlyingOrderOffset = 100; // 飛んでいる駒の表示順に足す（PieceMoveAnimator と同じ）

    private BoardCellView cellView;
    private BoardCellView CellView => cellView != null ? cellView : (cellView = FindAnyObjectByType<BoardCellView>());

    // ------------------------------------------------------------
    //  天変地異
    // ------------------------------------------------------------

    // 飛んでいく 1 マス分（駒と、炎上・落とし穴の表示）
    private class Flyer
    {
        public GamePiece piece;
        public Transform body;        // 駒（なければ null）
        public Transform token;       // 炎上・落とし穴の表示（BoardCellView のマスの表示。なければ null）
        public Vector3 from, to;      // 盤のローカル座標
        public float z;               // 駒の元の z
        public float baseRot;         // 駒の元の向き（度）
        public Vector3 baseScale;
        public float delay;           // 浮き始めるのを少しずつずらす
        public float tilt;            // 浮いている間の傾き（度）
        public float spin;            // 飛ぶ間の回転（度。±1 回転）
        public SpriteRenderer[] renderers;
        public int[] orders;
    }

    // 天変地異：moves は GameSystem.ShuffleCells の結果（盤面はもう入れ替わっている）。
    // 駒とマスの状態の表示を元のマスに置き直してから、time 秒後に飛ばし始める
    public void Cataclysm(List<CellMove> moves, float time)
    {
        List<Flyer> flyers = new List<Flyer>();
        BoardCellView view = CellView;
        foreach (CellMove m in moves)
        {
            Transform token = (m.Burning || m.Trap) && view != null ? view.VisualRoot(m.To.x, m.To.y) : null;
            if (m.Piece == null && token == null) continue;

            Vector2 f = GameHelper.CalcPanelLocation(m.From.x, m.From.y);
            Vector2 t = GameHelper.CalcPanelLocation(m.To.x, m.To.y);
            Flyer fl = new Flyer
            {
                piece = m.Piece,
                token = token,
                from = new Vector3(f.x, f.y, 0f),
                to = new Vector3(t.x, t.y, 0f),
                delay = Random.Range(0f, 0.15f),
                tilt = Random.Range(-0.35f, 0.35f) * Mathf.Rad2Deg,
                spin = (Random.value < 0.5f ? -1f : 1f) * 360f,
            };
            if (m.Piece != null)
            {
                Transform body = m.Piece.transform;
                DOTween.Kill(body, true); // 前の移動の演出が残っていれば終わらせる（位置・大きさ・表示順が元に戻る）
                fl.body = body;
                fl.z = body.localPosition.z;
                fl.baseRot = body.localEulerAngles.z;
                fl.baseScale = body.localScale;
                body.localPosition = new Vector3(f.x, f.y, fl.z);
            }
            // マスの状態の表示は、データに合わせて新しいマスへ付け替わっているので、飛ぶまでは元のマスに戻しておく
            if (token != null) token.localPosition = new Vector3(f.x, f.y, 0f);
            flyers.Add(fl);
        }
        At(time, () => PlayCataclysm(flyers));
    }

    void PlayCataclysm(List<Flyer> flyers)
    {
        Vector3 mid = CellWorld(4, 4);
        Vector3 up = gameBoard != null ? gameBoard.transform.InverseTransformDirection(ScreenUp) : ScreenUp;
        up.z = 0f;
        up.Normalize();

        // 飛んでいる駒は一番手前に出す
        foreach (Flyer fl in flyers)
        {
            if (fl.body == null) continue;
            fl.renderers = fl.body.GetComponentsInChildren<SpriteRenderer>(true);
            fl.orders = new int[fl.renderers.Length];
            for (int i = 0; i < fl.renderers.Length; i++)
            {
                fl.orders[i] = fl.renderers[i].sortingOrder;
                fl.renderers[i].sortingOrder += FlyingOrderOffset;
            }
        }

        // 地鳴りの始まり：盤の中央に魔法陣と衝撃波
        MagicCircle(mid, CataclysmColor, 3f);
        fx.Shockwave(mid, CataclysmColor, 12f, 0.9f);

        float last = 0f;
        bool lifted = false;
        DOVirtual.Float(0f, CataclysmTime, CataclysmTime, t =>
        {
            float dt = Mathf.Max(0f, t - last);
            last = t;

            // 地鳴り：着地まで画面全体が揺れ続け、だんだん強くなる
            fx.Shake(Mathf.Lerp(3f, 9f, t / CataclysmTime), 0.15f);
            if (t < QuakeLead && Random.value < dt * 16f)
            {
                Dust(CellWorld(Random.Range(0, BoardCells.Size), Random.Range(0, BoardCells.Size)));
            }

            // 宙に浮き始める
            if (!lifted && t >= QuakeLead)
            {
                lifted = true;
                if (newPieceFx != null) newPieceFx.Flash(CataclysmColor, 0.3f, 0.4f);
                foreach (Flyer fl in flyers) Dust(BoardWorld(fl.from));
            }

            foreach (Flyer fl in flyers) PlaceFlyer(fl, t, up);
        }).SetEase(Ease.Linear).SetLink(gameObject).OnComplete(() => LandCataclysm(flyers, mid));
    }

    // 時刻 t（地鳴りの始まりから）の、駒・マスの状態の位置・大きさ・向き
    void PlaceFlyer(Flyer fl, float t, Vector3 up)
    {
        // 浮く（power2.out）→ 飛ぶ（power2.inOut。放物線を描きながら 1 回転）→ 降りる（power3.in）
        float a = EaseOutQuad(Mathf.Clamp01((t - QuakeLead - fl.delay) / LiftTime));
        float lift = a, s = 1f + 0.2f * a, r = fl.tilt * a;
        float k = t >= FlyStart ? EaseInOutQuad(Mathf.Clamp01((t - FlyStart) / (DropStart - FlyStart))) : 0f;
        if (t >= DropStart)
        {
            float m = 1f - EaseInCubic(Mathf.Clamp01((t - DropStart) / (CataclysmTime - DropStart)));
            lift = m;
            s = 1f + 0.2f * m;
            r = fl.tilt * m;
        }
        Vector3 pos = Vector3.LerpUnclamped(fl.from, fl.to, k) + up * ((lift * LiftPx + Mathf.Sin(Mathf.PI * k) * ArcPx) * PX);

        if (fl.body != null)
        {
            fl.body.localPosition = new Vector3(pos.x, pos.y, fl.z);
            fl.body.localScale = fl.baseScale * s;
            fl.body.localRotation = Quaternion.Euler(0f, 0f, fl.baseRot + r + fl.spin * k);
            if (k > 0f && k < 1f && Random.value < 0.2f)
            {
                PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
                o.color = Random.value < 0.5f ? CataclysmColor : Color.white;
                o.life = 0.35f;
                o.size = Random.Range(0.1f, 0.2f);
                fx.Particle(fl.body.position + new Vector3(Random.Range(-12f, 12f), Random.Range(-12f, 12f), 0f) * PX, o);
            }
        }
        if (fl.token != null)
        {
            // 炎上の火の粉は BoardCellView が表示の位置から出し続けるので、ここでは動かすだけ
            fl.token.localPosition = new Vector3(pos.x, pos.y, 0f);
            fl.token.localScale = Vector3.one * s;
        }
    }

    // 着地：大きな揺れ・閃光・衝撃波・土ぼこり・「天変地異！」。駒とマスの表示を新しいマスにぴったり置き直す
    void LandCataclysm(List<Flyer> flyers, Vector3 mid)
    {
        fx.Shake(20f, 0.6f);
        if (newPieceFx != null) newPieceFx.Flash(Color.white, 0.45f, 0.3f);
        fx.Shockwave(mid, CataclysmColor, 12f, 0.6f);
        foreach (Flyer fl in flyers)
        {
            Dust(BoardWorld(fl.to));
            if (fl.body == null) continue;
            GamePiece p = fl.piece;
            Vector2 loc = p != null ? GameHelper.CalcPanelLocation(p.X, p.Y) : (Vector2)fl.to;
            fl.body.localPosition = new Vector3(loc.x, loc.y, fl.z);
            fl.body.localScale = fl.baseScale;
            fl.body.localRotation = Quaternion.Euler(0f, 0f, fl.baseRot);
            for (int i = 0; i < fl.renderers.Length; i++)
            {
                if (fl.renderers[i] != null) fl.renderers[i].sortingOrder = fl.orders[i];
            }
        }
        if (CellView != null) CellView.SnapVisuals();
        PopText("天変地異！", mid, 72f, Color.white, Hex(0xff6d00), Hex(0x2a0a00), 40f, 1.1f);
    }

    // 土ぼこり（駒・マスの少し下から）
    void Dust(Vector3 pos)
    {
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 6; burst.colors = DustColors; burst.speed = 90f * PX; burst.life = 0.6f; burst.size = 0.25f; burst.alpha = 0.6f;
        fx.Burst(pos - ScreenUp * (22f * PX), burst);
    }

    static float EaseOutQuad(float x) => 1f - (1f - x) * (1f - x);
    static float EaseInCubic(float x) => x * x * x;
    static float EaseInOutQuad(float x) => x < 0.5f ? 2f * x * x : 1f - Mathf.Pow(-2f * x + 2f, 2f) / 2f;

    // ------------------------------------------------------------
    //  世界の終わり：画面が赤黒く暗くなり、盤の中央から画面の外までひび割れが走る（Prototype の fx.doom）
    // ------------------------------------------------------------

    public void WorldEndDoom(float time)
    {
        At(time, PlayDoom);
    }

    void PlayDoom()
    {
        Camera c = cam != null ? cam : Camera.main;
        float halfH = c != null && c.orthographic ? c.orthographicSize : 5f;
        float halfW = halfH * (c != null ? c.aspect : 16f / 9f);
        Vector3 center = CellWorld(4, 4);
        Vector3 camPos = c != null ? c.transform.position : center;

        // 画面全体を覆う（揺れてもはみ出さないよう大きめに）。カメラの向きに合わせる
        Transform root = new GameObject("WorldEndDoom").transform;
        root.position = new Vector3(camPos.x, camPos.y, center.z - 0.4f);
        root.rotation = Quaternion.Euler(0f, 0f, c != null ? c.transform.eulerAngles.z : 0f);

        Color darkColor = Hex(0x120000), glowColor = Hex(0xb71c1c);
        SpriteRenderer dark = CreateChild(root, MagicSprites.Square, WithAlpha(darkColor, 0f), DoomOrder);
        dark.transform.localScale = new Vector3(halfW * 2f + 6f, halfH * 2f + 6f, 1f);
        // 中心の赤い滲み（外側ほど黒い）
        SpriteRenderer glow = CreateChild(root, MagicSprites.Glow, WithAlpha(glowColor, 0f), DoomOrder + 1);
        glow.transform.position = new Vector3(center.x, center.y, root.position.z);
        glow.transform.localScale = Vector3.one * 16f;

        // ひび（画面と同じくらいの大きさの画像に、伸びた分だけ描き足していく）
        int texW = Mathf.Min(1024, Mathf.CeilToInt((halfW * 2f + 2f) / PX / CrackCanvas.PxPerTexel));
        int texH = Mathf.Min(1024, Mathf.CeilToInt((halfH * 2f + 2f) / PX / CrackCanvas.PxPerTexel));
        CrackCanvas cracks = new CrackCanvas(texW, texH);
        const int count = 11;
        for (int i = 0; i < count; i++)
        {
            cracks.Grow(Vector2.zero, (float)i / count * Mathf.PI * 2f + Random.Range(-0.2f, 0.2f), Random.Range(650f, 950f), Random.Range(2.5f, 4f), 0f, 0);
        }
        SpriteRenderer crackSr = CreateChild(root, cracks.Sprite, Color.white, DoomOrder + 2);
        crackSr.transform.position = new Vector3(center.x, center.y, root.position.z);

        float darkA = 0f, glowA = 0f, crackA = 1f, all = 1f;
        System.Action apply = () =>
        {
            if (dark != null) dark.color = WithAlpha(darkColor, darkA * all);
            if (glow != null) glow.color = WithAlpha(glowColor, glowA * all);
            if (crackSr != null) crackSr.color = WithAlpha(Color.white, crackA * all);
        };

        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Insert(0f, DOVirtual.Float(0f, 0.82f, 0.4f, a => { darkA = a; apply(); }).SetEase(Ease.OutQuad));
        seq.Insert(0.1f, DOVirtual.Float(0f, 0.55f, 0.6f, a => { glowA = a; apply(); }));
        seq.InsertCallback(0.35f, () =>
        {
            fx.Shake(16f, 1.4f);
            if (newPieceFx != null) newPieceFx.Flash(DangerColor, 0.25f, 0.3f);
        });
        seq.Insert(0.35f, DOVirtual.Float(0f, 1000f, 0.9f, r => cracks.Reveal(r)).SetEase(Ease.OutQuad));
        seq.InsertCallback(0.9f, () => PopText("世界が崩れ始める……", center, 46f, Color.white, DangerColor, Hex(0x1a0000), 20f, 1.2f));
        // ひびが脈打つ
        seq.Insert(1.25f, DOVirtual.Float(1f, 0.5f, 0.12f, a => { crackA = a; apply(); }).SetLoops(4, LoopType.Yoyo));
        // 暗幕が晴れる（このあと WorldEndView が背景をうっすら赤く染める）
        seq.Insert(2.0f, DOVirtual.Float(1f, 0f, 0.6f, a => { all = a; apply(); }).SetEase(Ease.OutSine));
        seq.OnKill(() =>
        {
            cracks.Dispose();
            if (root != null) Destroy(root.gameObject);
        });
    }

    // ------------------------------------------------------------
    //  世界の終わりの崩壊
    // ------------------------------------------------------------

    // 崩れるマスが赤く点滅する（3 回光って、光ったまま CollapseWarningTime 後に崩れ始める）。「世界が崩れる……」
    public void CollapseWarning(List<Vector2Int> cells, float time)
    {
        List<Vector2Int> area = new List<Vector2Int>(cells);
        At(time, () =>
        {
            Vector3 pos = CellWorld(4, 4) + ScreenUp * 1.2f;
            PopText("世界が崩れる……", pos, 36f, Color.white, DangerColor, Hex(0x1a0000), 10f, 1f);
            DangerFlash(area);
        });
    }

    // 崩れ終わった：揺れ
    public void Collapsed(float time)
    {
        At(time, () => fx.Shake(10f, 0.4f));
    }

    // 危険の予兆（Prototype の fx.dangerFlash）：マスを赤く塗り、暗い赤の枠で囲んで、数回点滅させる
    void DangerFlash(List<Vector2Int> cells)
    {
        Color edgeColor = Hex(0x7f0000);
        Transform root = AreaCells(cells, DangerColor, 0.7f, out SpriteRenderer[] fills);
        List<SpriteRenderer> parts = new List<SpriteRenderer>(fills);
        List<Color> colors = new List<Color>();
        foreach (SpriteRenderer sr in fills) colors.Add(sr.color);

        const float line = 3f * PX;
        float inner = 1f - 4f * PX; // 枠は内側に 2px 寄せる
        foreach (Vector2Int c in cells)
        {
            Vector2 p = GameHelper.CalcPanelLocation(c.x, c.y);
            Vector3[] pos =
            {
                new Vector3(p.x, p.y + inner * 0.5f, -0.001f), new Vector3(p.x, p.y - inner * 0.5f, -0.001f),
                new Vector3(p.x + inner * 0.5f, p.y, -0.001f), new Vector3(p.x - inner * 0.5f, p.y, -0.001f),
            };
            Vector3[] scale =
            {
                new Vector3(inner, line, 1f), new Vector3(inner, line, 1f),
                new Vector3(line, inner, 1f), new Vector3(line, inner, 1f),
            };
            for (int i = 0; i < 4; i++)
            {
                SpriteRenderer e = CreateChild(root, MagicSprites.Square, edgeColor, fxOrder);
                e.transform.localPosition = pos[i];
                e.transform.localScale = scale[i];
                parts.Add(e);
                colors.Add(edgeColor);
            }
        }
        System.Action<float> setAlpha = a =>
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null) parts[i].color = WithAlpha(colors[i], colors[i].a * a);
            }
        };
        setAlpha(0f);
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(0f, 1f, CollapseWarningTime / 5f, v => setAlpha(v)).SetEase(Ease.Linear).SetLoops(5, LoopType.Yoyo));
        seq.Append(DOVirtual.Float(1f, 0f, 0.3f, v => setAlpha(v)));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // ------------------------------------------------------------
    //  ひび割れの画像（Prototype の doom のひびを、画像に描き足していく）
    //  中心からのギザギザの折れ線（ところどころ枝分かれ）を、外側の赤いぼかし・橙・明るい芯の 3 層で描く
    // ------------------------------------------------------------
    private class CrackCanvas : System.IDisposable
    {
        public const float PxPerTexel = 2f; // 1 テクセル = Prototype の 2px

        private class Crack
        {
            public readonly List<Vector3> pts = new List<Vector3>(); // x, y（px。中心からの位置）、z = 根元からの道のり
            public float width;   // 線の太さ（px）
            public float d0;      // 中心から割れが届くまでの道のり（ここまで届くと伸び始める）
            public float drawn;   // 描き終わった道のり
        }

        // 3 層（太さの倍率・色・不透明度）
        private static readonly float[] LayerWidth = { 4f, 1.6f, 0.6f };
        private static readonly Color[] LayerColor = { Hex(0xff1744), Hex(0xff6d00), Hex(0xffe0b2) };
        private static readonly float[] LayerAlpha = { 0.25f, 0.85f, 1f };

        private readonly int w, h;
        private readonly Color[] layerColor; // 層ごとの色（既定は LayerColor。時間停止のガラスのひびは白）
        private readonly float[][] cover;   // 層ごとの塗りの濃さ（0〜1）
        private readonly Color32[] pixels;
        private readonly List<Crack> cracks = new List<Crack>();
        private readonly Texture2D texture;
        public Sprite Sprite { get; }

        public CrackCanvas(int width, int height, Color[] colors = null)
        {
            w = Mathf.Max(2, width);
            h = Mathf.Max(2, height);
            layerColor = colors != null && colors.Length == 3 ? colors : LayerColor;
            cover = new float[3][];
            for (int i = 0; i < 3; i++) cover[i] = new float[w * h];
            pixels = new Color32[w * h];
            texture = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            texture.SetPixels32(pixels);
            texture.Apply();
            Sprite = Sprite.Create(texture, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 1f / (PxPerTexel * PX));
        }

        // (start) から向き ang へ長さ len のひびを作る（Prototype の grow。枝分かれは深さ 2 まで）
        public void Grow(Vector2 start, float ang, float len, float width, float d0, int depth)
        {
            Crack k = new Crack { width = width, d0 = d0 };
            k.pts.Add(new Vector3(start.x, start.y, 0f));
            Vector2 p = start;
            float d = 0f;
            while (d < len)
            {
                float a = ang + Random.Range(-0.55f, 0.55f);
                float s = Random.Range(16f, 40f);
                p += new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s;
                d += s;
                k.pts.Add(new Vector3(p.x, p.y, d));
                if (depth < 2 && Random.value < 0.12f)
                {
                    Grow(p, ang + (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.5f, 1.1f), (len - d) * Random.Range(0.3f, 0.6f), width * 0.6f, d0 + d, depth + 1);
                }
            }
            cracks.Add(k);
        }

        // 折れ線のひびを足す（pts は中心からの位置（px）。根元 pts[0] まで割れが届いてから、d0 + 道のりの順に伸びる）
        public void AddPolyline(List<Vector2> pts, float width, float d0)
        {
            if (pts == null || pts.Count < 2) return;
            Crack k = new Crack { width = width, d0 = d0 };
            float d = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) d += Vector2.Distance(pts[i - 1], pts[i]);
                k.pts.Add(new Vector3(pts[i].x, pts[i].y, d));
            }
            cracks.Add(k);
        }

        // 割れが中心から reach（px）まで届いた：まだ描いていない分を描き足す
        public void Reveal(float reach)
        {
            if (texture == null) return;
            int x0 = w, y0 = h, x1 = -1, y1 = -1;
            foreach (Crack k in cracks)
            {
                float target = reach - k.d0;
                if (target <= k.drawn) continue;
                for (int i = 1; i < k.pts.Count; i++)
                {
                    Vector3 a = k.pts[i - 1], b = k.pts[i];
                    if (b.z <= k.drawn) continue;
                    if (a.z >= target) break;
                    float ta = Mathf.InverseLerp(a.z, b.z, Mathf.Max(a.z, k.drawn));
                    float tb = Mathf.InverseLerp(a.z, b.z, Mathf.Min(b.z, target));
                    Vector2 pa = Vector2.Lerp(a, b, ta), pb = Vector2.Lerp(a, b, tb);
                    DrawSegment(pa, pb, k.width, ref x0, ref y0, ref x1, ref y1);
                }
                k.drawn = target;
            }
            if (x1 < x0 || y1 < y0) return;

            // 描き足した範囲だけ色を作り直す（赤いぼかし → 橙 → 芯の順に重ねる）
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int i = y * w + x;
                    Color c = new Color(0f, 0f, 0f, 0f);
                    for (int l = 0; l < 3; l++) c = OverColor(c, layerColor[l], LayerAlpha[l] * cover[l][i]);
                    pixels[i] = c;
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply();
        }

        // 線分 a-b（px）を 3 層の太さで塗る（濃い方を残す）。塗った範囲を広げる
        void DrawSegment(Vector2 a, Vector2 b, float widthPx, ref int x0, ref int y0, ref int x1, ref int y1)
        {
            Vector2 ta = ToTexel(a), tb = ToTexel(b);
            float maxHalf = widthPx * LayerWidth[0] * 0.5f / PxPerTexel;
            int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.x, tb.x) - maxHalf - 1f));
            int maxX = Mathf.Min(w - 1, Mathf.CeilToInt(Mathf.Max(ta.x, tb.x) + maxHalf + 1f));
            int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(ta.y, tb.y) - maxHalf - 1f));
            int maxY = Mathf.Min(h - 1, Mathf.CeilToInt(Mathf.Max(ta.y, tb.y) + maxHalf + 1f));
            if (minX > maxX || minY > maxY) return;

            float[] half = new float[3];
            for (int l = 0; l < 3; l++) half[l] = Mathf.Max(0.35f, widthPx * LayerWidth[l] * 0.5f / PxPerTexel);
            Vector2 ab = tb - ta;
            float len2 = Mathf.Max(1e-6f, ab.sqrMagnitude);
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(p - ta, ab) / len2);
                    float d = Vector2.Distance(p, ta + ab * t);
                    int i = y * w + x;
                    for (int l = 0; l < 3; l++)
                    {
                        float cov = Mathf.Clamp01(half[l] - d + 0.5f);
                        if (cov > cover[l][i]) cover[l][i] = cov;
                    }
                }
            }
            x0 = Mathf.Min(x0, minX);
            y0 = Mathf.Min(y0, minY);
            x1 = Mathf.Max(x1, maxX);
            y1 = Mathf.Max(y1, maxY);
        }

        Vector2 ToTexel(Vector2 px)
        {
            return new Vector2(px.x / PxPerTexel + w * 0.5f, px.y / PxPerTexel + h * 0.5f);
        }

        static Color OverColor(Color under, Color over, float alpha)
        {
            if (alpha <= 0f) return under;
            float outA = alpha + under.a * (1f - alpha);
            if (outA <= 0f) return new Color(0f, 0f, 0f, 0f);
            Color c = (over * alpha + under * (under.a * (1f - alpha))) / outA;
            c.a = outA;
            return c;
        }

        public void Dispose()
        {
            if (Sprite != null) Object.Destroy(Sprite);
            if (texture != null) Object.Destroy(texture);
        }
    }
}
