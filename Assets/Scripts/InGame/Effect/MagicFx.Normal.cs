using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 通常魔法「魔力の火花」「ワープ」「ボム」の演出（Prototype の view/skillFx.js の spark・warp.out・warp.in・bomb.throw・bomb.blast と、
// Game.js の animateMove の style 'warp'）。MagicFx の続き（partial）
//
//  魔力の火花 … 魔法陣・光の粒・画面シェイク・「きらきら！」
//  ワープ     … 元のマスと行き先に魔法陣が開き、駒に光が吸い込まれる → 駒がその場で縦に縮んで消え、行き先で伸びて現れる
//                → 光が弾ける（衝撃波・光の粒・「ワープ！」）
//  ボム       … 駒が振りかぶり、導火線に火のついた爆弾 3 つを前方へ放物線で投げる（落下地点が赤く明滅）
//                → 投げたマスそれぞれで爆発（閃光・揺れは 1 回にまとめる。範囲全体が赤く光る。「ボム!!」）。駒の吹き飛びは SpecialMoveFx
public partial class MagicFx
{
    public static readonly Color SparkColor = Hex(0x7c4dff);
    public static readonly Color WarpColor = Hex(0xe040fb);
    public static readonly Color BombColor = Hex(0xff7043);

    // ワープ：魔法陣が開いてから駒が縮み始めるまで／縮み始めてから行き先に現れるまで（WarpEffect はこの分待つ）
    public const float WarpOutTime = 0.35f;
    public const float WarpMoveTime = 0.4f;
    // ボム：投げてから 3 つ目の爆弾が着弾するまで（BombEffect はこの分待ってから爆発させる）
    public const float BombFlightTime = BombFlight + BombStagger * 2f;
    private const float BombFlight = 0.6f;
    private const float BombStagger = 0.06f;

    // ------------------------------------------------------------
    //  魔力の火花（動作確認用）
    // ------------------------------------------------------------

    public void Spark(Vector2Int at, float time)
    {
        At(time, () =>
        {
            Vector3 pos = CellWorld(at.x, at.y);
            MagicCircle(pos, SparkColor, 1.3f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 40; burst.colors = new[] { SparkColor, Hex(0xb388ff), Color.white };
            burst.speed = 260f * PX; burst.life = 0.8f; burst.size = 0.25f;
            fx.Burst(pos, burst);
            fx.Shake(6f, 0.25f);
            PopText("きらきら！", pos + ScreenUp * (36f * PX), 34f, Color.white, Hex(0xb388ff), Hex(0x1a0033), 40f, 0.9f);
        });
    }

    // ------------------------------------------------------------
    //  ワープ
    // ------------------------------------------------------------

    // 元のマスと行き先に魔法陣が開き、駒に光が吸い込まれる
    public void WarpOut(Vector2Int from, Vector2Int to, float time)
    {
        At(time, () =>
        {
            Vector3 a = CellWorld(from.x, from.y);
            Vector3 b = CellWorld(to.x, to.y);
            MagicCircle(a, WarpColor, 1.1f);
            MagicCircle(b, WarpColor, 1.1f);
            Implode(a, 30, 70f * PX, new[] { WarpColor, Color.white, Hex(0x18ffff) }, 0.4f, 0.2f);
        });
    }

    // 瞬間移動（Prototype の animateMove の style 'warp'）：その場で縦に縮んで消え、行き先で伸びて現れる（WarpMoveTime 秒で現れる）
    public void WarpMove(GamePiece piece, Vector2Int to, float time)
    {
        if (piece == null) return;
        Vector2 location = GameHelper.CalcPanelLocation(to.x, to.y);
        At(time, () =>
        {
            if (piece == null) return;
            Transform t = piece.transform;
            Vector3 baseScale = t.localScale;
            Vector3 thin = new Vector3(baseScale.x * 0.25f, baseScale.y * 1.6f, baseScale.z);
            Sequence seq = DOTween.Sequence().SetLink(piece.gameObject);
            seq.Append(t.DOScale(thin, 0.18f).SetEase(Ease.InQuad));
            seq.Append(t.DOScale(new Vector3(0f, baseScale.y * 2f, baseScale.z), 0.06f));
            seq.AppendCallback(() => piece.MoveTo(location));
            seq.AppendInterval(WarpMoveTime - 0.24f);
            seq.AppendCallback(() => t.localScale = thin);
            seq.Append(t.DOScale(baseScale, 0.35f).SetEase(Ease.OutBack, 3f));
            seq.OnKill(() =>
            {
                if (t == null) return;
                t.localScale = baseScale;
                piece.MoveTo(location);
            });
        });
    }

    // 行き先に現れた駒から光が弾ける
    public void WarpIn(Vector2Int at, float time)
    {
        At(time, () =>
        {
            Vector3 pos = CellWorld(at.x, at.y);
            fx.Shockwave(pos, WarpColor, 2f, 0.4f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 28; burst.colors = new[] { WarpColor, Color.white, Hex(0x18ffff) };
            burst.speed = 220f * PX; burst.life = 0.6f; burst.size = 0.22f;
            fx.Burst(pos, burst);
            fx.Shake(5f, 0.2f);
            PopText("ワープ！", pos + ScreenUp * (36f * PX), 32f, Color.white, Hex(0xea80fc), Hex(0x2a0033), 40f, 0.9f);
        });
    }

    // ------------------------------------------------------------
    //  ボム
    // ------------------------------------------------------------

    // 駒が振りかぶり、導火線に火のついた爆弾を landing の各マスへ放物線で投げる（落下地点の予告つき。BombFlightTime 秒で全部着弾）
    public void BombThrow(GamePiece caster, List<Vector2Int> landing, float time)
    {
        if (caster == null || landing == null || landing.Count == 0) return;
        List<Vector2Int> cells = new List<Vector2Int>(landing);
        Vector2Int fromCell = new Vector2Int(caster.X, caster.Y);
        At(time, () =>
        {
            Vector3 from = CellWorld(fromCell.x, fromCell.y);

            // 落下地点の予告：赤く明滅する（着弾まで）
            Transform warn = AreaCells(cells, Hex(0xff1744), 0.35f, out SpriteRenderer[] warnCells);
            Tween blink = DOVirtual.Float(0.3f, 1f, 0.1f, a =>
            {
                foreach (SpriteRenderer sr in warnCells) sr.color = WithAlpha(Hex(0xff1744), 0.35f * a);
            }).SetLoops(-1, LoopType.Yoyo).SetLink(warn.gameObject);
            DOVirtual.DelayedCall(BombFlightTime, () =>
            {
                blink.Kill();
                if (warn != null) Destroy(warn.gameObject);
            }, false).SetLink(warn.gameObject);

            // 振りかぶる
            if (caster != null)
            {
                Transform t = caster.transform;
                Vector3 baseScale = t.localScale;
                t.localScale = baseScale * 1.3f;
                t.DOScale(baseScale, 0.4f).SetEase(Ease.OutBack, 3f)
                    .OnKill(() => { if (t != null) t.localScale = baseScale; })
                    .SetLink(caster.gameObject);
            }
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 12; burst.colors = new[] { BombColor, Color.white };
            burst.speed = 140f * PX; burst.life = 0.35f; burst.size = 0.18f;
            fx.Burst(from, burst);

            for (int i = 0; i < cells.Count; i++) ThrowBomb(from, CellWorld(cells[i].x, cells[i].y), i);
        });
    }

    // 爆弾 1 つ：from から to へ放物線を描いて飛び、回りながら導火線の先から火花を散らす
    void ThrowBomb(Vector3 from, Vector3 to, int index)
    {
        Transform bomb = new GameObject("Bomb").transform;
        bomb.position = new Vector3(from.x, from.y, from.z - 0.3f);
        SpriteRenderer sr = bomb.gameObject.AddComponent<SpriteRenderer>();
        sr.sprite = MagicSprites.Bomb;
        sr.sortingOrder = fxOrder + 5;
        bomb.localScale = Vector3.one * 0.6f;
        sr.enabled = false; // 投げる順にずらして出す

        float camZ = cam != null ? cam.transform.eulerAngles.z : 0f; // 画面の上を上にする
        float spin = (index % 2 == 1 ? -1f : 1f) * 270f;
        Vector3 up = ScreenUp;
        Color[] sparkColors = { Hex(0xffea00), Hex(0xff9100), Color.white };
        Vector3 fuseTip = new Vector3(12f * PX, 28f * PX, 0f);

        DOVirtual.Float(0f, 1f, BombFlight, k =>
        {
            if (bomb == null) return;
            sr.enabled = true;
            float arc = Mathf.Sin(Mathf.PI * k);
            Vector3 p = Vector3.Lerp(from, to, k) + up * (arc * 1.3f);
            bomb.position = new Vector3(p.x, p.y, from.z - 0.3f);
            bomb.localScale = Vector3.one * (0.6f + arc * 0.45f);
            bomb.rotation = Quaternion.Euler(0f, 0f, camZ + spin * k);
            PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
            o.color = sparkColors[Random.Range(0, sparkColors.Length)];
            o.life = 0.25f;
            o.size = Random.Range(0.08f, 0.16f);
            fx.Particle(bomb.TransformPoint(fuseTip), o);
        }).SetDelay(index * BombStagger).SetEase(Ease.Linear).SetLink(bomb.gameObject)
          .OnComplete(() => { if (bomb != null) Destroy(bomb.gameObject); });
    }

    // 投げたマスそれぞれで 3×3 の爆発（範囲全体が赤く光る。閃光・揺れは 1 回にまとめる）
    public void BombBlast(List<Vector2Int> landing, List<Vector2Int> area, float time)
    {
        List<Vector2Int> centers = new List<Vector2Int>(landing);
        List<Vector2Int> cells = new List<Vector2Int>(area);
        At(time, () =>
        {
            for (int i = 0; i < centers.Count; i++)
            {
                Vector3 p = CellWorld(centers[i].x, centers[i].y);
                // 火球が重なりすぎて白く飛ばないよう、少し小さく・ずらす
                At(i * 0.1f, () => { if (newPieceFx != null) newPieceFx.Explosion(p, 66f * 2.2f, true); });
            }
            if (newPieceFx != null) newPieceFx.Flash(Hex(0xfff3e0), 0.45f, 0.35f);

            Transform hot = AreaCells(cells, Hex(0xff3d00), 0.6f, out SpriteRenderer[] hotCells);
            DOVirtual.Float(1f, 0f, 0.9f, a =>
            {
                foreach (SpriteRenderer sr in hotCells) sr.color = WithAlpha(Hex(0xff3d00), 0.6f * a);
            }).SetLink(hot.gameObject).OnComplete(() => { if (hot != null) Destroy(hot.gameObject); });

            fx.Shake(30f, 0.9f);
            Vector3 center = Vector3.zero;
            foreach (Vector2Int c in cells) center += CellWorld(c.x, c.y);
            if (cells.Count > 0) center /= cells.Count;
            PopText("ボム!!", center - ScreenUp * (10f * PX), 80f, Hex(0xfff59d), Hex(0xd50000), Hex(0x2a0000), 40f, 1.1f);
        });
    }
}

public static partial class MagicSprites
{
    private static Sprite bomb;

    // ボムの爆弾（Prototype の skillFx.js の bomb.throw の図形。黒い玉・茶色の縁・ハイライト・口金・導火線。幅 64px、+Y が上）。
    // 導火線の先は (12, 28)px
    public static Sprite Bomb => bomb != null ? bomb : (bomb = Make(64, 32f * PX, (ux, uy) =>
    {
        Vector2 p = new Vector2(ux, uy) * 32f;
        Color c = new Color(0f, 0f, 0f, 0f);

        // 導火線（(0,21) から (12,28) へ、(6,30) を制御点にした曲線）
        float fuse = float.MaxValue;
        Vector2 prev = new Vector2(0f, 21f);
        for (int i = 1; i <= 8; i++)
        {
            float t = i / 8f;
            Vector2 q = (1 - t) * (1 - t) * new Vector2(0f, 21f) + 2 * (1 - t) * t * new Vector2(6f, 30f) + t * t * new Vector2(12f, 28f);
            fuse = Mathf.Min(fuse, SegmentDistance(p, prev, q));
            prev = q;
        }
        c = Over(c, Hex(0xd7ccc8), Line(fuse, 1.5f));

        // 口金
        bool cap = p.x >= -4f && p.x <= 4f && p.y >= 15f && p.y <= 21f;
        if (cap) c = Over(c, Hex(0x8d6e63), 1f);

        // 本体（縁 3px）とハイライト
        float d = p.magnitude;
        c = Over(c, Hex(0x5d4037), Mathf.Clamp01(17.5f + 0.5f - d));
        c = Over(c, Hex(0x212121), Mathf.Clamp01(16f + 0.5f - d));
        float h = (p - new Vector2(-5f, 6f)).magnitude;
        c = Over(c, Color.white, 0.35f * Mathf.Clamp01(5f + 0.5f - h));
        return c;
    }, FilterMode.Bilinear));
}
