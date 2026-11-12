using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 超常魔法「停止世界」（時間停止）の見た目（Prototype の Game.js の setTimeStopLook・setHeld・renderTimeStopMarks・refreshTimeStopHud）。
//
//  ・色の反転 … 時間停止中は画面（盤・駒・背景）の色を反転する（画面を覆うスプライトに Resources/Shaders/InvertScreen を使う）。
//               始まるときは少しずつ反転し、解除のときはガラスが砕けた瞬間（MagicFx.TimeStopBreakTime）に戻る
//  ・残り回数 … 画面の上に「時間停止中」・残り回数の円グラフと「残り N 回」・「解除 [Q]」ボタン（発動したプレイヤーが操作できるときだけ押せる）
//  ・予定の駒 … 取られる予定・消える予定の駒は灰色にして、ほかの駒の下に残す（上に駒が重なっていれば少しずらす）。爆発する予定の火薬は色のまま
//  ・予定の印 … 取（取り）・爆（爆発。範囲を枠で囲む）・消（消失）・成（成り）・穴／壁／壊／炎（マスの変化。マスを枠で囲む）のバッジ
// 時間停止の決まりと処理は GameSystem.TimeStop.cs。シーンに置かなくても GameSystem が自動で追加する
public class TimeStopView : MonoBehaviour
{
    private const float PX = 1f / 66f; // プロトタイプの1マス(66px) = Unity の1マス

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private Canvas canvas;      // 未指定ならスキルボタンのある Canvas
    [SerializeField] private TMP_FontAsset font; // 未指定なら NotoSansJP を Resources から読む

    [Header("Sorting")]
    [SerializeField] private int invertOrder = 430; // 文字(400)より手前（それより奥に描かれたものの色を反転する）。閃光・カットイン（Canvas）は反転しない
    [SerializeField] private int markOrder = 440;   // 予定の印（反転の手前に描くので、元の色のまま見える）
    [SerializeField] private int heldOrderOffset = -8; // 予定の駒をほかの駒(10)の下・盤(0)の上に置く

    [Header("Layout (Canvas の基準解像度 1920×1080 でのピクセル)")]
    [SerializeField] private float hudTop = 20f;

    private static readonly Color HudColor = new Color(0.04f, 0.04f, 0.08f, 0.88f);
    private static readonly Color AccentColor = Hex(0xffd54a);
    private static readonly Color ReleaseColor = Hex(0x455a64);
    private static readonly Color FallbackTint = new Color(0.1f, 0.15f, 0.45f, 0.35f); // 反転のシェーダーが使えないときの代わり

    private Camera cam;
    private SpriteRenderer invert;
    private Material invertMaterial;
    private bool canInvert;
    private float invertAmount;
    private Tween invertTween;

    private RectTransform hud;
    private Image pieFill;
    private TextMeshProUGUI countText;
    private Button releaseButton;
    private int shownRemaining = -1;

    private Transform marksRoot;
    private readonly List<Transform> markTexts = new List<Transform>();

    private class HeldLook
    {
        public SpriteRenderer[] renderers;
        public Color[] colors;
        public int[] orders;
    }
    private readonly Dictionary<GamePiece, HeldLook> held = new Dictionary<GamePiece, HeldLook>();

    private static Sprite disc;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        if (canvas == null) canvas = FindUICanvas();
        cam = Camera.main;
        if (gameSystem == null || gameBoard == null) return;

        BuildInvert();
        if (canvas != null) BuildHud();
        marksRoot = new GameObject("TimeStopMarks").transform;
        marksRoot.SetParent(gameBoard.transform, false);
        marksRoot.localPosition = new Vector3(0f, 0f, -0.07f);

        gameSystem.TimeStopStarted += OnStarted;
        gameSystem.TimeStopUpdated += OnUpdated;
        gameSystem.TimeStopResolving += OnResolving;
        gameSystem.TimeStopMarks += OnMarks;
        gameSystem.PieceHeldChanged += OnPieceHeld;
    }

    void OnDestroy()
    {
        if (gameSystem != null)
        {
            gameSystem.TimeStopStarted -= OnStarted;
            gameSystem.TimeStopUpdated -= OnUpdated;
            gameSystem.TimeStopResolving -= OnResolving;
            gameSystem.TimeStopMarks -= OnMarks;
            gameSystem.PieceHeldChanged -= OnPieceHeld;
        }
        if (invert != null) Destroy(invert.gameObject);
        if (invertMaterial != null) Destroy(invertMaterial);
        if (hud != null) Destroy(hud.gameObject);
    }

    // ------------------------------------------------------------
    //  GameSystem からの通知
    // ------------------------------------------------------------
    void OnStarted(TimeStopState ts)
    {
        SetInverted(true);
        RenderMarks(ts, -1);
        RefreshHud(true);
    }

    void OnUpdated(TimeStopState ts, bool counted)
    {
        RenderMarks(ts, -1);
        RefreshHeldOffsets();
        RefreshHud(counted);
    }

    // 時間が動き出す：ガラスが砕けた瞬間に反転をやめる。残り回数の表示は消す
    void OnResolving(TimeStopState ts)
    {
        RefreshHud(false);
        DOVirtual.DelayedCall(MagicFx.TimeStopBreakTime, () => SetInverted(false), false).SetLink(gameObject);
    }

    void OnMarks(TimeStopState ts, int upto)
    {
        RenderMarks(ts, upto);
    }

    void OnPieceHeld(GamePiece piece, bool on, bool gray)
    {
        SetHeld(piece, on, gray);
        RefreshHeldOffsets();
    }

    // ------------------------------------------------------------
    //  毎フレーム
    // ------------------------------------------------------------
    void Update()
    {
        if (hud == null || gameSystem == null) return;
        TimeStopState ts = gameSystem.TimeStop;
        bool show = ts != null && gameSystem.IsInTurn;
        if (hud.gameObject.activeSelf != show) hud.gameObject.SetActive(show);
        if (!show) return;
        releaseButton.interactable = gameSystem.CanReleaseTimeStop;
        if (ts.Remaining != shownRemaining) RefreshHud(false);
    }

    void LateUpdate()
    {
        if (cam == null) cam = Camera.main;
        float camZ = cam != null ? cam.transform.eulerAngles.z : 0f;

        // 反転の板はカメラについていく（揺れ・回転しても画面を覆う）
        if (invert != null && invert.gameObject.activeSelf && cam != null)
        {
            Vector3 c = cam.transform.position;
            float z = gameBoard != null ? gameBoard.transform.position.z - 1f : 0f;
            invert.transform.position = new Vector3(c.x, c.y, z);
        }

        // 印の文字は画面で上向き
        foreach (Transform t in markTexts)
        {
            if (t != null) t.rotation = Quaternion.Euler(0f, 0f, camZ);
        }
    }

    // ------------------------------------------------------------
    //  色の反転
    // ------------------------------------------------------------
    void BuildInvert()
    {
        Shader shader = Resources.Load<Shader>("Shaders/InvertScreen");
        if (shader == null) shader = Shader.Find("UltimateShogi/InvertScreen");
        canInvert = shader != null && shader.isSupported;
        if (!canInvert) Debug.LogWarning("色の反転のシェーダー（Resources/Shaders/InvertScreen）が使えないので、時間停止中は画面を青く暗くするだけにします");

        invert = new GameObject("TimeStopInvert").AddComponent<SpriteRenderer>();
        invert.sprite = MagicSprites.Square;
        invert.sortingOrder = invertOrder;
        if (canInvert)
        {
            invertMaterial = new Material(shader);
            invert.sharedMaterial = invertMaterial;
        }
        float halfH = cam != null && cam.orthographic ? cam.orthographicSize : 5f;
        float halfW = halfH * (cam != null ? cam.aspect : 16f / 9f);
        // カメラが回っても揺れても覆えるよう、画面の対角線より大きい正方形にする
        float size = Mathf.Sqrt(halfW * halfW + halfH * halfH) * 2f + 4f;
        invert.transform.localScale = new Vector3(size, size, 1f);
        ApplyInvert(0f);
        invert.gameObject.SetActive(false);
    }

    // on なら少しずつ反転する（0.45 秒）。off ならすぐ戻す（ガラスが砕けた瞬間）
    void SetInverted(bool on)
    {
        if (invert == null) return;
        invertTween?.Kill();
        if (on)
        {
            invert.gameObject.SetActive(true);
            invertTween = DOVirtual.Float(invertAmount, 1f, 0.45f, ApplyInvert).SetEase(Ease.OutQuad).SetLink(gameObject);
        }
        else
        {
            ApplyInvert(0f);
            invert.gameObject.SetActive(false);
        }
    }

    void ApplyInvert(float amount)
    {
        invertAmount = amount;
        if (invert == null) return;
        invert.color = canInvert ? new Color(1f, 1f, 1f, amount) : WithAlpha(FallbackTint, FallbackTint.a * amount);
    }

    // ------------------------------------------------------------
    //  予定の駒（灰色にして、ほかの駒の下に残す）
    // ------------------------------------------------------------
    void SetHeld(GamePiece piece, bool on, bool gray)
    {
        if (piece == null) return;
        Transform t = piece.transform;
        if (on)
        {
            if (held.ContainsKey(piece)) return;
            DOTween.Kill(t, true); // 移動の演出が残っていれば終わらせる
            HeldLook look = new HeldLook { renderers = piece.GetComponentsInChildren<SpriteRenderer>(true) };
            look.colors = new Color[look.renderers.Length];
            look.orders = new int[look.renderers.Length];
            for (int i = 0; i < look.renderers.Length; i++)
            {
                SpriteRenderer sr = look.renderers[i];
                look.colors[i] = sr.color;
                look.orders[i] = sr.sortingOrder;
                sr.sortingOrder += heldOrderOffset;
                if (gray)
                {
                    float l = sr.color.grayscale * 0.7f;
                    sr.color = new Color(l, l, l, sr.color.a * 0.9f);
                }
            }
            held[piece] = look;
            return;
        }

        if (!held.TryGetValue(piece, out HeldLook prev)) return;
        held.Remove(piece);
        DOTween.Kill(t, false);
        for (int i = 0; i < prev.renderers.Length; i++)
        {
            SpriteRenderer sr = prev.renderers[i];
            if (sr == null) continue;
            sr.color = prev.colors[i];
            sr.sortingOrder = prev.orders[i];
        }
        Vector2 cell = GameHelper.CalcPanelLocation(piece.X, piece.Y);
        t.localPosition = new Vector3(cell.x, cell.y, t.localPosition.z);
    }

    // 予定の駒の上に別の駒が重なっていれば、予定の駒を画面で左上へ少しずらす
    void RefreshHeldOffsets()
    {
        if (gameBoard == null) return;
        List<GamePiece> gone = new List<GamePiece>();
        foreach (KeyValuePair<GamePiece, HeldLook> kv in held)
        {
            GamePiece p = kv.Key;
            if (p == null)
            {
                gone.Add(p);
                continue;
            }
            Vector2 cell = GameHelper.CalcPanelLocation(p.X, p.Y);
            Vector3 target = new Vector3(cell.x, cell.y, p.transform.localPosition.z);
            GamePiece on = gameBoard.GetPieceAt(p.X, p.Y);
            if (on != null && on != p) target += (ScreenUpLocal() - ScreenRightLocal()) * (9f * PX);
            p.transform.DOKill(false);
            p.transform.DOLocalMove(target, 0.2f).SetLink(p.gameObject);
        }
        foreach (GamePiece p in gone) held.Remove(p);
    }

    // ------------------------------------------------------------
    //  予定の印
    // ------------------------------------------------------------

    // upto 番目までを処理済みとして描く（state が null なら全部消す）
    void RenderMarks(TimeStopState ts, int upto)
    {
        if (marksRoot == null) return;
        for (int i = marksRoot.childCount - 1; i >= 0; i--) Destroy(marksRoot.GetChild(i).gameObject);
        markTexts.Clear();
        if (ts == null) return;

        for (int i = 0; i < ts.Pending.Count; i++)
        {
            TimeStopPending e = ts.Pending[i];
            if (i <= upto || TimeStopState.IsUndone(e)) continue;
            if (upto < 0 && e.Done) continue;
            switch (e.Kind)
            {
                case TimeStopKind.Capture:
                    Badge(e.At, 0, "取", Hex(0xff1744));
                    break;
                case TimeStopKind.Explode:
                {
                    int r = MatchConfig.ExplosionRadius;
                    Frame(Mathf.Max(0, e.At.x - r), Mathf.Min(8, e.At.x + r), Mathf.Max(0, e.At.y - r), Mathf.Min(8, e.At.y + r), Hex(0xff6d00), 0.12f, 3f);
                    Badge(e.At, 1, "爆", Hex(0xff6d00));
                    break;
                }
                case TimeStopKind.Vanish:
                    Badge(e.At, 2, "消", VanishColor(e.Reason));
                    break;
                case TimeStopKind.Promote:
                    if (e.Piece != null) Badge(new Vector2Int(e.Piece.X, e.Piece.Y), 3, "成", Hex(0xc62828));
                    break;
                case TimeStopKind.Cell:
                {
                    // Prototype の Game.js の予定の印（穴・壁・壊・炎）
                    string text;
                    Color color;
                    switch (e.CellEffect)
                    {
                        case TimeStopCellEffect.Trap: text = "穴"; color = Hex(0x7e57c2); break;
                        case TimeStopCellEffect.Wall: text = "壁"; color = Hex(0xa1887f); break;
                        case TimeStopCellEffect.Break: text = "壊"; color = Hex(0x8d6e63); break;
                        default: text = "炎"; color = Hex(0xff6d00); break;
                    }
                    Frame(e.At.x, e.At.x, e.At.y, e.At.y, color, 0.15f, 2f);
                    Badge(e.At, 4, text, color);
                    break;
                }
            }
        }
    }

    static Color VanishColor(string reason)
    {
        switch (reason)
        {
            case BoardCells.ReasonBurning: return Hex(0xff6d00);
            case BoardCells.ReasonTrap: return Hex(0x7e57c2);
            case BoardCells.ReasonBroken: return Hex(0x8d6e63);
            case MeteorEffect.Reason: return Hex(0xff5722);
            case CannonEffect.Reason: return Hex(0x00acc1);
            default: return Hex(0xff9100);
        }
    }

    // マスの角（0 = 左上 / 1 = 右上 / 2 = 左下 / 3 = 右下（画面で）/ 4 = 中央）に丸いバッジと 1 文字
    void Badge(Vector2Int cell, int corner, string text, Color color)
    {
        Vector2 c = GameHelper.CalcPanelLocation(cell.x, cell.y);
        Vector3 pos = new Vector3(c.x, c.y, 0f);
        if (corner < 4)
        {
            float k = 0.5f - 11f * PX;
            Vector3 right = ScreenRightLocal() * (corner % 2 == 1 ? k : -k);
            Vector3 up = ScreenUpLocal() * (corner < 2 ? k : -k);
            pos += right + up;
        }

        SpriteRenderer rim = CreateSprite("Badge", Disc, Color.white, markOrder + 1);
        rim.transform.localPosition = new Vector3(pos.x, pos.y, -0.001f);
        rim.transform.localScale = Vector3.one * (22f * PX);
        SpriteRenderer face = CreateSprite("BadgeFace", Disc, color, markOrder + 2);
        face.transform.localPosition = new Vector3(pos.x, pos.y, -0.002f);
        face.transform.localScale = Vector3.one * (19f * PX);

        GameObject textObj = new GameObject("BadgeText", typeof(RectTransform));
        textObj.transform.SetParent(marksRoot, false);
        textObj.transform.localPosition = new Vector3(pos.x, pos.y, -0.003f);
        TextMeshPro tmp = textObj.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = 12f * 0.153f; // NewPieceFx・BoardCellView と同じ換算
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.color = Color.white;
        tmp.outlineColor = new Color32(0, 0, 0, 255);
        tmp.outlineWidth = 0.2f;
        tmp.sortingOrder = markOrder + 3;
        tmp.rectTransform.sizeDelta = new Vector2(0.5f, 0.5f);
        markTexts.Add(textObj.transform);
    }

    // x0〜x1 段・y0〜y1 筋のマスを囲む枠（塗りはうっすら）
    void Frame(int x0, int x1, int y0, int y1, Color color, float fillAlpha, float linePx)
    {
        Vector2 a = GameHelper.CalcPanelLocation(x0, y0), b = GameHelper.CalcPanelLocation(x1, y1);
        Vector2 min = Vector2.Min(a, b) - Vector2.one * 0.5f, max = Vector2.Max(a, b) + Vector2.one * 0.5f;
        float inset = 3f * PX;
        min += Vector2.one * inset;
        max -= Vector2.one * inset;
        Vector2 size = max - min, center = (min + max) * 0.5f;

        SpriteRenderer fill = CreateSprite("FrameFill", MagicSprites.Square, WithAlpha(color, fillAlpha), markOrder);
        fill.transform.localPosition = new Vector3(center.x, center.y, 0f);
        fill.transform.localScale = new Vector3(size.x, size.y, 1f);
        float line = linePx * PX;
        Vector3[] pos =
        {
            new Vector3(center.x, max.y - line * 0.5f, -0.001f), new Vector3(center.x, min.y + line * 0.5f, -0.001f),
            new Vector3(max.x - line * 0.5f, center.y, -0.001f), new Vector3(min.x + line * 0.5f, center.y, -0.001f),
        };
        Vector3[] scale =
        {
            new Vector3(size.x, line, 1f), new Vector3(size.x, line, 1f),
            new Vector3(line, size.y, 1f), new Vector3(line, size.y, 1f),
        };
        for (int i = 0; i < 4; i++)
        {
            SpriteRenderer e = CreateSprite("FrameEdge", MagicSprites.Square, WithAlpha(color, 0.9f), markOrder);
            e.transform.localPosition = pos[i];
            e.transform.localScale = scale[i];
        }
    }

    SpriteRenderer CreateSprite(string name, Sprite sprite, Color color, int order)
    {
        SpriteRenderer sr = new GameObject(name).AddComponent<SpriteRenderer>();
        sr.transform.SetParent(marksRoot, false);
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    // 画面の右・上を盤のローカルの向きにしたもの（カメラが 180° 回っていても見た目の右・上）
    Vector3 ScreenRightLocal()
    {
        Vector3 v = cam != null ? cam.transform.right : Vector3.right;
        v = gameBoard != null ? gameBoard.transform.InverseTransformDirection(v) : v;
        v.z = 0f;
        return v.normalized;
    }

    Vector3 ScreenUpLocal()
    {
        Vector3 v = cam != null ? cam.transform.up : Vector3.up;
        v = gameBoard != null ? gameBoard.transform.InverseTransformDirection(v) : v;
        v.z = 0f;
        return v.normalized;
    }

    // 幅 1unit の白い円
    static Sprite Disc
    {
        get
        {
            if (disc != null) return disc;
            const int size = 64;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - size * 0.5f, dy = y + 0.5f - size * 0.5f;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(size * 0.5f - d));
                }
            }
            tex.SetPixels(pixels);
            tex.Apply();
            disc = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
            return disc;
        }
    }

    // ------------------------------------------------------------
    //  残り回数と「解除」ボタン（画面の上）
    // ------------------------------------------------------------
    void BuildHud()
    {
        GameObject panelObj = new GameObject("TimeStopHud", typeof(RectTransform));
        hud = panelObj.GetComponent<RectTransform>();
        hud.SetParent(canvas.transform, false);
        hud.anchorMin = new Vector2(0.5f, 1f);
        hud.anchorMax = new Vector2(0.5f, 1f);
        hud.pivot = new Vector2(0.5f, 1f);
        hud.anchoredPosition = new Vector2(0f, -hudTop);
        hud.sizeDelta = new Vector2(600f, 76f);
        Image bg = panelObj.AddComponent<Image>();
        bg.color = HudColor;
        bg.raycastTarget = false;
        Outline line = panelObj.AddComponent<Outline>();
        line.effectColor = WithAlpha(AccentColor, 0.8f);
        line.effectDistance = new Vector2(3f, -3f);

        // 左：「⏸ 時間停止中」
        TextMeshProUGUI title = CreateText("Title", hud, "時間停止中", 30f, AccentColor, TextAlignmentOptions.Left);
        Place(title.rectTransform, new Vector2(20f, 0f), new Vector2(190f, 60f));

        // 残り回数の円グラフ
        GameObject pieObj = new GameObject("Pie", typeof(RectTransform));
        RectTransform pieRt = pieObj.GetComponent<RectTransform>();
        pieRt.SetParent(hud, false);
        Place(pieRt, new Vector2(215f, 0f), new Vector2(46f, 46f));
        Image pieBg = pieObj.AddComponent<Image>();
        pieBg.sprite = Disc;
        pieBg.color = new Color(1f, 1f, 1f, 0.9f);
        pieBg.raycastTarget = false;
        GameObject fillObj = new GameObject("Fill", typeof(RectTransform));
        RectTransform fillRt = fillObj.GetComponent<RectTransform>();
        fillRt.SetParent(pieRt, false);
        fillRt.anchorMin = Vector2.zero;
        fillRt.anchorMax = Vector2.one;
        fillRt.offsetMin = new Vector2(3f, 3f);
        fillRt.offsetMax = new Vector2(-3f, -3f);
        pieFill = fillObj.AddComponent<Image>();
        pieFill.sprite = Disc;
        pieFill.color = AccentColor;
        pieFill.type = Image.Type.Filled;
        pieFill.fillMethod = Image.FillMethod.Radial360;
        pieFill.fillOrigin = (int)Image.Origin360.Top;
        pieFill.fillClockwise = true;
        pieFill.raycastTarget = false;

        // 「残り N 回」
        countText = CreateText("Count", hud, "", 34f, Color.white, TextAlignmentOptions.Center);
        Place(countText.rectTransform, new Vector2(275f, 0f), new Vector2(170f, 60f));

        // 「解除 [Q]」ボタン
        GameObject buttonObj = new GameObject("ReleaseButton", typeof(RectTransform));
        RectTransform brt = buttonObj.GetComponent<RectTransform>();
        brt.SetParent(hud, false);
        Place(brt, new Vector2(455f, 0f), new Vector2(130f, 52f));
        Image bImage = buttonObj.AddComponent<Image>();
        bImage.color = ReleaseColor;
        releaseButton = buttonObj.AddComponent<Button>();
        releaseButton.targetGraphic = bImage;
        ColorBlock colors = releaseButton.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.6f);
        releaseButton.colors = colors;
        Outline bLine = buttonObj.AddComponent<Outline>();
        bLine.effectColor = new Color(0f, 0f, 0f, 0.5f);
        bLine.effectDistance = new Vector2(2f, -2f);
        TextMeshProUGUI bLabel = CreateText("Label", brt, "解除  [Q]", 26f, Color.white, TextAlignmentOptions.Center);
        bLabel.rectTransform.anchorMin = Vector2.zero;
        bLabel.rectTransform.anchorMax = Vector2.one;
        bLabel.rectTransform.offsetMin = Vector2.zero;
        bLabel.rectTransform.offsetMax = Vector2.zero;
        releaseButton.onClick.AddListener(() => gameSystem.ReleaseTimeStop());

        hud.gameObject.SetActive(false);
    }

    // 残り回数の表示を更新する（bump なら数字を弾ませる）
    void RefreshHud(bool bump)
    {
        if (hud == null || gameSystem == null) return;
        TimeStopState ts = gameSystem.TimeStop;
        if (ts == null)
        {
            hud.gameObject.SetActive(false);
            shownRemaining = -1;
            return;
        }
        shownRemaining = ts.Remaining;
        countText.text = $"残り {ts.Remaining} 回";
        pieFill.fillAmount = ts.Total > 0 ? (float)ts.Remaining / ts.Total : 0f;
        if (bump)
        {
            RectTransform rt = countText.rectTransform;
            rt.DOKill(true);
            rt.localScale = Vector3.one * 1.5f;
            rt.DOScale(1f, 0.35f).SetEase(Ease.OutBack, 3f).SetLink(rt.gameObject);
        }
    }

    // 左端からの位置 left に、縦は中央に置く
    static void Place(RectTransform rt, Vector2 left, Vector2 size)
    {
        rt.anchorMin = new Vector2(0f, 0.5f);
        rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = left;
        rt.sizeDelta = size;
    }

    TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        GameObject obj = new GameObject(name, typeof(RectTransform));
        obj.transform.SetParent(parent, false);
        TextMeshProUGUI tmp = obj.AddComponent<TextMeshProUGUI>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size;
        tmp.fontStyle = FontStyles.Bold;
        tmp.color = color;
        tmp.alignment = alignment;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.raycastTarget = false;
        return tmp;
    }

    // スキルボタンのある Canvas（なければシーンの Canvas）
    Canvas FindUICanvas()
    {
        if (gameSystem != null)
        {
            foreach (Button button in gameSystem.SkillSlotButtons)
            {
                if (button == null) continue;
                Canvas c = button.GetComponentInParent<Canvas>();
                if (c != null) return c.rootCanvas;
            }
        }
        if (HandUIManager.Instance != null)
        {
            Canvas c = HandUIManager.Instance.GetComponentInParent<Canvas>();
            if (c != null) return c.rootCanvas;
        }
        return FindAnyObjectByType<Canvas>();
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
