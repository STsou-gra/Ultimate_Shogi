using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

// マスの状態の見た目（プロトタイプ src/game/Game.js の renderCells / drawHole / drawBurning / burnLabel / createTrapView と、
// src/view/events.js の cellBroken / cellBurning / trapClosed / fireOut の移植）
//  破壊   … 盤が抜け落ちたような暗い穴。破壊した瞬間に、そのマスの盤の絵が回りながら奥へ落ちていく
//  炎上   … 焦げ跡＋揺らめく炎＋立ち上る火の粉、右上に残りターン数
//  落とし穴 … 暗い渦巻き＋仕掛けた人の色の縁取り（渦は回り続ける）
// GameBoard が自動で追加する（シーンの編集は不要）。毎フレーム BoardCells の状態に表示を合わせる
public class BoardCellView : MonoBehaviour
{
    // プロトタイプの1マス(66px)を Unity の1マス(1unit)に合わせる
    private const float PX = 1f / 66f;
    private const int TEX = 128; // 1マスぶんのテクスチャの大きさ

    [Header("Sorting (盤 0 / 移動先パネル 4 / カーソル 5 / 駒 10)")]
    [SerializeField] private int orderBase = 1;   // 穴・焦げ跡・落とし穴
    [SerializeField] private int orderFire = 2;   // 炎
    [SerializeField] private int orderSwirl = 3;  // 落とし穴の渦・落ちていく盤の絵
    [SerializeField] private int orderLabel = 12; // 炎上の残りターン数（炎上マスに立っている駒より手前）
    [SerializeField] private int panelOrder = 4;  // 移動先パネル（GameSelectPanel）を炎上・落とし穴の上に出す

    [Header("Colors")]
    [SerializeField] private Color player1Color = Hex(0x4fc3ff);
    [SerializeField] private Color player2Color = Hex(0xff5277);

    [Header("Burning")]
    [SerializeField] private float fireParticleRate = 9f;   // 1マスあたり1秒に出す火の粉の数
    [SerializeField] private float labelFontSize = 3.2f;     // 残りターン数の文字の大きさ（17px 相当）

    [Header("Trap")]
    [SerializeField] private float swirlSpeed = 2.2f;        // 渦の回転（rad/秒）

    private static readonly Color[] FireColors = { Hex(0xff6d00), Hex(0xffab00), Hex(0xff3d00), Hex(0xffd54a) };
    private static readonly Color[] FireBurstColors = { Hex(0xff6d00), Hex(0xffab00), Hex(0xff3d00), Hex(0xffea00) };
    private static readonly Color[] DebrisColors = { Hex(0x8d6e63), Hex(0x5d4037), Hex(0x3e2723), Hex(0xe6bd74) };
    private static readonly Color[] SmokeColors = { Hex(0x9e9e9e), Hex(0x616161), Hex(0x424242) };
    private static readonly Color TrapColor = Hex(0x7e57c2);
    private static readonly Color BreakColor = Hex(0x8d6e63);
    private static readonly Color TileFallbackColor = Hex(0xe6bd74);

    // 1マスぶんの表示
    private class CellVisual
    {
        public Transform root;        // マスの中心（盤のローカル座標）
        public Transform screen;      // 画面の向きにそろえる子（カメラが180°回っても炎が上へ伸びる）
        public SpriteRenderer hole;
        public SpriteRenderer scorch;
        public SpriteRenderer fire;
        public SpriteRenderer pitFill;
        public SpriteRenderer pitRim;
        public SpriteRenderer swirl;
        public TextMeshPro label;
        public int heldTrap;          // 駒が落ちていく間は、データから消えても落とし穴を描き続ける
        public PlayerType trapOwner;
    }

    private GameBoard board;
    private GameSystem gameSystem;
    private PieceFx fx;
    private Camera cam;
    private readonly CellVisual[] visuals = new CellVisual[BoardCells.Size * BoardCells.Size];

    private Sprite[] holeSprites;
    private Sprite squareSprite;
    private Sprite fireSprite;
    private Sprite pitFillSprite;
    private Sprite pitRimSprite;
    private Sprite swirlSprite;
    private SpriteRenderer boardImage;

    private float time;

    public void Init(GameBoard owner)
    {
        board = owner;
        gameSystem = FindAnyObjectByType<GameSystem>();
        fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        cam = Camera.main;

        GameObject boardObj = GameObject.Find("BoardImage");
        if (boardObj != null) boardImage = boardObj.GetComponent<SpriteRenderer>();

        CreateSprites();
        for (int i = 0; i < visuals.Length; i++)
        {
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);
            visuals[i] = CreateCellVisual(x, y);
        }

        // 移動先パネルを炎上・落とし穴のマスの上に出す
        foreach (GameSelectPanel panel in board.GetComponentsInChildren<GameSelectPanel>(true))
        {
            foreach (SpriteRenderer sr in panel.GetComponentsInChildren<SpriteRenderer>(true)) sr.sortingOrder = panelOrder;
        }
    }

    void LateUpdate()
    {
        if (board == null) return;
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        time += dt;
        if (cam == null) cam = Camera.main;
        Quaternion screenRot = cam != null ? Quaternion.Inverse(board.transform.rotation) * cam.transform.rotation : Quaternion.identity;
        int turn = gameSystem != null ? gameSystem.TurnNumber : 1;

        // 炎の揺らめき（全マス共通）
        float fireAlpha = 0.75f + 0.15f * Mathf.Sin(time * 11f) + 0.1f * Mathf.Sin(time * 17f);

        BoardCells cells = board.Cells;
        for (int i = 0; i < visuals.Length; i++)
        {
            CellVisual v = visuals[i];
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);
            CellData c = cells.Get(x, y);

            v.screen.localRotation = screenRot;

            bool broken = c.IsBroken;
            v.hole.enabled = broken;

            bool burning = c.IsBurning;
            v.scorch.enabled = burning;
            v.fire.enabled = burning;
            v.label.enabled = burning;
            if (burning)
            {
                v.fire.color = new Color(1f, 1f, 1f, fireAlpha);
                string text = cells.BurnRemaining(x, y, turn).ToString();
                if (v.label.text != text) v.label.text = text;
                if (Random.value < dt * fireParticleRate) SpawnFireParticle(v.root.position);
            }

            bool trap = c.HasTrap || v.heldTrap > 0;
            if (c.HasTrap) v.trapOwner = c.trap.Value;
            v.pitFill.enabled = trap;
            v.pitRim.enabled = trap;
            v.swirl.enabled = trap;
            if (trap)
            {
                Color rim = OwnerColor(v.trapOwner);
                v.pitRim.color = rim;
                v.swirl.transform.localRotation = Quaternion.Euler(0f, 0f, time * swirlSpeed * Mathf.Rad2Deg);
            }
        }
    }

    // ------------------------------------------------------------
    //  マスの状態が変わったときの演出
    // ------------------------------------------------------------

    /// <summary>マスの破壊：そのマスの盤の絵が回りながら奥へ落ちて、穴が開く</summary>
    public void PlayBreak(int x, int y)
    {
        CellVisual v = VisualAt(x, y);
        if (v == null) return;
        Vector3 c = v.root.position;

        // 落ちていく盤の絵（盤の画像から切り出せなければ木の色の四角）
        GameObject tileObj = new GameObject("FallingTile");
        tileObj.transform.SetParent(v.root, false);
        SpriteRenderer tile = tileObj.AddComponent<SpriteRenderer>();
        tile.sortingOrder = orderSwirl;
        Sprite cut = CutBoardSprite(c);
        if (cut != null) tile.sprite = cut;
        else { tile.sprite = squareSprite; tile.color = TileFallbackColor; }
        Color tileColor = tile.color;

        // 穴は落ちていく盤の絵の下に見える（LateUpdate で表示する）

        Vector3 down = -(Vector3)ScreenUp() * 0.15f;
        float spin = (Random.value < 0.5f ? -1f : 1f) * Random.Range(25f, 60f);
        DOTween.Sequence()
            .Join(tileObj.transform.DOScale(0.15f, 0.55f).SetEase(Ease.InQuad))
            .Join(tileObj.transform.DOLocalRotate(new Vector3(0f, 0f, spin), 0.55f).SetEase(Ease.InQuad))
            .Join(tileObj.transform.DOMove(c + down, 0.55f).SetEase(Ease.InQuad))
            .Join(DOVirtual.Float(0f, 1f, 0.55f, k =>
            {
                // 奥へ落ちるほど暗く、最後は見えなくなる
                if (tile == null) return;
                float m = Mathf.Lerp(1f, 0.25f, k);
                tile.color = new Color(tileColor.r * m, tileColor.g * m, tileColor.b * m, 1f - k * k);
            }))
            .OnComplete(() => Destroy(tileObj))
            .SetLink(tileObj);

        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 22; burst.colors = DebrisColors; burst.speed = 220f * PX; burst.life = 0.8f; burst.size = 0.28f; burst.gravity = 420f * PX;
        fx.Burst(c, burst);
        fx.Shockwave(c, BreakColor, 1.8f, 0.4f);
        fx.Shake(5f, 0.2f);
    }

    /// <summary>炎上の開始：火の粉がはじける</summary>
    public void PlayBurning(int x, int y)
    {
        CellVisual v = VisualAt(x, y);
        if (v == null) return;
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 18; burst.colors = FireBurstColors; burst.speed = 160f * PX; burst.life = 0.6f; burst.size = 0.25f; burst.gravity = -120f * PX;
        fx.Burst(v.root.position, burst);
    }

    /// <summary>落とし穴の設置</summary>
    public void PlayTrapSet(int x, int y)
    {
        CellVisual v = VisualAt(x, y);
        if (v == null) return;
        Vector3 c = v.root.position;
        v.pitFill.transform.localScale = Vector3.one * 0.2f;
        v.pitFill.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetLink(v.root.gameObject);
        fx.Shockwave(c, TrapColor, 1.2f, 0.35f);
    }

    /// <summary>ターンの終わりに鎮火したマス：煙が立ち上る</summary>
    public void PlayFireOut(List<Vector2Int> ended)
    {
        if (ended == null) return;
        foreach (Vector2Int p in ended)
        {
            CellVisual v = VisualAt(p.x, p.y);
            if (v == null) continue;
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 12; burst.colors = SmokeColors; burst.speed = 60f * PX; burst.life = 1.2f; burst.size = 0.5f;
            burst.gravity = -50f * PX; burst.grow = 1f; burst.shrink = false; burst.alpha = 0.5f; burst.drag = 0.97f;
            fx.Burst(v.root.position, burst);
        }
    }

    /// <summary>
    /// 駒の消失の演出。盤のデータからはもう外れている駒を、移動のアニメーションが終わってから消して破棄する。
    /// reason は GameBoard.Reason〜（落とし穴なら穴に吸い込まれ、穴が閉じる）
    /// </summary>
    public void PlayVanish(GamePiece piece, string reason, int x, int y)
    {
        if (piece == null) return;
        CellVisual v = VisualAt(x, y);
        bool fall = reason == GameBoard.ReasonTrap;
        if (fall && v != null) v.heldTrap++; // データから消えても、吸い込み終わるまで直前の持ち主の色で描く
        StartCoroutine(VanishRoutine(piece, reason, v));
    }

    IEnumerator VanishRoutine(GamePiece piece, string reason, CellVisual v)
    {
        // 移動のアニメーション（PieceMoveAnimator は駒の Transform を id にしている）が終わるまで待つ
        while (piece != null && DOTween.IsTweening(piece.transform)) yield return null;
        if (piece == null) { ReleaseTrap(v, false); yield break; }

        Transform t = piece.transform;
        Vector3 center = v != null ? v.root.position : t.position;
        SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++) baseColors[i] = renderers[i].color;
        Vector3 baseScale = t.localScale;

        Sequence seq = DOTween.Sequence().SetLink(piece.gameObject);
        if (reason == GameBoard.ReasonTrap)
        {
            // 落とし穴：回りながら穴の中心へ吸い込まれる
            seq.Join(t.DOMove(new Vector3(center.x, center.y, t.position.z), 0.6f).SetEase(Ease.InQuad))
               .Join(t.DOScale(baseScale * 0.05f, 0.6f).SetEase(Ease.InBack))
               .Join(t.DORotate(new Vector3(0f, 0f, t.eulerAngles.z + 540f), 0.6f, RotateMode.FastBeyond360).SetEase(Ease.InQuad))
               .Join(FadeTween(renderers, baseColors, 0.6f, 0.2f));
        }
        else if (reason == GameBoard.ReasonBurning)
        {
            // 炎上：燃え上がって黒く焦げながら消える
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 20; burst.colors = FireBurstColors; burst.speed = 140f * PX; burst.life = 0.7f; burst.size = 0.3f; burst.gravity = -160f * PX;
            fx.Burst(t.position, burst);
            seq.Join(t.DOScale(baseScale * 1.15f, 0.45f).SetEase(Ease.OutQuad))
               .Join(FadeTween(renderers, baseColors, 0.45f, 0.15f));
        }
        else
        {
            // マス破壊：盤と一緒に奥へ落ちる
            seq.Join(t.DOScale(baseScale * 0.15f, 0.55f).SetEase(Ease.InQuad))
               .Join(t.DOMove(t.position - (Vector3)ScreenUp() * 0.15f, 0.55f).SetEase(Ease.InQuad))
               .Join(FadeTween(renderers, baseColors, 0.55f, 0.6f));
        }

        yield return seq.WaitForCompletion();
        if (piece != null) Destroy(piece.gameObject);
        ReleaseTrap(v, reason == GameBoard.ReasonTrap);
    }

    // 色を暗く（darken: 1 なら元の色のまま）しながら透明にする
    Tween FadeTween(SpriteRenderer[] renderers, Color[] baseColors, float duration, float darken)
    {
        return DOVirtual.Float(0f, 1f, duration, k =>
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null) continue;
                Color b = baseColors[i];
                float m = Mathf.Lerp(1f, darken, k);
                renderers[i].color = new Color(b.r * m, b.g * m, b.b * m, b.a * (1f - k));
            }
        }).SetEase(Ease.InQuad);
    }

    // 吸い込み終わってから落とし穴を閉じる
    void ReleaseTrap(CellVisual v, bool playClose)
    {
        if (v == null || v.heldTrap <= 0) return;
        v.heldTrap--;
        if (!playClose) return;
        fx.Shockwave(v.root.position, TrapColor, 1.4f, 0.35f);
        fx.Shake(4f, 0.2f);
    }

    // ------------------------------------------------------------
    //  表示の組み立て
    // ------------------------------------------------------------

    CellVisual CreateCellVisual(int x, int y)
    {
        CellVisual v = new CellVisual();
        GameObject root = new GameObject($"Cell_{x}_{y}");
        root.transform.SetParent(board.transform, false);
        root.transform.localPosition = GameHelper.CalcPanelLocation(x, y);
        v.root = root.transform;

        GameObject screen = new GameObject("Screen");
        screen.transform.SetParent(v.root, false);
        v.screen = screen.transform;

        int variant = (x * 7 + y * 13) % holeSprites.Length;
        v.hole = CreateRenderer("Hole", v.root, holeSprites[variant], orderBase, Color.white);
        v.scorch = CreateRenderer("Scorch", v.root, squareSprite, orderBase, new Color(0x3e / 255f, 0x15 / 255f, 0f, 0.45f));
        v.scorch.transform.localScale = Vector3.one * (64f / 66f);
        v.fire = CreateRenderer("Fire", v.screen, fireSprite, orderFire, Color.white);
        v.pitFill = CreateRenderer("TrapPit", v.root, pitFillSprite, orderBase, Color.white);
        v.pitRim = CreateRenderer("TrapRim", v.pitFill.transform, pitRimSprite, orderBase, Color.white);
        v.swirl = CreateRenderer("TrapSwirl", v.pitFill.transform, swirlSprite, orderSwirl, new Color(TrapColor.r, TrapColor.g, TrapColor.b, 0.85f));

        // 残りターン数（マスの右上。画面の向きにそろえる）
        GameObject labelObj = new GameObject("BurnLabel");
        labelObj.transform.SetParent(v.screen, false);
        TextMeshPro label = labelObj.AddComponent<TextMeshPro>();
        label.rectTransform.sizeDelta = new Vector2(0.6f, 0.4f);
        label.rectTransform.pivot = new Vector2(1f, 1f);
        label.rectTransform.localPosition = new Vector3(0.5f - 3f * PX, 0.5f - 1f * PX, 0f);
        label.alignment = TextAlignmentOptions.TopRight;
        label.fontSize = labelFontSize;
        label.fontStyle = FontStyles.Bold;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.enableVertexGradient = true;
        label.colorGradient = new VertexGradient(Hex(0xfff59d), Hex(0xfff59d), Hex(0xff9100), Hex(0xff9100));
        label.outlineWidth = 0.3f;
        label.outlineColor = new Color32(0x2a, 0x00, 0x00, 0xff);
        label.sortingOrder = orderLabel;
        v.label = label;

        v.hole.enabled = v.scorch.enabled = v.fire.enabled = false;
        v.pitFill.enabled = v.pitRim.enabled = v.swirl.enabled = false;
        label.enabled = false;
        return v;
    }

    static SpriteRenderer CreateRenderer(string name, Transform parent, Sprite sprite, int order, Color color)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        sr.color = color;
        return sr;
    }

    CellVisual VisualAt(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        return index == -1 ? null : visuals[index];
    }

    void SpawnFireParticle(Vector3 center)
    {
        Vector2 up = ScreenUp();
        Vector2 right = new Vector2(up.y, -up.x);
        // プロトタイプ：中心から x ±26px、y +4〜+26px（画面の下側）、上向きに 40〜95px/秒
        Vector3 pos = center + (Vector3)(right * Random.Range(-26f, 26f) * PX - up * Random.Range(4f, 26f) * PX);
        fx.Particle(pos, new PieceFx.ParticleOptions
        {
            velocity = (right * Random.Range(-12f, 12f) + up * Random.Range(40f, 95f)) * PX,
            color = FireColors[Random.Range(0, FireColors.Length)],
            life = Random.Range(0.4f, 0.8f),
            size = Random.Range(0.12f, 0.28f),
            gravity = 0f,
            drag = 0.98f,
            alpha = 1f,
            shrink = true,
        });
    }

    Vector2 ScreenUp()
    {
        return cam != null ? (Vector2)cam.transform.up : Vector2.up;
    }

    Color OwnerColor(PlayerType owner)
    {
        return owner == PlayerType.Player1 ? player1Color : player2Color;
    }

    // 盤の画像から (x,y) のマスの部分を切り出す（切り出せなければ null）
    Sprite CutBoardSprite(Vector3 worldCenter)
    {
        if (boardImage == null || boardImage.sprite == null) return null;
        try
        {
            Sprite s = boardImage.sprite;
            Transform bt = boardImage.transform;
            Vector3 local = bt.InverseTransformPoint(worldCenter);
            float scale = Mathf.Abs(bt.lossyScale.x) > 0.0001f ? Mathf.Abs(bt.lossyScale.x) : 1f;
            float cellPx = s.pixelsPerUnit / scale; // 1マス（1unit）のピクセル数
            Rect tr = s.textureRect;
            float px = tr.x + (local.x - s.bounds.min.x) * s.pixelsPerUnit - cellPx * 0.5f;
            float py = tr.y + (local.y - s.bounds.min.y) * s.pixelsPerUnit - cellPx * 0.5f;
            Rect r = new Rect(px, py, cellPx, cellPx);
            if (r.xMin < tr.xMin || r.yMin < tr.yMin || r.xMax > tr.xMax || r.yMax > tr.yMax) return null;
            return Sprite.Create(s.texture, r, new Vector2(0.5f, 0.5f), cellPx);
        }
        catch (System.Exception)
        {
            return null; // アトラスに詰めて配置されたスプライトなど
        }
    }

    // ------------------------------------------------------------
    //  画像の生成（どれも 1マス = 1unit）
    // ------------------------------------------------------------

    void CreateSprites()
    {
        holeSprites = new Sprite[4];
        for (int i = 0; i < holeSprites.Length; i++) holeSprites[i] = CreateHoleSprite(i);
        squareSprite = CreateSprite(4, 4, (px, py) => Color.white, 4f);
        fireSprite = CreateFireSprite();
        pitFillSprite = CreateSprite(TEX, TEX, (px, py) =>
        {
            float r = Dist(px, py) * 66f;   // プロトタイプの px
            float a = Mathf.Clamp01(66f * 0.34f - r + 0.5f);
            return new Color(0x07 / 255f, 0x04 / 255f, 0x0c / 255f, 0.95f * a);
        });
        pitRimSprite = CreateSprite(TEX, TEX, (px, py) =>
        {
            float r = Dist(px, py) * 66f;
            float line = Mathf.Clamp01(1.25f - Mathf.Abs(r - 66f * 0.34f) + 0.5f) * 0.95f;  // 2.5px の縁取り
            float glow = Mathf.Clamp01(2.5f - Mathf.Abs(r - 66f * 0.36f) + 0.5f) * 0.3f;   // 5px の外側の光
            return new Color(1f, 1f, 1f, Mathf.Max(line, glow));
        });
        swirlSprite = CreateSwirlSprite();
    }

    // テクスチャの (px, py) の、中心からの距離（1マス = 1）
    static float Dist(float px, float py)
    {
        return Vector2.Distance(new Vector2(px, py), new Vector2(TEX * 0.5f, TEX * 0.5f)) / TEX;
    }

    // 破壊マス：割れた縁＋穴の内壁＋奥の闇（プロトタイプ drawHole。縁のギザギザは variant ごとに変える）
    static Sprite CreateHoleSprite(int variant)
    {
        const int n = 16;
        float[] r1 = new float[n], r2 = new float[n];
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            float edge = 33f / Mathf.Max(Mathf.Abs(Mathf.Cos(a)), Mathf.Abs(Mathf.Sin(a))); // 正方形の外周までの距離
            r1[i] = edge - 1f - Noise(variant, i, 1) * 5f;
            r2[i] = r1[i] - 3f - Noise(variant, i, 2) * 4f;
        }
        Color rimLine = Hex(0x9c6b2f), rim = Hex(0x3a2210), wall = Hex(0x1a0d05), deep = Color.black;
        return CreateSprite(TEX, TEX, (px, py) =>
        {
            float dx = (px - TEX * 0.5f) / TEX * 66f, dy = (py - TEX * 0.5f) / TEX * 66f;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            float ang = Mathf.Atan2(dy, dx);
            if (ang < 0f) ang += Mathf.PI * 2f;
            float f = ang / (Mathf.PI * 2f) * n;
            int i0 = Mathf.FloorToInt(f) % n, i1 = (i0 + 1) % n;
            float k = f - Mathf.Floor(f);
            float e1 = Mathf.Lerp(r1[i0], r1[i1], k), e2 = Mathf.Lerp(r2[i0], r2[i1], k);
            if (r > e1) return Color.clear;
            if (r > e1 - 1.5f) return rimLine;
            if (r > e2) return rim;
            if (r > e2 * 0.78f) return wall;
            return deep;
        });
    }

    static float Noise(int a, int b, int c)
    {
        float s = Mathf.Sin(a * 127.1f + b * 311.7f + c * 74.7f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }

    // 炎上マスの炎：赤い下地＋3本の炎（外側の橙・内側の黄）。画面の下が根元
    static Sprite CreateFireSprite()
    {
        float[][] flames = { new[] { -17f, 9f, 26f }, new[] { 0f, 12f, 38f }, new[] { 17f, 9f, 28f } };
        Color red = Hex(0xff3d00), orange = Hex(0xff6d00), yellow = Hex(0xffd54a);
        return CreateSprite(TEX, TEX, (px, py) =>
        {
            // プロトタイプの座標（マスの左上が原点・y は下向き）
            float x = px / TEX * 66f, y = (TEX - py) / TEX * 66f;
            Color c = new Color(red.r, red.g, red.b, 0f);
            if (x >= 1f && x <= 65f && y >= 1f && y <= 65f) c.a = 0.32f;
            foreach (float[] fl in flames)
            {
                float fx = 33f + fl[0], fy = 66f - 6f;
                if (InFlame(x, y, fx, fy, fl[1], fl[2])) c = Over(c, orange, 0.55f);
                if (InFlame(x, y, fx, fy, fl[1] * 0.5f, fl[2] * 0.55f)) c = Over(c, yellow, 0.55f);
            }
            return c;
        });
    }

    // 根元 (fx, fy)・幅 w・高さ h の炎の形の中か（根元の幅 0.55w、少し上でふくらみ、先がとがる）
    static bool InFlame(float x, float y, float fx, float fy, float w, float h)
    {
        float t = (fy - y) / h;
        if (t < 0f || t > 1f) return false;
        float half = w * Mathf.Lerp(0.55f, 0.75f, Mathf.Clamp01(t / 0.4f)) * (t < 0.4f ? 1f : Mathf.Pow((1f - t) / 0.6f, 0.9f));
        return Mathf.Abs(x - fx) <= half;
    }

    static Color Over(Color dst, Color src, float a)
    {
        float outA = a + dst.a * (1f - a);
        if (outA <= 0f) return Color.clear;
        Color c = (src * a + dst * dst.a * (1f - a)) / outA;
        c.a = outA;
        return c;
    }

    // 落とし穴の渦：3本の腕の渦巻き（白。色は SpriteRenderer で付ける）
    static Sprite CreateSwirlSprite()
    {
        float[] alpha = new float[TEX * TEX];
        float scale = TEX / 66f;
        float brush = 1.25f * scale;
        for (int arm = 0; arm < 3; arm++)
        {
            for (int i = 0; i <= 200; i++)
            {
                float k = i / 200f;
                float r = 66f * 0.3f * (1f - k * 0.9f);
                float a = arm * (Mathf.PI * 2f / 3f) + k * Mathf.PI * 1.6f;
                float cx = TEX * 0.5f + Mathf.Cos(a) * r * scale;
                float cy = TEX * 0.5f - Mathf.Sin(a) * r * scale; // プロトタイプは y が下向きなので上下を反転
                int x0 = Mathf.FloorToInt(cx - brush - 1), x1 = Mathf.CeilToInt(cx + brush + 1);
                int y0 = Mathf.FloorToInt(cy - brush - 1), y1 = Mathf.CeilToInt(cy + brush + 1);
                for (int py = Mathf.Max(0, y0); py <= Mathf.Min(TEX - 1, y1); py++)
                {
                    for (int px = Mathf.Max(0, x0); px <= Mathf.Min(TEX - 1, x1); px++)
                    {
                        float d = Vector2.Distance(new Vector2(px + 0.5f, py + 0.5f), new Vector2(cx, cy));
                        float v = Mathf.Clamp01(brush - d + 0.5f);
                        int idx = py * TEX + px;
                        if (v > alpha[idx]) alpha[idx] = v;
                    }
                }
            }
        }
        return CreateSprite(TEX, TEX, (px, py) => new Color(1f, 1f, 1f, alpha[(int)py * TEX + (int)px]));
    }

    // colorAt にはピクセルの左下の座標を渡す（pixelsPerUnit を省略すると w ピクセル = 1unit）
    static Sprite CreateSprite(int w, int h, System.Func<float, float, Color> colorAt, float pixelsPerUnit = -1f)
    {
        Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;
        Color32[] pixels = new Color32[w * h];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                pixels[y * w + x] = colorAt(x, y);
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), pixelsPerUnit > 0f ? pixelsPerUnit : w);
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
