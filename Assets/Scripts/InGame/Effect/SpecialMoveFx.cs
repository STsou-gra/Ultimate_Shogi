using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 必殺技の演出（Prototype の view/events.js の specialCast・specialReady・ghost・landed・pushOut・pushBlocked と、
// view/skillFx.js の special.beams・ki.shock、view/fx.js の ghost・beam）
//
//  発動 … 駒が光って弾む（衝撃波・光の粒）→ カットイン（CutInEffect。GameSystem が待つ）→「突撃！」
//  効果 … SpecialContext が「発動からの秒数」を付けて呼ぶ。盤面のデータはもう変わっているので、ここでは見た目だけを時刻どおりに再生する
//  倒された駒 … 駒の GameObject はすぐ破棄されるので写しを作り、当たる瞬間までその場に残してから弾き飛ばす
//               （爆発で消えた駒は NewPieceFx が吹き飛ばす）
//
// シーンに置かなくても GameSystem が自動で追加する
public class SpecialMoveFx : MonoBehaviour
{
    private const float PX = 1f / 66f; // プロトタイプの1マス(66px) = Unity の1マス

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private PieceFx fx;
    [SerializeField] private PieceMoveAnimator animator;
    [SerializeField] private NewPieceFx newPieceFx; // 写し・閃光・文字を借りる

    [Header("Sorting")]
    [SerializeField] private int fxOrder = 210;

    private static readonly Color Gold = SpecialMove.Hex(0xffd54a);

    private Sprite squareSprite;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (fx == null) fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        if (animator == null) animator = FindAnyObjectByType<PieceMoveAnimator>();
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        squareSprite = CreateSquareSprite();

        if (gameSystem != null)
        {
            gameSystem.PieceDefeated += OnPieceDefeated;
            gameSystem.SpecialReady += OnSpecialReady;
        }
    }

    void OnDestroy()
    {
        if (gameSystem != null)
        {
            gameSystem.PieceDefeated -= OnPieceDefeated;
            gameSystem.SpecialReady -= OnSpecialReady;
        }
    }

    // time 秒後に action を行う
    public void At(float time, System.Action action)
    {
        DOVirtual.DelayedCall(Mathf.Max(0f, time), () => action(), false).SetLink(gameObject);
    }

    // ------------------------------------------------------------
    //  発動・解放
    // ------------------------------------------------------------

    // 発動の瞬間（カットインの前）：駒が光って弾む
    public void PlayCast(GamePiece caster, SpecialMove move)
    {
        if (caster == null || fx == null) return;
        Vector3 pos = caster.transform.position;
        fx.Shockwave(pos, move.color, 3f, 0.5f);
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 40; burst.colors = new[] { move.color, Color.white }; burst.speed = 260f * PX; burst.life = 0.6f;
        fx.Burst(pos, burst);

        Transform t = caster.transform;
        Vector3 baseScale = t.localScale;
        t.localScale = baseScale * 1.5f;
        t.DOScale(baseScale, 0.4f).SetEase(Ease.OutElastic, 1f, 0.4f)
            .OnKill(() => { if (t != null) t.localScale = baseScale; })
            .SetLink(caster.gameObject);
    }

    // カットインのあと：技名が駒の上に飛び出す
    public void PopName(GamePiece caster, SpecialMove move)
    {
        if (caster == null || newPieceFx == null) return;
        newPieceFx.PopText($"{move.moveName}！", caster.transform.position + ScreenUp * (40f * PX), 40f, Color.white, move.color, SpecialMove.Hex(0x1a0000), 40f, 1f);
    }

    // 移動回数がちょうど必要数に達した：「必殺技 解放！」
    void OnSpecialReady(GamePiece piece)
    {
        if (piece == null) return;
        // 移動の見た目が終わってから出す
        At(0.3f, () =>
        {
            if (piece == null) return;
            Vector3 pos = piece.transform.position;
            if (newPieceFx != null) newPieceFx.PopText("必殺技 解放！", pos + ScreenUp * (40f * PX), 30f, Color.white, Gold, SpecialMove.Hex(0x2a1a00), 40f, 1f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 30; burst.colors = new[] { Gold, Color.white }; burst.speed = 200f * PX; burst.life = 0.7f;
            fx.Burst(pos, burst);
        });
    }

    // ------------------------------------------------------------
    //  効果の見た目（SpecialContext から呼ばれる。time は発動からの秒数）
    // ------------------------------------------------------------

    // 駒が (x, y) へ動く
    public void MovePiece(GamePiece piece, int x, int y, PieceMoveStyle style, float duration, float time)
    {
        Vector2 location = GameHelper.CalcPanelLocation(x, y);
        At(time, () =>
        {
            if (piece == null) return;
            if (animator != null) animator.PlayMove(piece, location, style, 0, duration);
            else piece.MoveTo(location);
        });
    }

    // 着地の衝撃
    public void Landed(GamePiece piece, int power, float time)
    {
        At(time, () =>
        {
            if (piece != null && animator != null) animator.LandFx(piece, power);
        });
    }

    // 残像（駒の形の光が膨らみながら消える）
    public void Afterimage(GamePiece piece, Color color, float time)
    {
        At(time, () =>
        {
            if (piece == null) return;
            GameObject ghost = CopyVisual(piece);
            SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
            foreach (SpriteRenderer sr in sprites) sr.color = new Color(color.r, color.g, color.b, 0.65f);
            Transform gt = ghost.transform;
            Sequence seq = DOTween.Sequence().SetLink(ghost);
            seq.Insert(0f, gt.DOScale(gt.localScale * 1.25f, 0.4f));
            seq.Insert(0f, DOVirtual.Float(0.65f, 0f, 0.4f, a =>
            {
                foreach (SpriteRenderer sr in sprites)
                {
                    if (sr != null) sr.color = new Color(color.r, color.g, color.b, a);
                }
            }));
            seq.OnComplete(() => Destroy(ghost));
        });
    }

    // ぶつかって止まる（押し出し先が塞がっている・取りが防がれた）
    public void Bump(GamePiece piece, Vector2Int dir, float time)
    {
        At(time, () =>
        {
            if (piece == null) return;
            Transform t = piece.transform;
            Vector3 basePos = (Vector3)GameHelper.CalcPanelLocation(piece.X, piece.Y);
            basePos.z = t.localPosition.z;
            Vector3 offset = (Vector3)LocalDirection(dir) * (10f * PX);
            t.DOLocalMove(basePos + offset, 0.06f).SetLoops(2, LoopType.Yoyo)
                .OnKill(() => { if (t != null) t.localPosition = basePos; })
                .SetLink(piece.gameObject);
        });
    }

    // 盤外へ押し出される：「場外！」
    public void PushOut(GamePiece piece, float time)
    {
        if (piece == null) return;
        Vector3 pos = piece.transform.position;
        At(time, () =>
        {
            if (newPieceFx != null) newPieceFx.PopText("場外！", pos + ScreenUp * (30f * PX), 30f, Color.white, Gold, SpecialMove.Hex(0x2a1a00), 40f, 0.9f);
        });
    }

    // 金：周囲への衝撃波
    public void Shock(Vector2Int cell, float time)
    {
        Vector3 pos = CellWorld(cell.x, cell.y);
        At(time, () =>
        {
            fx.Shockwave(pos, Gold, 5f, 0.6f);
            fx.Shockwave(pos, Color.white, 3.5f, 0.45f);
            fx.Shake(12f, 0.4f);
        });
    }

    // 角・飛：光線を放つ（着弾の取りは 0.3 秒後）
    public void Beams(Vector2Int from, List<Vector2Int> ends, Color color, float time)
    {
        Vector3 origin = CellWorld(from.x, from.y);
        List<Vector3> targets = new List<Vector3>();
        foreach (Vector2Int e in ends) targets.Add(CellWorld(e.x, e.y));
        At(time, () =>
        {
            fx.Shockwave(origin, color, 3f, 0.5f);
            foreach (Vector3 to in targets) Beam(origin, to, color);
            if (newPieceFx != null) newPieceFx.Flash(color, 0.35f, 0.3f);
            fx.Shake(12f, 0.45f);
        });
    }

    // 太い光線（Prototype の fx.beam：外側のぼかし・本体・白い芯 → 明滅して消える。光線に沿った粒子、先端の火花）
    void Beam(Vector3 from, Vector3 to, Color color)
    {
        Vector3 d = to - from;
        d.z = 0f;
        float len = d.magnitude;
        if (len < 0.001f) return;

        GameObject root = new GameObject("SpecialBeam");
        root.transform.position = (from + to) * 0.5f;
        root.transform.rotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

        float[] widths = { 30f * PX, 14f * PX, 5f * PX };
        Color[] colors = { WithAlpha(color, 0.22f), WithAlpha(color, 0.6f), Color.white };
        List<SpriteRenderer> layers = new List<SpriteRenderer>();
        for (int i = 0; i < widths.Length; i++)
        {
            GameObject obj = new GameObject("Layer");
            obj.transform.SetParent(root.transform, false);
            obj.transform.localScale = new Vector3(len, widths[i], 1f);
            SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = squareSprite;
            sr.color = colors[i];
            sr.sortingOrder = fxOrder + i;
            layers.Add(sr);
        }

        System.Action<float> setAlpha = a =>
        {
            for (int i = 0; i < layers.Count; i++) layers[i].color = WithAlpha(colors[i], colors[i].a * a);
        };
        Sequence seq = DOTween.Sequence().SetLink(root);
        seq.Append(DOVirtual.Float(1f, 0.4f, 0.05f, v => setAlpha(v)).SetLoops(4, LoopType.Yoyo));
        seq.Append(DOVirtual.Float(1f, 0f, 0.45f, v => setAlpha(v)));
        seq.OnComplete(() => Destroy(root));

        int n = Mathf.CeilToInt(len / (14f * PX));
        for (int i = 0; i < n; i++)
        {
            fx.Particle(Vector3.Lerp(from, to, Random.value), new PieceFx.ParticleOptions
            {
                velocity = new Vector2(Random.Range(-60f, 60f), Random.Range(-60f, 60f)) * PX,
                color = Random.value < 0.5f ? color : Color.white,
                life = Random.Range(0.3f, 0.7f), size = Random.Range(0.1f, 0.25f), drag = 0.95f, alpha = 1f, shrink = true,
            });
        }
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 16; burst.colors = new[] { color, Color.white }; burst.speed = 220f * PX; burst.life = 0.5f;
        fx.Burst(to, burst);
    }

    // ------------------------------------------------------------
    //  倒された駒（取り・炎上・落とし穴。爆発は NewPieceFx）
    // ------------------------------------------------------------
    void OnPieceDefeated(PieceDefeat defeat)
    {
        SpecialContext ctx = gameSystem != null ? gameSystem.ActiveSpecial : null;
        if (ctx == null || newPieceFx == null) return;
        if (defeat.Kind == DefeatKind.Vanish && defeat.Reason == GameSystem.ReasonExplosion) return;
        GamePiece source = defeat.Piece;
        if (source == null) return;

        GameObject ghost = newPieceFx.CreateGhost(source, true);
        if (ghost == null) return;
        Transform gt = ghost.transform;
        SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) baseColors[i] = sprites[i].color;
        Vector3 baseScale = gt.localScale;
        float hitTime = Mathf.Max(0f, ctx.HitTime);
        Vector2Int dir = ctx.HitDirection;

        Sequence seq = DOTween.Sequence().SetLink(ghost);

        // 倒されたマスまで（突進の途中で消える駒など。もうそのマスにいればその場で待つ）
        Vector2 cell = GameHelper.CalcPanelLocation(defeat.X, defeat.Y);
        Vector3 cellLocal = new Vector3(cell.x, cell.y, gt.localPosition.z);
        if ((gt.localPosition - cellLocal).sqrMagnitude > 0.0001f && hitTime > 0f) seq.Append(gt.DOLocalMove(cellLocal, hitTime).SetEase(Ease.Linear));
        else seq.AppendInterval(hitTime);

        System.Func<float, Tween> fade = length => DOVirtual.Float(1f, 0f, length, a =>
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) sprites[i].color = WithAlpha(baseColors[i], baseColors[i].a * a);
            }
        });

        if (dir == Vector2Int.zero)
        {
            // 炎上・落とし穴：その場で縮んで消える
            const float dur = 0.45f;
            seq.Append(gt.DOScale(baseScale * 0.2f, dur).SetEase(Ease.InQuad));
            seq.Join(gt.DOLocalRotate(new Vector3(0f, 0f, 180f), dur, RotateMode.LocalAxisAdd));
            seq.Join(fade(dur));
        }
        else
        {
            // 取り：当たった向きへ弾き飛ばされる
            const float dur = 0.5f;
            seq.AppendCallback(() =>
            {
                if (ghost == null) return;
                PieceFx.BurstOptions hit = PieceFx.BurstOptions.Default;
                hit.count = 18; hit.colors = new[] { Color.white, Gold }; hit.speed = 240f * PX; hit.life = 0.45f;
                fx.Burst(gt.position, hit);
                fx.Shake(6f, 0.2f);
            });
            Vector3 end = cellLocal + (Vector3)(LocalDirection(dir) * (80f * PX)) + LocalScreenUp(gt) * (20f * PX);
            seq.Append(gt.DOLocalMove(end, dur).SetEase(Ease.OutQuad));
            seq.Join(gt.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-200f, 200f)), dur, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));
            seq.Join(gt.DOScale(baseScale * 0.6f, dur));
            seq.Join(fade(dur).SetEase(Ease.InQuad));
        }
        seq.OnComplete(() => Destroy(ghost));
    }

    // ------------------------------------------------------------
    //  共通
    // ------------------------------------------------------------

    // 駒の見た目だけの写し（今の位置のまま。駒の移動の演出は止めない）
    static GameObject CopyVisual(GamePiece source)
    {
        Transform st = source.transform;
        GameObject copy = Instantiate(source.gameObject, st.parent);
        copy.name = $"{source.name}_Afterimage";
        foreach (GamePiece gp in copy.GetComponentsInChildren<GamePiece>(true)) DestroyImmediate(gp);
        copy.transform.localPosition = st.localPosition;
        copy.transform.localScale = st.localScale;
        return copy;
    }

    // 盤の向き (dx = 段, dy = 筋) を盤のローカル座標の向きに直す（CalcPanelLocation は x ← 筋, y ← 段）
    static Vector2 LocalDirection(Vector2Int dir)
    {
        Vector2 v = new Vector2(dir.y, dir.x);
        return v.sqrMagnitude > 0f ? v.normalized : Vector2.zero;
    }

    Vector3 CellWorld(int x, int y)
    {
        Vector2 location = GameHelper.CalcPanelLocation(x, y);
        if (gameBoard == null) return location;
        return gameBoard.transform.TransformPoint(new Vector3(location.x, location.y, 0f));
    }

    Vector3 ScreenUp
    {
        get
        {
            Camera cam = Camera.main;
            return cam != null ? cam.transform.up : Vector3.up;
        }
    }

    Vector3 LocalScreenUp(Transform t)
    {
        Vector3 up = t.parent != null ? t.parent.InverseTransformDirection(ScreenUp) : ScreenUp;
        up.z = 0f;
        return up.normalized;
    }

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }

    // 白い正方形（幅 1unit）
    static Sprite CreateSquareSprite()
    {
        const int size = 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }
}
