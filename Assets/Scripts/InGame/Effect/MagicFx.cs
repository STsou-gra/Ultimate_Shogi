using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 魔法スキルの演出（Prototype の view/events.js の magicCast・shieldGiven・shieldBroken・bounce、
// view/skillFx.js の pitfall.cast・pitfall.set・sword.slash、view/fx.js の magicCircle・implode・shards・slash）
//
//  発動     … 対象のマスに魔法陣（GameSystem がこのあとカットインを出す）
//  盾       … 魔法陣・光の粒・「盾！」。水色のバリアは GameTickEffects が駒に重ねて出す
//  盾が割れる … 破片・衝撃波・閃光・画面シェイク・「GUARD!」。取ろうとして動いた駒はバリアにぶつかって跳ね返る
//  剣       … 駒が構えて弾む → 前方へ三日月形の斬撃の弧（刃先から火花）→ 閃光・シェイク・範囲の駒に火花
//  落とし穴 … 魔法陣に闇の粒が吸い込まれる → 穴が開く（衝撃波・土くれ・「落とし穴 設置！」。穴の見た目は BoardCellView）
//
// 盤面のデータはすぐ変わるので、見た目は SkillContext.Timeline の時刻（発動からの秒数）に合わせて再生する。
// シーンに置かなくても GameSystem が自動で追加する
public class MagicFx : MonoBehaviour
{
    private const float PX = 1f / 66f; // プロトタイプの1マス(66px) = Unity の1マス

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private PieceFx fx;
    [SerializeField] private NewPieceFx newPieceFx; // 閃光・文字を借りる

    [Header("Sorting")]
    [SerializeField] private int fxOrder = 210; // 駒(10)・移動中の駒(110)・パーティクル(200)より手前

    // 色（Prototype の各魔法の color と PLAYER の色）
    public static readonly Color ShieldColor = Hex(0x4fc3f7);
    public static readonly Color ShieldLightColor = Hex(0x80deea);
    public static readonly Color ShieldPaleColor = Hex(0xe1f5fe);
    public static readonly Color SwordColor = Hex(0x90caf9);
    public static readonly Color PitfallColor = Hex(0x7e57c2);
    private static readonly Color Player1Color = Hex(0x4fc3ff);
    private static readonly Color Player2Color = Hex(0xff5277);
    private static readonly Color[] DirtColors = { Hex(0x5d4037), Hex(0x3e2723), Hex(0x8d6e63) };

    private Camera cam;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (fx == null) fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        cam = Camera.main;

        if (gameSystem != null)
        {
            gameSystem.ShieldGiven += OnShieldGiven;
            gameSystem.ShieldBroken += OnShieldBroken;
        }
    }

    void OnDestroy()
    {
        if (gameSystem != null)
        {
            gameSystem.ShieldGiven -= OnShieldGiven;
            gameSystem.ShieldBroken -= OnShieldBroken;
        }
    }

    // time 秒後に action を行う（0 以下ならすぐ）
    void At(float time, System.Action action)
    {
        if (time <= 0f)
        {
            action();
            return;
        }
        DOVirtual.DelayedCall(time, () => action(), false).SetLink(gameObject);
    }

    // ------------------------------------------------------------
    //  発動
    // ------------------------------------------------------------

    // カットインの前：対象のマス（対象を選ばない魔法は盤の中央）に魔法陣
    public void PlayCast(SkillDefinition skill, Vector2Int? focus)
    {
        if (skill == null) return;
        Vector2Int cell = focus ?? new Vector2Int(4, 4);
        MagicCircle(CellWorld(cell.x, cell.y), skill.color, focus.HasValue ? 1.1f : 1.6f);
    }

    // ------------------------------------------------------------
    //  盾
    // ------------------------------------------------------------

    void OnShieldGiven(GamePiece piece)
    {
        if (piece == null) return;
        Vector3 pos = piece.transform.position;
        MagicCircle(pos, ShieldColor, 1.1f);
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 24; burst.colors = new[] { ShieldColor, ShieldLightColor, Color.white };
        burst.speed = 180f * PX; burst.life = 0.6f; burst.size = 0.2f;
        fx.Burst(pos, burst);
        PopText("盾！", pos + ScreenUp * (36f * PX), 36f, Color.white, ShieldColor, Hex(0x01303f), 40f, 0.9f);
    }

    // 盾が取りを防いで割れた（攻撃が届く info.Delay 秒後に割れる。バリアはそれまで残す）
    void OnShieldBroken(ShieldBreak info)
    {
        GamePiece piece = info.Piece;
        if (piece == null) return;
        piece.barrierHoldUntil = Time.time + info.Delay;
        At(info.Delay, () =>
        {
            if (piece == null) return;
            Vector3 pos = piece.transform.position;
            Shards(pos, 18, new[] { ShieldColor, ShieldLightColor, ShieldPaleColor }, 26f * PX, 90f * PX);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 20; burst.colors = new[] { ShieldLightColor, Color.white };
            burst.speed = 240f * PX; burst.life = 0.5f; burst.size = 0.2f;
            fx.Burst(pos, burst);
            fx.Shockwave(pos, ShieldColor, 2.2f, 0.4f);
            if (newPieceFx != null) newPieceFx.Flash(ShieldColor, 0.2f, 0.2f);
            fx.Shake(8f, 0.25f);
            PopText("GUARD!", pos + ScreenUp * (34f * PX), 42f, Color.white, ShieldColor, Hex(0x01303f), 40f, 0.9f);
        });
    }

    // 取ろうとした駒が盾にぶつかって元のマスへ跳ね返る（Prototype の stepPiece の reach: 0.55 → bounce）。
    // reach 秒かけて移動先の 55% まで進み、跳ね返って戻る
    public void Bounce(GamePiece piece, int toX, int toY, float reach)
    {
        if (piece == null) return;
        Transform t = piece.transform;
        DOTween.Kill(t, true);
        Vector2 from = GameHelper.CalcPanelLocation(piece.X, piece.Y);
        Vector2 to = GameHelper.CalcPanelLocation(toX, toY);
        Vector3 basePos = new Vector3(from.x, from.y, t.localPosition.z);
        Vector3 hit = Vector3.Lerp(basePos, new Vector3(to.x, to.y, basePos.z), 0.55f);

        // 移動中は手前に描く（PieceMoveAnimator と同じ）
        SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
        int[] orders = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            orders[i] = renderers[i].sortingOrder;
            renderers[i].sortingOrder += 100;
        }

        Sequence seq = DOTween.Sequence().SetId(t).SetLink(piece.gameObject);
        seq.Append(t.DOLocalMove(hit, Mathf.Max(0.05f, reach)).SetEase(Ease.InQuad));
        seq.AppendCallback(() =>
        {
            if (piece == null) return;
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 10; burst.colors = new[] { ShieldLightColor, Color.white };
            burst.speed = 160f * PX; burst.life = 0.35f; burst.size = 0.16f;
            fx.Burst(t.position, burst);
        });
        seq.Append(t.DOLocalMove(basePos, 0.22f).SetEase(Ease.OutBack, 2f));
        seq.OnKill(() =>
        {
            if (t != null) t.localPosition = basePos;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].sortingOrder = orders[i];
            }
        });
    }

    // ------------------------------------------------------------
    //  剣
    // ------------------------------------------------------------

    // 構え → 前方へ斬撃の弧 → 閃光・シェイク・範囲の駒に火花（time は発動からの秒数）。
    // forward は前方（Unity の X の向き。+1 / -1）
    public void SwordSlash(GamePiece caster, int forward, List<GamePiece> hits, float time)
    {
        if (caster == null) return;
        Vector2 casterLocal = GameHelper.CalcPanelLocation(caster.X, caster.Y);
        List<Vector3> hitPositions = new List<Vector3>();
        foreach (GamePiece q in hits)
        {
            if (q != null) hitPositions.Add(CellWorld(q.X, q.Y));
        }

        At(time, () =>
        {
            if (caster != null)
            {
                Transform t = caster.transform;
                Vector3 baseScale = t.localScale;
                t.localScale = baseScale * 1.35f;
                t.DOScale(baseScale, 0.45f).SetEase(Ease.OutElastic, 1f, 0.4f)
                    .OnKill(() => { if (t != null) t.localScale = baseScale; })
                    .SetLink(caster.gameObject);
            }
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 18; burst.colors = new[] { SwordColor, Color.white };
            burst.speed = 160f * PX; burst.life = 0.4f; burst.size = 0.2f;
            fx.Burst(BoardWorld(casterLocal), burst);
        });

        // 盤の向き：Unity の X（段）は盤のローカル座標の y（GameHelper.CalcPanelLocation）
        float angle = forward > 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
        At(time + SwordEffect.WindUp, () => Slash(casterLocal, angle, SwordColor, SwordEffect.SweepTime));
        At(time + SwordEffect.WindUp + SwordEffect.SweepTime, () =>
        {
            if (newPieceFx != null) newPieceFx.Flash(SwordColor, 0.3f, 0.25f);
            fx.Shake(14f, 0.4f);
            foreach (Vector3 p in hitPositions)
            {
                PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
                burst.count = 14; burst.colors = new[] { SwordColor, Color.white };
                burst.speed = 240f * PX; burst.life = 0.4f; burst.size = 0.18f;
                fx.Burst(p, burst);
            }
        });
    }

    // 斬撃の弧（Prototype の fx.slash）：盤のローカル座標 center を中心に、angle の向きへ ±spread の三日月形を
    // duration 秒で描き切り、広がりながら消える。刃先から接線方向に火花
    void Slash(Vector2 center, float angle, Color color, float duration)
    {
        if (gameBoard == null) return;
        const float R = 1.2f;            // CELL * 1.2
        const float W = 30f * PX;        // 弧の太さ
        const float spread = 1.15f;
        const int n = 28;
        float a0 = angle - spread, a1 = angle + spread;

        Transform root = new GameObject("SwordSlash").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(center.x, center.y, -0.1f);

        // 外側のぼかし・本体・白い芯の 3 層。弧を n 個の細い四角に分け、刃先が通ったところから見せる
        float[] widths = { W * 1.8f, W, W * 0.4f };
        Color[] colors = { WithAlpha(color, 0.3f), WithAlpha(color, 0.75f), Color.white };
        SpriteRenderer[,] segments = new SpriteRenderer[3, n];
        float segLength = R * (a1 - a0) / n * 1.2f;
        for (int layer = 0; layer < 3; layer++)
        {
            for (int i = 0; i < n; i++)
            {
                float s = (i + 0.5f) / n;
                float a = a0 + (a1 - a0) * s;
                float w = widths[layer] * Mathf.Sin(Mathf.PI * s);
                SpriteRenderer sr = new GameObject("Seg").AddComponent<SpriteRenderer>();
                sr.sprite = MagicSprites.Square;
                sr.sortingOrder = fxOrder + layer;
                sr.color = colors[layer];
                Transform st = sr.transform;
                st.SetParent(root, false);
                st.localPosition = new Vector3(Mathf.Cos(a) * R, Mathf.Sin(a) * R, -0.001f * layer);
                st.localRotation = Quaternion.Euler(0f, 0f, a * Mathf.Rad2Deg + 90f);
                st.localScale = new Vector3(segLength, Mathf.Max(0.005f, w), 1f);
                sr.gameObject.SetActive(false);
                segments[layer, i] = sr;
            }
        }

        int shown = 0;
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(0f, 1f, duration, k =>
        {
            int upTo = Mathf.Clamp(Mathf.CeilToInt(k * n), 0, n);
            for (; shown < upTo; shown++)
            {
                for (int layer = 0; layer < 3; layer++) segments[layer, shown].gameObject.SetActive(true);
            }
            // 刃先から接線方向に火花
            float a = a0 + (a1 - a0) * k;
            Vector2 tip = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R;
            for (int j = 0; j < 2; j++)
            {
                float sp = Random.Range(120f, 300f) * PX;
                Vector2 v = new Vector2(-Mathf.Sin(a), Mathf.Cos(a)) * sp + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(0f, 80f) * PX;
                PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
                o.color = Random.value < 0.5f ? color : Color.white;
                o.velocity = BoardWorldVector(v);
                o.life = Random.Range(0.25f, 0.5f);
                o.size = Random.Range(0.12f, 0.24f);
                fx.Particle(BoardWorld(tip), o);
            }
        }).SetEase(Ease.OutQuad));
        seq.Append(root.DOScale(1.15f, 0.35f));
        seq.Join(DOVirtual.Float(1f, 0f, 0.35f, k =>
        {
            for (int layer = 0; layer < 3; layer++)
            {
                for (int i = 0; i < n; i++)
                {
                    SpriteRenderer sr = segments[layer, i];
                    if (sr != null) sr.color = WithAlpha(colors[layer], colors[layer].a * k);
                }
            }
        }));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // ------------------------------------------------------------
    //  落とし穴
    // ------------------------------------------------------------

    // 魔法陣に闇の粒が吸い込まれる
    public void PitfallCast(Vector2Int cell, PlayerType owner, float time)
    {
        At(time, () =>
        {
            Vector3 pos = CellWorld(cell.x, cell.y);
            MagicCircle(pos, PitfallColor, 1.1f);
            Implode(pos, 36, 80f * PX, new[] { PitfallColor, Hex(0x311b92), PlayerColor(owner) }, 0.5f, 0.2f);
        });
    }

    // 穴が開く（衝撃波・土くれ・「落とし穴 設置！」）
    public void PitfallSet(Vector2Int cell, PlayerType owner, float time)
    {
        At(time, () =>
        {
            Vector3 pos = CellWorld(cell.x, cell.y);
            fx.Shockwave(pos, PlayerColor(owner), 1.6f, 0.4f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 14; burst.colors = DirtColors;
            burst.speed = 150f * PX; burst.life = 0.6f; burst.size = 0.22f; burst.gravity = 380f * PX;
            fx.Burst(pos, burst);
            fx.Shake(4f, 0.2f);
            PopText("落とし穴 設置！", pos + ScreenUp * (36f * PX), 30f, Color.white, Hex(0xb388ff), Hex(0x1a0033), 40f, 0.9f);
        });
    }

    // ------------------------------------------------------------
    //  共通の部品
    // ------------------------------------------------------------

    // 魔法陣（Prototype の fx.magicCircle）：回転しながら広がって消える。中心から光の粒
    public void MagicCircle(Vector3 pos, Color color, float scale = 1f)
    {
        Transform root = new GameObject("MagicCircle").transform;
        root.position = new Vector3(pos.x, pos.y, pos.z - 0.2f);
        SpriteRenderer colored = CreateChild(root, MagicSprites.CircleColored, color, fxOrder);
        SpriteRenderer white = CreateChild(root, MagicSprites.CircleWhite, Color.white, fxOrder + 1);

        root.localScale = Vector3.one * (0.2f * scale);
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Insert(0f, root.DOScale(scale, 0.35f).SetEase(Ease.OutBack, 2f));
        seq.Insert(0f, root.DOLocalRotate(new Vector3(0f, 0f, 180f), 1.1f, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));
        seq.Insert(0.7f, DOVirtual.Float(1f, 0f, 0.4f, a =>
        {
            colored.color = WithAlpha(color, a);
            white.color = WithAlpha(Color.white, a);
        }));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });

        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 16; burst.colors = new[] { color, Color.white };
        burst.speed = 140f * PX; burst.life = 0.6f; burst.size = 0.2f;
        fx.Burst(pos, burst);
    }

    // 周りから中心へ吸い込まれる粒（Prototype の fx.implode。寿命が尽きる瞬間に中心へ届く）
    void Implode(Vector3 pos, int count, float radius, Color[] colors, float life, float size)
    {
        for (int i = 0; i < count; i++)
        {
            float a = Random.value * Mathf.PI * 2f;
            float d = radius * Random.Range(0.6f, 1f);
            float l = life * Random.Range(0.7f, 1f);
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
            o.color = colors[Random.Range(0, colors.Length)];
            o.velocity = -dir * d / l;
            o.drag = 1f;
            o.life = l;
            o.size = size * Random.Range(0.6f, 1.3f);
            fx.Particle(pos + (Vector3)(dir * d), o);
        }
    }

    // 破片が飛び散る（Prototype の fx.shards）
    void Shards(Vector3 pos, int count, Color[] colors, float r0, float distance)
    {
        for (int i = 0; i < count; i++)
        {
            float a = (float)i / count * Mathf.PI * 2f + Random.Range(-0.3f, 0.3f);
            Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            SpriteRenderer sr = new GameObject("Shard").AddComponent<SpriteRenderer>();
            sr.sprite = MagicSprites.Shard;
            sr.sortingOrder = fxOrder;
            Color c = colors[Random.Range(0, colors.Length)];
            sr.color = WithAlpha(c, 0.9f);
            Transform t = sr.transform;
            t.position = pos + dir * r0 + new Vector3(0f, 0f, -0.2f);
            t.rotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
            t.localScale = Vector3.one * (9f * PX * 2f * Random.Range(0.6f, 1.3f));

            float d = distance * Random.Range(0.6f, 1.3f);
            float dur = 0.7f * Random.Range(0.7f, 1.2f);
            Vector3 end = t.position + dir * d - ScreenUp * (Random.Range(10f, 40f) * PX);
            Sequence seq = DOTween.Sequence().SetLink(sr.gameObject);
            seq.Insert(0f, t.DOMove(end, dur).SetEase(Ease.OutQuad));
            seq.Insert(0f, t.DORotate(new Vector3(0f, 0f, Random.Range(-340f, 340f)), dur, RotateMode.LocalAxisAdd));
            seq.Insert(0f, sr.DOFade(0f, dur).SetEase(Ease.InQuad));
            seq.OnComplete(() => { if (sr != null) Destroy(sr.gameObject); });
        }
    }

    void PopText(string text, Vector3 pos, float size, Color top, Color bottom, Color outline, float rise, float duration)
    {
        if (newPieceFx != null) newPieceFx.PopText(text, pos, size, top, bottom, outline, rise, duration);
    }

    SpriteRenderer CreateChild(Transform parent, Sprite sprite, Color color, int order)
    {
        SpriteRenderer sr = new GameObject("Layer").AddComponent<SpriteRenderer>();
        sr.transform.SetParent(parent, false);
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
        return sr;
    }

    static Color PlayerColor(PlayerType owner)
    {
        return owner == PlayerType.Player1 ? Player1Color : Player2Color;
    }

    Vector3 CellWorld(int x, int y)
    {
        return BoardWorld(GameHelper.CalcPanelLocation(x, y));
    }

    // 盤のローカル座標 → ワールド座標
    Vector3 BoardWorld(Vector2 local)
    {
        if (gameBoard == null) return local;
        return gameBoard.transform.TransformPoint(new Vector3(local.x, local.y, 0f));
    }

    Vector2 BoardWorldVector(Vector2 local)
    {
        if (gameBoard == null) return local;
        return gameBoard.transform.TransformVector(new Vector3(local.x, local.y, 0f));
    }

    // 画面の上方向（カメラが180°回転していても見た目の「上」）
    Vector3 ScreenUp => cam != null ? cam.transform.up : Vector3.up;

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

// 魔法の演出で使う画像（実行時に作る。プロトタイプの PIXI.Graphics の図形を、同じピクセル値で描いたもの）
public static class MagicSprites
{
    private const float PX = 1f / 66f;

    private static Sprite square, shard, circleColored, circleWhite, barrierGlow, barrierFace;

    // 白い正方形（幅 1unit）
    public static Sprite Square => square != null ? square : (square = Make(4, 0.5f, (x, y) => Color.white, FilterMode.Point));

    // 破片（不揃いな三角形。幅 1unit）
    public static Sprite Shard => shard != null ? shard : (shard = Make(32, 0.5f, (x, y) =>
    {
        // 頂点 (0,1)・(0.7,-0.5)・(-0.6,-0.35) の三角形（単位：半径）
        Vector2 p = new Vector2(x, y);
        float d = TriangleDistance(p, new Vector2(0f, 0.95f), new Vector2(0.7f, -0.5f), new Vector2(-0.6f, -0.35f));
        float fill = Mathf.Clamp01(-d * 20f + 0.5f);
        float edge = Mathf.Clamp01(1f - Mathf.Abs(d) * 20f);
        return Color.Lerp(new Color(1f, 1f, 1f, fill * 0.8f), Color.white, edge * 0.9f);
    }));

    // 魔法陣（R = 46px）。色を付ける部分（外周の輪・六芒星）と白い部分（内側の輪・外周の目盛り）を別の画像にする
    public static Sprite CircleColored => circleColored != null ? circleColored : (circleColored = MakeCircle(true));
    public static Sprite CircleWhite => circleWhite != null ? circleWhite : (circleWhite = MakeCircle(false));

    // 盾のバリア（六角形。R = 駒の大きさ × 0.54）：ぼかした発光（色を付ける）と、面・輪郭（色つきの画像）
    public static Sprite BarrierGlow => barrierGlow != null ? barrierGlow : (barrierGlow = MakeBarrier(true));
    public static Sprite BarrierFace => barrierFace != null ? barrierFace : (barrierFace = MakeBarrier(false));

    static Sprite MakeCircle(bool colored)
    {
        const float R = 46f;
        const float half = 56f; // px
        return Make(256, half * PX, (ux, uy) =>
        {
            // ux, uy は -1〜1。px 単位に直す
            Vector2 p = new Vector2(ux, uy) * half;
            float d = p.magnitude;
            float a = 0f;
            if (colored)
            {
                a = Mathf.Max(a, 0.25f * Line(Mathf.Abs(d - R), 5f));        // 太くうっすらした外周
                a = Mathf.Max(a, Line(Mathf.Abs(d - R), 1.5f));              // 外周の輪
                // 六芒星（半径 0.8R の正三角形 2 つ）
                for (int k = 0; k < 2; k++)
                {
                    float rot = k == 0 ? -Mathf.PI / 2f : Mathf.PI / 2f;
                    for (int i = 0; i < 3; i++)
                    {
                        Vector2 v0 = Polar(R * 0.8f, rot + i * Mathf.PI * 2f / 3f);
                        Vector2 v1 = Polar(R * 0.8f, rot + (i + 1) * Mathf.PI * 2f / 3f);
                        a = Mathf.Max(a, 0.95f * Line(SegmentDistance(p, v0, v1), 1f));
                    }
                }
            }
            else
            {
                a = Mathf.Max(a, 0.8f * Line(Mathf.Abs(d - R * 0.8f), 0.75f)); // 内側の輪
                // 外周の目盛り（24 本）
                float ang = Mathf.Atan2(p.y, p.x);
                for (int i = 0; i < 24; i++)
                {
                    float t = i / 24f * Mathf.PI * 2f;
                    float r2 = R * (i % 2 == 1 ? 0.9f : 0.96f);
                    a = Mathf.Max(a, 0.7f * Line(SegmentDistance(p, Polar(R * 0.84f, t), Polar(r2, t)), 1f));
                }
            }
            return new Color(1f, 1f, 1f, a);
        }, FilterMode.Bilinear);
    }

    static Sprite MakeBarrier(bool glow)
    {
        const float R = 0.54f * 66f; // px
        const float half = 0.8f * 66f;
        Color fill = MagicFx.ShieldColor;
        Color line = MagicFx.ShieldLightColor;
        Color pale = MagicFx.ShieldPaleColor;
        return Make(128, half * PX, (ux, uy) =>
        {
            Vector2 p = new Vector2(ux, uy) * half;
            float d = HexagonDistance(p, R);
            if (glow)
            {
                // 太さ 8px の線をぼかした光（白。SpriteRenderer の色で水色にする）
                float g = Mathf.Exp(-(d * d) / (2f * 6f * 6f));
                return new Color(1f, 1f, 1f, 0.9f * g);
            }
            Color c = new Color(fill.r, fill.g, fill.b, d < 0f ? 0.16f : 0f);   // うっすらした面
            c = Over(c, line, Line(Mathf.Abs(d), 1.25f));                       // 輪郭
            c = Over(c, pale, 0.6f * Line(Mathf.Abs(HexagonDistance(p, R * 0.78f)), 0.6f)); // 内側の輪郭
            return c;
        }, FilterMode.Bilinear);
    }

    // 線の濃さ：中心線からの距離 dist、太さの半分 halfWidth（境目を 1px ぼかす）
    static float Line(float dist, float halfWidth)
    {
        return Mathf.Clamp01(halfWidth + 0.5f - dist);
    }

    static Vector2 Polar(float r, float a)
    {
        return new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r);
    }

    static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
        return Vector2.Distance(p, a + ab * t);
    }

    // 辺が上下にある六角形（頂点が 0°, 60°, …。Prototype の hexagonPolygon）までの符号付きの距離（内側が負）
    static float HexagonDistance(Vector2 p, float R)
    {
        float apothem = R * Mathf.Cos(Mathf.PI / 6f);
        float d = float.MinValue;
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.PI / 6f + i * Mathf.PI / 3f; // 辺の法線
            d = Mathf.Max(d, Vector2.Dot(p, new Vector2(Mathf.Cos(a), Mathf.Sin(a))) - apothem);
        }
        return d;
    }

    // 三角形までの符号付きの距離（内側が負。単位は p と同じ）
    static float TriangleDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d = Mathf.Min(SegmentDistance(p, a, b), Mathf.Min(SegmentDistance(p, b, c), SegmentDistance(p, c, a)));
        float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
        bool inside = (s1 >= 0f && s2 >= 0f && s3 >= 0f) || (s1 <= 0f && s2 <= 0f && s3 <= 0f);
        return inside ? -d : d;
    }

    static float Cross(Vector2 a, Vector2 b)
    {
        return a.x * b.y - a.y * b.x;
    }

    static Color Over(Color under, Color over, float alpha)
    {
        if (alpha <= 0f) return under;
        float a = alpha + under.a * (1f - alpha);
        if (a <= 0f) return new Color(0f, 0f, 0f, 0f);
        Color c = (over * alpha + under * under.a * (1f - alpha)) / a;
        c.a = a;
        return c;
    }

    // size×size の画像を作る。shader は中心を (0, 0)、端を ±1 とした座標で色を返す。幅は 2 × halfUnits（unit）
    static Sprite Make(int size, float halfUnits, System.Func<float, float, Color> shader, FilterMode filter)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = filter };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float ux = (x + 0.5f) / size * 2f - 1f;
                float uy = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = shader(ux, uy);
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        float ppu = size / (2f * halfUnits);
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
    }
}
