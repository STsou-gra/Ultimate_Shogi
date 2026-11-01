using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using TMPro;

// マスの状態（破壊・炎上・落とし穴）の見た目（Prototype の Game.js の renderCells・drawHole・drawBurning・burnLabel・
// createTrapView、Game.tick の炎の粒・渦の回転、view/events.js の cellBroken・cellBurning・fireOut・trapClosed・
// destroyed（落とし穴に吸い込まれる）、view/fx.js の fallingTile・implode）
//
//  破壊   … 盤のマスが縮みながら抜け落ち、暗い穴（割れた縁）が残る
//  炎上   … 焦げ跡＋揺らめく炎＋立ち上る火の粉。マスの右上に残りターン数
//  落とし穴 … 仕掛けたプレイヤーの色で縁取った暗い穴と、回る渦
//  炎上・落とし穴・破壊で消えた駒は、そのマスで燃え尽きる／穴に吸い込まれる／マスと一緒に落ちる
//
// データは GameBoard.Cells（BoardCells）が持ち、ここは Cells.Changed と GameSystem.PieceDefeated を受けて表示を変えるだけ。
// 見た目は実行時に GameBoard の子として作る（駒・マスのハイライトと同じ座標系。1マス = 1）。
// シーンに置かなくても GameSystem が自動で追加する
public class BoardCellView : MonoBehaviour
{
    // プロトタイプの1マス(66px)を Unity の1マス(1unit)に合わせて、ピクセル値をそのまま使う
    private const float PX = 1f / 66f;

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private SpriteRenderer boardImage; // 描画順の基準（未指定なら "BoardImage" を探す）
    [SerializeField] private PieceFx fx;                // パーティクル・衝撃波・画面シェイク
    [SerializeField] private TMP_FontAsset font;        // 残りターン数の文字（未指定なら NotoSansJP を Resources から読む）

    [Header("Colors (Prototype の PLAYER の色)")]
    [SerializeField] private Color player1Color = Hex(0x4fc3ff);
    [SerializeField] private Color player2Color = Hex(0xff5277);

    [Header("Sorting")]
    [Tooltip("盤の画像と同じ描画順に置き、陣地の色分け(BoardZoneView)よりカメラ側に寄せる")]
    [SerializeField] private float zOffset = -0.02f;
    [Tooltip("残りターン数の描画順（盤の描画順に足す。マスのハイライト(1)より上、駒(10)より下）")]
    [SerializeField] private int labelOrderOffset = 2;
    [Tooltip("抜け落ちるマスの描画順（盤の描画順に足す）")]
    [SerializeField] private int fallingTileOrderOffset = 3;

    [Header("Burn Label")]
    [SerializeField] private float labelFontSize = 2.6f; // 17px 相当

    // ------------------------------------------------------------
    //  色（Prototype の値）
    // ------------------------------------------------------------
    private static readonly Color HoleRimColor = Hex(0x3a2210);
    private static readonly Color HoleRimLineColor = WithAlpha(Hex(0x9c6b2f), 0.9f);
    private static readonly Color HoleWallColor = Hex(0x1a0d05);
    private static readonly Color HoleDeepColor = Color.black;
    private static readonly Color TileColor = Hex(0xe6bd74);
    private static readonly Color[] DebrisColors = { Hex(0x8d6e63), Hex(0x5d4037), Hex(0x3e2723), Hex(0xe6bd74) };
    private static readonly Color DebrisRingColor = Hex(0x8d6e63);

    private static readonly Color ScorchColor = WithAlpha(Hex(0x3e1500), 0.45f);
    private static readonly Color FireTintColor = WithAlpha(Hex(0xff3d00), 0.32f);
    private static readonly Color FlameOuterColor = WithAlpha(Hex(0xff6d00), 0.55f);
    private static readonly Color FlameInnerColor = WithAlpha(Hex(0xffd54a), 0.55f);
    private static readonly Color[] FireParticleColors = { Hex(0xff6d00), Hex(0xffab00), Hex(0xff3d00), Hex(0xffd54a) };
    private static readonly Color[] IgniteColors = { Hex(0xff6d00), Hex(0xffab00), Hex(0xff3d00), Hex(0xffea00) };
    private static readonly Color[] SmokeColors = { Hex(0x9e9e9e), Hex(0x616161), Hex(0x424242) };
    private static readonly Color LabelTopColor = Hex(0xfff59d);
    private static readonly Color LabelBottomColor = Hex(0xff9100);
    private static readonly Color LabelOutlineColor = Hex(0x2a0000);

    private static readonly Color PitColor = WithAlpha(Hex(0x07040c), 0.95f);
    private static readonly Color SwirlColor = WithAlpha(Hex(0x7e57c2), 0.85f);
    private static readonly Color[] ImplodeColors = { Hex(0x7e57c2), Hex(0x311b92), Hex(0xb388ff) };
    private static readonly Color TrapRingColor = Hex(0x7e57c2);

    // 炎の形（中心からの横のずれ・幅・高さ px）。Prototype の drawBurning と同じ3本
    private static readonly Vector3[] FlameShapes = { new Vector3(-17f, 9f, 26f), new Vector3(0f, 12f, 38f), new Vector3(17f, 9f, 28f) };

    // ------------------------------------------------------------
    //  マスごとの表示
    // ------------------------------------------------------------
    private class CellVisual
    {
        public int x, y;
        public Transform root;   // マスの中心。カメラの回転に合わせて回す（炎が画面の上へ立ち上り、数字が読める向きになる）

        public GameObject hole;

        public GameObject burning;
        public readonly List<SpriteRenderer> fireSprites = new List<SpriteRenderer>(); // 明滅させる炎
        public readonly List<float> fireBaseAlpha = new List<float>();
        public readonly List<Transform> flames = new List<Transform>();
        public readonly List<Vector3> flameBaseScale = new List<Vector3>();
        public TextMeshPro label;
        public int shownRemaining = int.MinValue;

        public GameObject trap;
        public SpriteRenderer trapGlow;
        public SpriteRenderer trapRing;
        public Transform swirl;
        public bool trapClosing; // 駒が落ちて穴が閉じるまでの間（吸い込みが終わるまで表示を残す）
    }

    private readonly CellVisual[] visuals = new CellVisual[BoardCells.Size * BoardCells.Size];
    private BoardCells cells;
    private Transform root;
    private Camera cam;
    private int sortingLayer;
    private int baseOrder;
    private float time;

    private static Sprite squareSprite;
    private static Sprite discSprite;
    private static Sprite pitRingSprite;
    private static Sprite pitGlowSprite;
    private static Sprite flameSprite;
    private static Sprite swirlSprite;

    void Start()
    {
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (boardImage == null)
        {
            GameObject obj = GameObject.Find("BoardImage");
            if (obj != null) boardImage = obj.GetComponent<SpriteRenderer>();
        }
        if (fx == null) fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        cam = Camera.main;

        if (gameBoard == null) return;
        cells = gameBoard.Cells;

        sortingLayer = boardImage != null ? boardImage.sortingLayerID : 0;
        baseOrder = boardImage != null ? boardImage.sortingOrder : 0;

        root = new GameObject("BoardCells").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(0f, 0f, zOffset);

        // 開始前に変わっていたマスがあれば表示する
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++) Refresh(x, y);
        }

        cells.Changed += OnCellChanged;
        if (gameSystem != null) gameSystem.PieceDefeated += OnPieceDefeated;
    }

    void OnDestroy()
    {
        if (cells != null) cells.Changed -= OnCellChanged;
        if (gameSystem != null) gameSystem.PieceDefeated -= OnPieceDefeated;
        if (root != null) Destroy(root.gameObject);
    }

    // ------------------------------------------------------------
    //  毎フレーム：向き・炎の揺らめきと火の粉・渦の回転・残りターン数
    // ------------------------------------------------------------
    void LateUpdate()
    {
        if (root == null) return;
        float dt = Mathf.Min(0.05f, Time.deltaTime);
        time += dt;
        float t = time;

        Quaternion upright = Quaternion.Euler(0f, 0f, cam != null ? cam.transform.eulerAngles.z : 0f);
        Vector2 up = ScreenUp;
        Vector2 right = new Vector2(up.y, -up.x);
        float fireAlpha = 0.75f + 0.15f * Mathf.Sin(t * 11f) + 0.1f * Mathf.Sin(t * 17f);
        int turnNumber = gameSystem != null && gameSystem.Match != null ? gameSystem.Match.Turn.TurnNumber : 0;

        foreach (CellVisual v in visuals)
        {
            if (v == null) continue;
            v.root.rotation = upright;

            if (v.burning != null && v.burning.activeSelf)
            {
                for (int i = 0; i < v.fireSprites.Count; i++)
                {
                    SpriteRenderer sr = v.fireSprites[i];
                    sr.color = WithAlpha(sr.color, v.fireBaseAlpha[i] * fireAlpha);
                }
                for (int i = 0; i < v.flames.Count; i++)
                {
                    Vector3 s = v.flameBaseScale[i];
                    s.y *= 1f + 0.08f * Mathf.Sin(t * 13f + i * 1.7f);
                    v.flames[i].localScale = s;
                }

                int remaining = cells.BurnRemaining(v.x, v.y, turnNumber);
                if (remaining != v.shownRemaining && v.label != null)
                {
                    v.shownRemaining = remaining;
                    v.label.text = remaining.ToString();
                }

                // 立ち上る火の粉
                if (fx != null && Random.value < dt * 9f)
                {
                    Vector3 c = v.root.position;
                    Vector2 pos = (Vector2)c + right * (Random.Range(-26f, 26f) * PX) - up * (Random.Range(4f, 26f) * PX);
                    fx.Particle(new Vector3(pos.x, pos.y, c.z), new PieceFx.ParticleOptions
                    {
                        velocity = right * (Random.Range(-12f, 12f) * PX) + up * (Random.Range(40f, 95f) * PX),
                        color = Pick(FireParticleColors),
                        life = Random.Range(0.4f, 0.8f),
                        size = Random.Range(0.12f, 0.28f),
                        drag = 0.98f, alpha = 1f, shrink = true,
                    });
                }
            }

            if (v.swirl != null && v.trap != null && v.trap.activeSelf)
            {
                v.swirl.localRotation = Quaternion.Euler(0f, 0f, t * 2.2f * Mathf.Rad2Deg);
            }
        }
    }

    // ------------------------------------------------------------
    //  マスの状態が変わった
    // ------------------------------------------------------------
    void OnCellChanged(int x, int y, CellChange change)
    {
        Vector3 center = CellWorldCenter(x, y);
        switch (change)
        {
            case CellChange.Broken:
                Refresh(x, y);
                PlayFallingTile(x, y);
                if (fx != null)
                {
                    PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
                    burst.count = 22; burst.colors = DebrisColors; burst.speed = 220f * PX; burst.life = 0.8f;
                    burst.size = 0.28f; burst.gravity = 420f * PX;
                    fx.Burst(center, burst);
                    fx.Shockwave(center, DebrisRingColor, 1.8f, 0.4f);
                    fx.Shake(5f, 0.2f);
                }
                break;

            case CellChange.Burning:
                Refresh(x, y);
                if (fx != null)
                {
                    PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
                    burst.count = 18; burst.colors = IgniteColors; burst.speed = 160f * PX; burst.life = 0.6f;
                    burst.size = 0.25f; burst.gravity = -120f * PX;
                    fx.Burst(center, burst);
                }
                break;

            case CellChange.Extinguished:
                Refresh(x, y);
                if (fx != null)
                {
                    PieceFx.BurstOptions smoke = PieceFx.BurstOptions.Default;
                    smoke.count = 12; smoke.colors = SmokeColors; smoke.speed = 60f * PX; smoke.life = 1.2f;
                    smoke.size = 0.5f; smoke.gravity = -50f * PX; smoke.grow = 1f; smoke.shrink = false;
                    smoke.alpha = 0.5f; smoke.drag = 0.97f;
                    fx.Burst(center, smoke);
                }
                break;

            case CellChange.TrapSet:
            {
                Refresh(x, y);
                CellVisual v = visuals[Index(x, y)];
                if (v != null && v.trap != null)
                {
                    v.trap.transform.localScale = Vector3.zero;
                    v.trap.transform.DOScale(1f, 0.35f).SetEase(Ease.OutBack).SetLink(v.trap);
                }
                break;
            }

            case CellChange.TrapSprung:
            {
                // 駒が吸い込まれ終わってから穴を閉じる（Prototype は 0.7 秒待ってから trapClosed）
                CellVisual v = GetVisual(x, y);
                v.trapClosing = true;
                DOVirtual.DelayedCall(0.65f, () =>
                {
                    v.trapClosing = false;
                    Refresh(x, y);
                    if (fx != null)
                    {
                        fx.Shockwave(CellWorldCenter(x, y), TrapRingColor, 1.4f, 0.35f);
                        fx.Shake(4f, 0.2f);
                    }
                }).SetLink(gameObject);
                break;
            }

            default:
                Refresh(x, y);
                break;
        }
    }

    // マスのデータに合わせて、そのマスの表示（穴・炎・落とし穴）を出し入れする
    void Refresh(int x, int y)
    {
        BoardCell cell = cells.At(x, y);
        if (cell == null) return;
        CellVisual v = visuals[Index(x, y)];
        bool needed = cell.IsBroken || cell.IsBurning || cell.HasTrap;
        if (v == null && !needed) return;
        if (v == null) v = GetVisual(x, y);

        // 破壊マス（穴の形はマスごとに決まるので、最初に破壊されたときに作る）
        if (cell.IsBroken && v.hole == null) v.hole = CreateHole(v);
        if (v.hole != null) v.hole.SetActive(cell.IsBroken);

        // 炎上
        if (cell.IsBurning && v.burning == null) CreateBurning(v);
        if (v.burning != null)
        {
            v.burning.SetActive(cell.IsBurning);
            v.shownRemaining = int.MinValue; // 残りターン数を書き直す
        }

        // 落とし穴（駒が落ちている間は、データから消えていても表示を残す）
        bool showTrap = cell.HasTrap || v.trapClosing;
        if (showTrap && v.trap == null) CreateTrap(v);
        if (v.trap != null)
        {
            if (cell.HasTrap)
            {
                Color owner = cell.Trap.Value == PlayerType.Player1 ? player1Color : player2Color;
                v.trapGlow.color = WithAlpha(owner, 0.3f);
                v.trapRing.color = WithAlpha(owner, 0.95f);
            }
            if (!showTrap) v.trap.transform.localScale = Vector3.one;
            v.trap.SetActive(showTrap);
        }
    }

    CellVisual GetVisual(int x, int y)
    {
        int index = Index(x, y);
        if (visuals[index] != null) return visuals[index];

        CellVisual v = new CellVisual { x = x, y = y };
        GameObject obj = new GameObject($"Cell_{x}_{y}");
        v.root = obj.transform;
        v.root.SetParent(root, false);
        Vector2 location = GameHelper.CalcPanelLocation(x, y);
        v.root.localPosition = new Vector3(location.x, location.y, 0f);
        visuals[index] = v;
        return v;
    }

    // ------------------------------------------------------------
    //  破壊マス：盤が抜け落ちたような暗い穴＋割れた縁
    // ------------------------------------------------------------
    GameObject CreateHole(CellVisual v)
    {
        SpriteRenderer sr = AddSprite("Hole", v.root, CreateHoleSprite(v.x, v.y), Color.white, Vector2.zero, Vector2.one, 0f, baseOrder);
        return sr.gameObject;
    }

    // 盤のマスが縮みながら暗くなって抜け落ちる（Prototype の fx.fallingTile）
    void PlayFallingTile(int x, int y)
    {
        CellVisual v = GetVisual(x, y);
        GameObject tile = new GameObject("FallingTile");
        tile.transform.SetParent(v.root, false);
        tile.transform.localPosition = new Vector3(0f, 0f, -0.01f);
        int order = baseOrder + fallingTileOrderOffset;
        SpriteRenderer face = AddSprite("Face", tile.transform, GetSquareSprite(), TileColor, Vector2.zero, Vector2.one, 0f, order);
        SpriteRenderer shade = AddSprite("Shade", tile.transform, GetSquareSprite(), WithAlpha(Color.black, 0f), Vector2.zero, Vector2.one, -0.001f, order);

        const float dur = 0.6f;
        Vector3 down = tile.transform.localPosition + new Vector3(0f, -6f * PX, 0f); // v.root はカメラに合わせて回るので、ローカルの下 = 画面の下
        DOTween.Sequence()
            .Join(tile.transform.DOScale(0.12f, dur).SetEase(Ease.InQuad))
            .Join(tile.transform.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-0.6f, 0.6f) * Mathf.Rad2Deg), dur).SetEase(Ease.InQuad))
            .Join(tile.transform.DOLocalMove(down, dur).SetEase(Ease.InQuad))
            .Join(shade.DOFade(0.9f, dur).SetEase(Ease.InSine))
            .Insert(dur - 0.2f, face.DOFade(0f, 0.2f))
            .Insert(dur - 0.2f, shade.DOFade(0f, 0.2f))
            .OnComplete(() => Destroy(tile))
            .SetLink(tile);
    }

    // ------------------------------------------------------------
    //  炎上マス：焦げ跡＋揺らめく炎＋残りターン数
    // ------------------------------------------------------------
    void CreateBurning(CellVisual v)
    {
        GameObject obj = new GameObject("Burning");
        obj.transform.SetParent(v.root, false);
        v.burning = obj;
        Vector2 inner = Vector2.one * (1f - 2f * PX);

        AddSprite("Scorch", obj.transform, GetSquareSprite(), ScorchColor, Vector2.zero, inner, -0.001f, baseOrder);
        AddFire(v, AddSprite("FireTint", obj.transform, GetSquareSprite(), FireTintColor, Vector2.zero, inner, -0.002f, baseOrder));

        // 3本の炎（外側の橙・内側の黄）。マスの下端から立ち上る
        float bottom = -0.5f + 6f * PX;
        for (int i = 0; i < FlameShapes.Length; i++)
        {
            Vector3 shape = FlameShapes[i];
            GameObject flame = new GameObject($"Flame{i}");
            flame.transform.SetParent(obj.transform, false);
            flame.transform.localPosition = new Vector3(shape.x * PX, bottom, -0.003f - i * 0.0005f);
            // Prototype の炎は制御点 ±w の2次曲線なので、いちばん太いところの幅は約 1.4w
            Vector3 scale = new Vector3(shape.y * 1.4f * PX, shape.z * PX, 1f);
            flame.transform.localScale = scale;
            v.flames.Add(flame.transform);
            v.flameBaseScale.Add(scale);

            AddFire(v, AddSprite("Outer", flame.transform, GetFlameSprite(), FlameOuterColor, Vector2.zero, Vector2.one, 0f, baseOrder));
            AddFire(v, AddSprite("Inner", flame.transform, GetFlameSprite(), FlameInnerColor, Vector2.zero, new Vector2(0.5f, 0.55f), -0.0002f, baseOrder));
        }

        // 残りターン数（マスの右上）
        GameObject labelObj = new GameObject("Remaining", typeof(RectTransform));
        labelObj.transform.SetParent(obj.transform, false);
        TextMeshPro tmp = labelObj.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.fontSize = labelFontSize;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.TopRight;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = Color.white;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(LabelTopColor, LabelTopColor, LabelBottomColor, LabelBottomColor);
        tmp.outlineColor = LabelOutlineColor;
        tmp.outlineWidth = 0.3f;
        tmp.sortingLayerID = sortingLayer;
        tmp.sortingOrder = baseOrder + labelOrderOffset;
        RectTransform rt = tmp.rectTransform;
        rt.pivot = new Vector2(1f, 1f);
        rt.sizeDelta = new Vector2(0.8f, 0.4f);
        rt.localPosition = new Vector3(0.5f - 3f * PX, 0.5f - 1f * PX, -0.005f);
        v.label = tmp;
    }

    void AddFire(CellVisual v, SpriteRenderer sr)
    {
        v.fireSprites.Add(sr);
        v.fireBaseAlpha.Add(sr.color.a);
    }

    // ------------------------------------------------------------
    //  落とし穴：仕掛けたプレイヤーの色で縁取った暗い穴＋回る渦
    // ------------------------------------------------------------
    void CreateTrap(CellVisual v)
    {
        GameObject obj = new GameObject("Trap");
        obj.transform.SetParent(v.root, false);
        v.trap = obj;
        v.trapGlow = AddSprite("Glow", obj.transform, GetPitGlowSprite(), Color.white, Vector2.zero, Vector2.one, -0.001f, baseOrder);
        AddSprite("Pit", obj.transform, GetDiscSprite(), PitColor, Vector2.zero, Vector2.one * (0.34f * 2f), -0.002f, baseOrder);
        v.trapRing = AddSprite("Ring", obj.transform, GetPitRingSprite(), Color.white, Vector2.zero, Vector2.one, -0.003f, baseOrder);
        v.swirl = AddSprite("Swirl", obj.transform, GetSwirlSprite(), SwirlColor, Vector2.zero, Vector2.one, -0.004f, baseOrder).transform;
    }

    // ------------------------------------------------------------
    //  マスの効果で消えた駒の演出（燃え尽きる・穴に吸い込まれる・マスと一緒に落ちる）
    //  駒の GameObject は消失した直後に破棄されるので、写しを作って演出する
    // ------------------------------------------------------------
    void OnPieceDefeated(PieceDefeat defeat)
    {
        if (defeat.Kind != DefeatKind.Vanish) return;
        string reason = defeat.Reason;
        if (reason != BoardCells.ReasonTrap && reason != BoardCells.ReasonBurning && reason != BoardCells.ReasonBroken) return;

        GamePiece source = defeat.Piece;
        if (source == null) return;
        Transform st = source.transform;
        Vector3 from = st.localPosition;
        DOTween.Kill(st, true); // 移動の演出を終わらせて、大きさ・表示順を元に戻す

        GameObject ghost = Instantiate(source.gameObject, st.parent);
        ghost.name = $"{source.name}_Vanish";
        // 写しは盤の駒として扱われないよう、GamePiece をすぐに外す（見た目だけ残す）
        foreach (GamePiece gp in ghost.GetComponentsInChildren<GamePiece>(true)) DestroyImmediate(gp);
        Transform gt = ghost.transform;
        gt.localPosition = from;

        SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) baseColors[i] = sprites[i].color;

        // 移動の途中なら、まずそのマスまで進む
        Vector2 location = GameHelper.CalcPanelLocation(defeat.X, defeat.Y);
        Vector3 center = new Vector3(location.x, location.y, from.z);
        Sequence seq = DOTween.Sequence().SetLink(ghost);
        float travel = Vector2.Distance(from, center) > 0.05f ? 0.2f : 0f;
        if (travel > 0f) seq.Append(gt.DOLocalMove(center, travel).SetEase(Ease.OutQuad));

        Vector3 baseScale = gt.localScale;
        if (reason == BoardCells.ReasonTrap)
        {
            // 縮小・回転しながら穴の中心へ吸い込まれる
            const float dur = 0.65f;
            float start = travel;
            seq.InsertCallback(start, () =>
            {
                if (fx != null) Implode(gt.position, 26, 70f * PX, ImplodeColors, 0.6f);
            });
            seq.Insert(start, gt.DOScale(baseScale * 0.05f, dur).SetEase(Ease.InQuad));
            seq.Insert(start, gt.DOLocalRotate(new Vector3(0f, 0f, 540f), dur, RotateMode.LocalAxisAdd).SetEase(Ease.InQuad));
            seq.Insert(start + 0.4f, FadeSprites(sprites, baseColors, Color.white, 0.25f, Ease.InSine));
        }
        else if (reason == BoardCells.ReasonBurning)
        {
            // 炎に包まれて燃え尽きる
            float start = travel;
            seq.InsertCallback(start, () =>
            {
                if (fx == null) return;
                PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
                burst.count = 16; burst.colors = IgniteColors; burst.speed = 200f * PX; burst.life = 0.6f;
                burst.size = 0.26f; burst.gravity = -120f * PX;
                fx.Burst(gt.position, burst);
                fx.Shockwave(gt.position, Hex(0xff6d00), 1.6f, 0.35f);
            });
            Vector3 up = gt.parent != null ? gt.parent.InverseTransformDirection(ScreenUp) : (Vector3)ScreenUp;
            up.z = 0f;
            seq.Insert(start, BurnSprites(sprites, baseColors, 0.6f));
            seq.Insert(start + 0.15f, gt.DOScale(baseScale * 0.85f, 0.45f).SetEase(Ease.InQuad));
            seq.Insert(start + 0.15f, gt.DOLocalMove(center + up.normalized * (12f * PX), 0.45f).SetEase(Ease.OutQuad));
        }
        else
        {
            // マスと一緒に縮みながら暗くなって落ちる
            const float dur = 0.6f;
            float start = travel;
            seq.Insert(start, gt.DOScale(baseScale * 0.12f, dur).SetEase(Ease.InQuad));
            seq.Insert(start, gt.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-35f, 35f)), dur, RotateMode.LocalAxisAdd).SetEase(Ease.InQuad));
            seq.Insert(start, FadeSprites(sprites, baseColors, Color.black, dur, Ease.InQuad));
        }
        seq.OnComplete(() => Destroy(ghost));
    }

    // 燃え尽きる色の変化：最初の 1/4 で炎の色に染まり、残りで焦げた色になりながら透明になる
    static Tween BurnSprites(SpriteRenderer[] sprites, Color[] baseColors, float duration)
    {
        Color fire = Hex(0xff8a3d);
        Color ash = Hex(0x3e1500);
        return DOVirtual.Float(0f, 1f, duration, k =>
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color b = baseColors[i];
                Color c;
                if (k < 0.25f)
                {
                    c = Color.Lerp(b, b * fire, k / 0.25f);
                    c.a = b.a;
                }
                else
                {
                    float f = (k - 0.25f) / 0.75f;
                    c = Color.Lerp(b * fire, b * ash, f);
                    c.a = b.a * (1f - f * f);
                }
                sprites[i].color = c;
            }
        });
    }

    // sprites を、元の色に tint を掛けた色へ寄せながら透明にする
    static Tween FadeSprites(SpriteRenderer[] sprites, Color[] baseColors, Color tint, float duration, Ease ease)
    {
        return DOVirtual.Float(0f, 1f, duration, k =>
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                Color c = Color.Lerp(baseColors[i], baseColors[i] * tint, k);
                c.a = baseColors[i].a * (1f - k);
                sprites[i].color = c;
            }
        }).SetEase(ease);
    }

    // 周りから中心へ吸い込まれる粒（Prototype の fx.implode：寿命が尽きる瞬間に中心へ届く）
    void Implode(Vector3 center, int count, float radius, Color[] colors, float life)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = radius * Random.Range(0.6f, 1f);
            float l = life * Random.Range(0.7f, 1f);
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            fx.Particle(center + (Vector3)(dir * d), new PieceFx.ParticleOptions
            {
                velocity = -dir * d / l, drag = 1f, life = l, color = Pick(colors),
                size = 0.2f * Random.Range(0.6f, 1.3f), alpha = 1f, shrink = true,
            });
        }
    }

    // ------------------------------------------------------------
    //  補助
    // ------------------------------------------------------------
    static int Index(int x, int y)
    {
        return GameHelper.CalcPanelNum(x, y);
    }

    Vector3 CellWorldCenter(int x, int y)
    {
        Vector2 location = GameHelper.CalcPanelLocation(x, y);
        Transform parent = root != null ? root : gameBoard.transform;
        return parent.TransformPoint(new Vector3(location.x, location.y, 0f));
    }

    Vector2 ScreenUp => cam != null ? (Vector2)cam.transform.up : Vector2.up;

    SpriteRenderer AddSprite(string name, Transform parent, Sprite sprite, Color color, Vector2 position, Vector2 size, float z, int order)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = new Vector3(position.x, position.y, z);
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingLayerID = sortingLayer;
        sr.sortingOrder = order;
        return sr;
    }

    static Color Pick(Color[] colors)
    {
        return colors[Random.Range(0, colors.Length)];
    }

    static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // ------------------------------------------------------------
    //  画像（実行時に作る。どれも1マス = 1 の大きさ）
    // ------------------------------------------------------------

    // 1×1 の白い四角
    static Sprite GetSquareSprite()
    {
        if (squareSprite != null) return squareSprite;
        const int size = 4;
        Texture2D tex = NewTexture(size, size);
        tex.filterMode = FilterMode.Point;
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(pixels);
        tex.Apply();
        squareSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return squareSprite;
    }

    // 直径 1 の白い円（ふちをなめらかにする）
    static Sprite GetDiscSprite()
    {
        if (discSprite != null) return discSprite;
        const int size = 64;
        Texture2D tex = NewTexture(size, size);
        float half = size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(half - 0.5f - d + 0.5f)));
            }
        }
        tex.Apply();
        discSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return discSprite;
    }

    // 落とし穴の縁（半径 0.34・太さ 2.5px）と、その外のぼんやりした縁（半径 0.36・太さ 5px）
    static Sprite GetPitRingSprite()
    {
        if (pitRingSprite == null) pitRingSprite = CreateRingSprite(0.34f, 2.5f * PX);
        return pitRingSprite;
    }

    static Sprite GetPitGlowSprite()
    {
        if (pitGlowSprite == null) pitGlowSprite = CreateRingSprite(0.36f, 5f * PX);
        return pitGlowSprite;
    }

    // 1×1 の画像に、半径 radius・太さ width の輪を描く
    static Sprite CreateRingSprite(float radius, float width)
    {
        const int size = 128;
        Texture2D tex = NewTexture(size, size);
        float half = size * 0.5f;
        float r = radius * size;
        float w = width * size * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half));
                float a = Mathf.Clamp01(w - Mathf.Abs(d - r) + 0.5f);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // 炎の形（下が少し細く、下から3割ほどが一番太く、上が尖る）。幅 1・高さ 1、基準点は下端の中央
    static Sprite GetFlameSprite()
    {
        if (flameSprite != null) return flameSprite;
        const int w = 64, h = 64;
        Texture2D tex = NewTexture(w, h);
        float halfW = w * 0.5f;
        for (int y = 0; y < h; y++)
        {
            float v = (y + 0.5f) / h;
            float hw = v < 0.3f
                ? Mathf.Lerp(0.8f, 1f, Mathf.Sin(v / 0.3f * Mathf.PI * 0.5f))
                : Mathf.Pow(Mathf.Cos((v - 0.3f) / 0.7f * Mathf.PI * 0.5f), 1.2f);
            float edge = hw * (halfW - 1f);
            for (int x = 0; x < w; x++)
            {
                float dx = Mathf.Abs(x + 0.5f - halfW);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(edge - dx + 0.5f)));
            }
        }
        tex.Apply();
        flameSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0f), h);
        flameSprite.name = "Flame";
        return flameSprite;
    }

    // 3本の腕の渦巻き（Prototype の createTrapView の swirl）
    static Sprite GetSwirlSprite()
    {
        if (swirlSprite != null) return swirlSprite;
        const int size = 128;
        const int steps = 20;
        Texture2D tex = NewTexture(size, size);
        List<Vector2> points = new List<Vector2>();
        List<int> armStart = new List<int>();
        for (int arm = 0; arm < 3; arm++)
        {
            armStart.Add(points.Count);
            for (int i = 0; i <= steps; i++)
            {
                float k = i / (float)steps;
                float r = 0.3f * (1f - k * 0.9f);
                float a = arm * (Mathf.PI * 2f / 3f) + k * Mathf.PI * 1.6f;
                points.Add(new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r));
            }
        }
        float halfWidth = 2.5f * PX * 0.5f;
        float pxSize = 1f / size;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f);
                float best = float.MaxValue;
                for (int arm = 0; arm < 3; arm++)
                {
                    int s0 = armStart[arm];
                    for (int i = 0; i < steps; i++)
                    {
                        best = Mathf.Min(best, DistanceToSegment(p, points[s0 + i], points[s0 + i + 1]));
                    }
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01((halfWidth - best) / pxSize + 0.5f)));
            }
        }
        tex.Apply();
        swirlSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return swirlSprite;
    }

    // 破壊マスの穴（Prototype の drawHole：割れた縁 → 穴の内壁 → 奥の闇）。形はマスごとに決まった乱れ方をする
    static Sprite CreateHoleSprite(int cellX, int cellY)
    {
        const int size = 66;
        const int n = 16;
        Texture2D tex = NewTexture(size, size);
        float half = size * 0.5f;

        // Prototype の座標（x = 筋, y = 段、y は下向き）でノイズを作る。Unity の X が段、Y が筋
        int px = cellY, py = cellX;
        Vector2[] rim = new Vector2[n];
        Vector2[] wall = new Vector2[n];
        Vector2[] deep = new Vector2[n];
        for (int i = 0; i < n; i++)
        {
            float a = i / (float)n * Mathf.PI * 2f;
            float edge = half / Mathf.Max(Mathf.Abs(Mathf.Cos(a)), Mathf.Abs(Mathf.Sin(a))); // 正方形の外周までの距離
            float r1 = edge - 1f - CellNoise(px, py, i) * 5f;
            float r2 = r1 - 3f - CellNoise(py, px, i) * 4f;
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            rim[i] = dir * r1;
            wall[i] = dir * r2;
            deep[i] = new Vector2(dir.x * r2 * 0.8f, -4f + dir.y * r2 * 0.74f); // 奥の闇は少し下にずらす（画像の y は上向き）
        }

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                Color c = new Color(0f, 0f, 0f, 0f);
                if (InPolygon(p, rim))
                {
                    c = HoleRimColor;
                    if (InPolygon(p, wall)) c = InPolygon(p, deep) ? HoleDeepColor : HoleWallColor;
                }
                // 割れた縁の線（太さ 1.5px）
                float line = Mathf.Clamp01(0.75f - DistanceToPolygon(p, rim) + 0.5f);
                if (line > 0f)
                {
                    Color lc = HoleRimLineColor;
                    float la = lc.a * line;
                    c = new Color(Mathf.Lerp(c.r, lc.r, la), Mathf.Lerp(c.g, lc.g, la), Mathf.Lerp(c.b, lc.b, la), Mathf.Max(c.a, la));
                }
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    // Prototype の util.js の cellNoise（0〜1 の決まった乱れ）
    static float CellNoise(int x, int y, int i)
    {
        double v = System.Math.Sin(x * 127.1 + y * 311.7 + i * 74.7) * 43758.5453;
        return (float)(v - System.Math.Floor(v));
    }

    static bool InPolygon(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[i], b = poly[j];
            if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x) inside = !inside;
        }
        return inside;
    }

    static float DistanceToPolygon(Vector2 p, Vector2[] poly)
    {
        float best = float.MaxValue;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            best = Mathf.Min(best, DistanceToSegment(p, poly[j], poly[i]));
        }
        return best;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float len = ab.sqrMagnitude;
        float t = len > 0f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len) : 0f;
        return Vector2.Distance(p, a + ab * t);
    }

    static Texture2D NewTexture(int width, int height)
    {
        return new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
    }
}
