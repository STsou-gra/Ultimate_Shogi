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
//  ファランクス … 魔法陣・衝撃波・「ファランクス！」。槍のマーカーは GameTickEffects。自動行動では槍が弾む → 前進 → 槍の突き
//  隕石     … 落下予告（範囲が赤く明滅）→ 斜め上から光の尾を引いて落下 → 着弾（爆発・閃光・衝撃波・土くれ・「隕石落下!!」）
//  破壊砲   … 充填（範囲が明滅・発射口に光が集まる）→ 盤の端から端へ極太の破壊光線 →「破壊!!」
//  天変地異・世界の終わり（超常魔法）… MagicFx.Transcendent.cs
//  アンデッド・パラダイス（超常魔法）… MagicFx.Undead.cs
//
// 盤面のデータはすぐ変わるので、見た目は SkillContext.Timeline の時刻（発動からの秒数）に合わせて再生する。
// シーンに置かなくても GameSystem が自動で追加する
public partial class MagicFx : MonoBehaviour
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
    public static readonly Color PhalanxColor = Hex(0xffc400);
    public static readonly Color MeteorColor = Hex(0xff5722);
    public static readonly Color CannonColor = Hex(0x00e5ff);

    // 隕石が落ち始めてから着弾するまで（MeteorEffect はこの分待ってから範囲の駒を消す）
    public const float MeteorFallTime = 0.9f;
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
            gameSystem.PhalanxGiven += OnPhalanxGiven;
            gameSystem.PieceDefeated += OnUndeadDefeated;
        }
    }

    void OnDestroy()
    {
        if (gameSystem != null)
        {
            gameSystem.ShieldGiven -= OnShieldGiven;
            gameSystem.ShieldBroken -= OnShieldBroken;
            gameSystem.PhalanxGiven -= OnPhalanxGiven;
            gameSystem.PieceDefeated -= OnUndeadDefeated;
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
    //  ファランクス（Prototype の events.js の phalanxGiven・phalanxStep・phalanxBlocked・phalanxThrust、fx.js の thrust）
    //  槍のマーカーは GameTickEffects が駒に重ねて出す。小さなカットイン「ファランクス前進！」は GameSystem が出す
    // ------------------------------------------------------------

    void OnPhalanxGiven(GamePiece piece)
    {
        if (piece == null) return;
        Vector3 pos = piece.transform.position;
        MagicCircle(pos, PhalanxColor, 1.1f);
        fx.Shockwave(pos, Hex(0xffd54a), 2.4f, 0.45f);
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 28; burst.colors = new[] { PhalanxColor, Hex(0xffe082), Color.white };
        burst.speed = 200f * PX; burst.life = 0.6f; burst.size = 0.2f;
        fx.Burst(pos, burst);
        PopText("ファランクス！", pos + ScreenUp * (40f * PX), 32f, Color.white, PhalanxColor, Hex(0x2a1a00), 40f, 0.9f);
    }

    // 自動行動の始まり：駒の足元に衝撃波、槍のマーカーが弾む
    public void PhalanxStep(GamePiece piece, float time)
    {
        At(time, () =>
        {
            if (piece == null) return;
            fx.Shockwave(piece.transform.position, PhalanxColor, 1.8f, 0.35f);
            piece.spearPulseAt = Time.time;
        });
    }

    // 槍の突き：駒が弾み、前方の相手の駒へ穂先が一瞬で伸びる → 閃光・シェイク
    public void PhalanxThrust(GamePiece piece, List<GamePiece> targets, float time)
    {
        if (piece == null) return;
        Vector2 from = GameHelper.CalcPanelLocation(piece.X, piece.Y);
        List<Vector2> ends = new List<Vector2>();
        foreach (GamePiece q in targets)
        {
            if (q != null) ends.Add(GameHelper.CalcPanelLocation(q.X, q.Y));
        }
        At(time, () =>
        {
            if (piece != null)
            {
                Transform t = piece.transform;
                Vector3 baseScale = t.localScale;
                t.localScale = baseScale * 1.25f;
                t.DOScale(baseScale, 0.35f).SetEase(Ease.OutBack, 3f)
                    .OnKill(() => { if (t != null) t.localScale = baseScale; })
                    .SetLink(piece.gameObject);
            }
            foreach (Vector2 to in ends) Thrust(from, to, PhalanxColor);
        });
        At(time + PhalanxAdvance.ThrustTime, () =>
        {
            if (newPieceFx != null) newPieceFx.Flash(PhalanxColor, 0.2f, 0.2f);
            fx.Shake(10f, 0.3f);
        });
    }

    // 穂先が from から to（盤のローカル座標）へ ThrustTime 秒で伸び、着いたところで火花が散る（Prototype の fx.thrust）
    void Thrust(Vector2 from, Vector2 to, Color color)
    {
        if (gameBoard == null) return;
        Vector2 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return;
        const float r0 = 0.3f; // CELL * 0.3

        Transform root = new GameObject("PhalanxThrust").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(from.x, from.y, -0.1f);
        root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

        SpriteRenderer glow = CreateChild(root, MagicSprites.Square, WithAlpha(color, 0.3f), fxOrder);
        SpriteRenderer core = CreateChild(root, MagicSprites.Square, WithAlpha(Color.white, 0.9f), fxOrder + 1);
        SpriteRenderer head = CreateChild(root, MagicSprites.Arrow, color, fxOrder + 2);
        SpriteRenderer headCore = CreateChild(root, MagicSprites.Arrow, WithAlpha(Color.white, 0.9f), fxOrder + 3);
        SpriteRenderer[] parts = { glow, core, head, headCore };
        Color[] baseColors = { glow.color, core.color, head.color, headCore.color };
        headCore.transform.localScale = new Vector3(0.55f, 0.4f, 1f);

        System.Action<float> place = L =>
        {
            glow.transform.localPosition = new Vector3((r0 + L) * 0.5f, 0f, 0f);
            glow.transform.localScale = new Vector3(Mathf.Max(0.001f, L - r0), 12f * PX, 1f);
            float coreEnd = Mathf.Max(r0, L - 10f * PX);
            core.transform.localPosition = new Vector3((r0 + coreEnd) * 0.5f, 0f, -0.001f);
            core.transform.localScale = new Vector3(Mathf.Max(0.001f, coreEnd - r0), 3f * PX, 1f);
            head.transform.localPosition = new Vector3(L, 0f, -0.002f);
            headCore.transform.localPosition = new Vector3(L - 2f * PX, 0f, -0.003f);
        };
        place(r0);

        Vector3 hit = BoardWorld(to);
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(r0, len, PhalanxAdvance.ThrustTime, L => place(L)).SetEase(Ease.OutCubic));
        seq.AppendCallback(() =>
        {
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 14; burst.colors = new[] { color, Color.white };
            burst.speed = 220f * PX; burst.life = 0.35f; burst.size = 0.18f;
            fx.Burst(hit, burst);
        });
        seq.Append(DOVirtual.Float(1f, 0f, 0.3f, a =>
        {
            for (int i = 0; i < parts.Length; i++) parts[i].color = WithAlpha(baseColors[i], baseColors[i].a * a);
        }));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // ------------------------------------------------------------
    //  隕石（Prototype の skillFx.js の meteor.fall、fx.js の meteor）
    //  落下予告（範囲が赤く明滅）→ 術者の側の斜め上から光の尾を引いて落下 → 着弾（爆発・閃光・衝撃波・土くれ・大きなシェイク）
    // ------------------------------------------------------------

    public void MeteorFall(Vector2Int center, List<Vector2Int> cells, PlayerType owner, float time)
    {
        List<Vector2Int> area = new List<Vector2Int>(cells);
        float dir = owner == PlayerType.Player1 ? 1f : -1f; // 1 = 右上から / -1 = 左上から（画面の向き）
        At(time, () =>
        {
            Vector3 target = CellWorld(center.x, center.y);

            // 落下予告：範囲が赤く明滅する（着弾まで）
            Transform warn = AreaCells(area, Hex(0xff1744), 0.35f, out SpriteRenderer[] warnCells);
            Tween blink = DOVirtual.Float(0.3f, 1f, 0.12f, a =>
            {
                foreach (SpriteRenderer sr in warnCells) sr.color = WithAlpha(Hex(0xff1744), 0.35f * a);
            }).SetLoops(-1, LoopType.Yoyo).SetLink(warn.gameObject);

            Meteor(target, dir, MeteorColor, MeteorFallTime, () =>
            {
                blink.Kill();
                if (warn != null) Destroy(warn.gameObject);
                MeteorImpact(target, area);
            });
        });
    }

    void MeteorImpact(Vector3 pos, List<Vector2Int> area)
    {
        if (newPieceFx != null)
        {
            newPieceFx.Explosion(pos, 66f * 3f);
            newPieceFx.Flash(Hex(0xff6d00), 0.35f, 0.8f);
        }
        fx.Shockwave(pos, Color.white, 10f, 0.8f);
        PieceFx.BurstOptions dirt = PieceFx.BurstOptions.Default;
        dirt.count = 30; dirt.colors = DirtColors; dirt.speed = 620f * PX; dirt.life = 1f; dirt.size = 0.3f; dirt.gravity = 700f * PX;
        fx.Burst(pos, dirt);
        fx.Shake(36f, 1.2f);

        // 着弾の熱：範囲がオレンジに光って消える
        Transform hot = AreaCells(area, Hex(0xff6d00), 0.7f, out SpriteRenderer[] hotCells);
        DOVirtual.Float(1f, 0f, 1f, a =>
        {
            foreach (SpriteRenderer sr in hotCells) sr.color = WithAlpha(Hex(0xff6d00), 0.7f * a);
        }).SetLink(hot.gameObject).OnComplete(() => { if (hot != null) Destroy(hot.gameObject); });

        PopText("隕石落下!!", pos + ScreenUp * (20f * PX), 76f, Hex(0xfff59d), Hex(0xd50000), Hex(0x2a0000), 40f, 1.1f);
    }

    // 隕石の落下（斜め上から光の尾を引き、拡大しながら target へ落ちる）。着弾したら onImpact
    void Meteor(Vector3 target, float dir, Color color, float duration, System.Action onImpact)
    {
        Vector3 start = target + ScreenRight * (dir * 460f * PX) + ScreenUp * (620f * PX);
        Vector3 u = target - start;
        u.z = 0f;
        u.Normalize();
        float angle = Mathf.Atan2(u.y, u.x) * Mathf.Rad2Deg;

        Transform root = new GameObject("Meteor").transform;
        root.position = new Vector3(start.x, start.y, target.z - 0.3f);

        // 光の尾（先端が太く、後ろへ細くなる三角形 3 枚）
        Transform trail = new GameObject("Trail").transform;
        trail.SetParent(root, false);
        trail.localRotation = Quaternion.Euler(0f, 0f, angle);
        float[,] tails = { { 34f, 340f }, { 20f, 250f }, { 8f, 170f } };
        Color[] tailColors = { WithAlpha(color, 0.22f), WithAlpha(Hex(0xffab00), 0.45f), WithAlpha(Color.white, 0.85f) };
        SpriteRenderer[] tailSprites = new SpriteRenderer[3];
        for (int i = 0; i < 3; i++) tailSprites[i] = CreateChild(trail, MagicSprites.Tail, tailColors[i], fxOrder + 5 + i);

        // 本体（外炎・岩・芯）
        Transform head = new GameObject("Head").transform;
        head.SetParent(root, false);
        SpriteRenderer halo = CreateChild(head, MagicSprites.Glow, color, fxOrder + 8);
        halo.transform.localScale = Vector3.one * 2.4f;
        SpriteRenderer rock = CreateChild(head, MagicSprites.Rock, Color.white, fxOrder + 9);
        SpriteRenderer coreGlow = CreateChild(head, MagicSprites.Glow, Hex(0xfff59d), fxOrder + 10);
        coreGlow.transform.localScale = Vector3.one * 1.1f;

        Color[] sparkColors = { color, Hex(0xffab00), Hex(0xffd54a), Color.white };
        Tween fall = DOVirtual.Float(0f, 1f, duration, t =>
        {
            if (root == null) return;
            float s = 0.35f + 0.9f * t; // 近づくにつれて拡大
            Vector3 hp = Vector3.Lerp(start, target, t);
            root.position = new Vector3(hp.x, hp.y, target.z - 0.3f);
            head.localScale = Vector3.one * s;
            rock.transform.localRotation *= Quaternion.Euler(0f, 0f, 0.12f * Mathf.Rad2Deg);
            for (int i = 0; i < 3; i++)
            {
                tailSprites[i].transform.localScale = new Vector3(tails[i, 1] * s * PX, tails[i, 0] * 2f * s * PX, 1f);
            }
            for (int i = 0; i < 3; i++)
            {
                PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
                o.color = sparkColors[Random.Range(0, sparkColors.Length)];
                o.velocity = (Vector2)(-u * Random.Range(60f, 200f) * PX) + new Vector2(Random.Range(-40f, 40f), Random.Range(-40f, 40f)) * PX;
                o.life = Random.Range(0.3f, 0.7f);
                o.size = Random.Range(0.15f, 0.35f) * s;
                fx.Particle(hp + (Vector3)(new Vector2(Random.Range(-12f, 12f), Random.Range(-12f, 12f)) * s * PX), o);
            }
        }).SetEase(Ease.InQuad).SetLink(root.gameObject);
        fall.OnComplete(() =>
        {
            if (root != null) Destroy(root.gameObject);
            onImpact?.Invoke();
        });
    }

    // ------------------------------------------------------------
    //  破壊砲（Prototype の skillFx.js の cannon.charge・cannon.fire・cannon.done）
    //  光線は術者の側の盤の端から、相手の側の盤の端へ（盤のローカル座標で描くので、盤と一緒に回る）
    // ------------------------------------------------------------

    // 光線の幾何（盤のローカル座標）：光線が進む軸（段の向き）の始点・終点、各列（筋）の中心、帯の中央
    struct CannonGeometry
    {
        public float A0, A1;          // 光線の始点・終点（ローカル y）
        public List<float> Lanes;     // 各列の中心（ローカル x）
        public float Mid;             // 帯の中央（ローカル x）
        public Vector2 Pt(float along, float across) => new Vector2(across, along);
        public Vector2 Muzzle => Pt(A0, Mid);
    }

    static CannonGeometry Cannon(List<Vector2Int> cells, PlayerType owner)
    {
        const float ext = 0.6f; // 盤の枠の少し外から
        float low = GameHelper.CalcPanelLocation(0, 0).y - 0.5f - ext;
        float high = GameHelper.CalcPanelLocation(8, 0).y + 0.5f + ext;
        bool fromLow = SpecialContext.Forward(owner) > 0; // 先手は手前（X = 0 側）から撃つ
        SortedSet<int> files = new SortedSet<int>();
        foreach (Vector2Int c in cells) files.Add(c.y);
        List<float> lanes = new List<float>();
        foreach (int y in files) lanes.Add(GameHelper.CalcPanelLocation(0, y).x);
        float mid = lanes.Count > 0 ? (lanes[0] + lanes[lanes.Count - 1]) * 0.5f : 0f;
        return new CannonGeometry { A0 = fromLow ? low : high, A1 = fromLow ? high : low, Lanes = lanes, Mid = mid };
    }

    // 1. 充填：範囲が明滅し、盤の端の発射口に光が集まる
    public void CannonCharge(List<Vector2Int> cells, PlayerType owner, float time)
    {
        CannonGeometry g = Cannon(cells, owner);
        List<Vector2Int> area = new List<Vector2Int>(cells);
        At(time, () =>
        {
            Zone(area, CannonColor, CannonEffect.ChargeTime + 0.05f);
            foreach (float l in g.Lanes) MagicCircle(BoardWorld(g.Pt(g.A0, l)), CannonColor, 0.75f);
            Implode(BoardWorld(g.Muzzle), 60, 160f * PX, new[] { CannonColor, Color.white, Hex(0x84ffff) }, 0.6f, 0.25f);
            fx.Shake(4f, 0.6f);
        });
    }

    // 2. 発射：帯全体を覆う極太の光線に、列ごとの太い光線を少しずつずらして何本も重ねる
    public void CannonFire(List<Vector2Int> cells, PlayerType owner, float time)
    {
        CannonGeometry g = Cannon(cells, owner);
        System.Action<float, float, float> fire = (across, width, fade) => Beam(g.Pt(g.A0, across), g.Pt(g.A1, across), CannonColor, width, fade);
        At(time, () =>
        {
            fire(g.Mid, 66f * g.Lanes.Count / 30f, 0.9f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 50; burst.colors = new[] { CannonColor, Color.white };
            burst.speed = 420f * PX; burst.life = 0.6f;
            fx.Burst(BoardWorld(g.Muzzle), burst);
            if (newPieceFx != null) newPieceFx.Flash(Color.white, 0.75f, 0.45f);
            fx.Shake(26f, 0.9f);
        });
        for (int k = 0; k < 3; k++)
        {
            float laneWidth = 2.4f - k * 0.5f;
            At(time + k * 0.1f, () =>
            {
                foreach (float l in g.Lanes) fire(l + Random.Range(-6f, 6f) * PX, laneWidth, 0.45f);
            });
        }
        At(time + 0.3f, () =>
        {
            if (newPieceFx != null) newPieceFx.Flash(CannonColor, 0.4f, 0.9f);
        });
    }

    // 3. 撃ち終わり：「破壊!!」
    public void CannonDone(List<Vector2Int> cells, PlayerType owner, float time)
    {
        CannonGeometry g = Cannon(cells, owner);
        At(time, () =>
        {
            Vector3 center = BoardWorld(g.Pt((g.A0 + g.A1) * 0.5f, g.Mid));
            PopText("破壊!!", center, 84f, Color.white, Hex(0x00b8d4), Hex(0x001a20), 40f, 1.1f);
        });
    }

    // 太い光線（Prototype の fx.beam：外側のぼかし・本体・白い芯 → 明滅して消える。光線に沿った粒子、先端の火花）。
    // from・to は盤のローカル座標、width は太さの倍率（1 = 30px / 14px / 5px）、fade は消えるまでの秒数
    void Beam(Vector2 from, Vector2 to, Color color, float width, float fade)
    {
        if (gameBoard == null) return;
        Vector2 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return;

        Transform root = new GameObject("CannonBeam").transform;
        root.SetParent(gameBoard.transform, false);
        Vector2 mid = (from + to) * 0.5f;
        root.localPosition = new Vector3(mid.x, mid.y, -0.15f);
        root.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);

        float[] widths = { 30f * PX * width, 14f * PX * width, 5f * PX * width };
        Color[] colors = { WithAlpha(color, 0.22f), WithAlpha(color, 0.6f), Color.white };
        SpriteRenderer[] layers = new SpriteRenderer[3];
        for (int i = 0; i < 3; i++)
        {
            layers[i] = CreateChild(root, MagicSprites.Square, colors[i], fxOrder + 20 + i);
            layers[i].transform.localPosition = new Vector3(0f, 0f, -0.001f * i);
            layers[i].transform.localScale = new Vector3(len, widths[i], 1f);
        }
        System.Action<float> setAlpha = a =>
        {
            for (int i = 0; i < 3; i++)
            {
                if (layers[i] != null) layers[i].color = WithAlpha(colors[i], colors[i].a * a);
            }
        };
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(1f, 0.4f, 0.05f, v => setAlpha(v)).SetLoops(4, LoopType.Yoyo));
        seq.Append(DOVirtual.Float(1f, 0f, fade, v => setAlpha(v)));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });

        // 光線に沿った粒子（太い光線ほど横に広く散らす）
        Vector2 dirN = d / len;
        Vector2 normal = new Vector2(-dirN.y, dirN.x);
        float spread = (width - 1f) * 7f * PX;
        int n = Mathf.CeilToInt(len / (14f * PX));
        for (int i = 0; i < n; i++)
        {
            Vector2 p = from + d * Random.value + normal * Random.Range(-spread, spread);
            PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
            o.color = Random.value < 0.5f ? color : Color.white;
            o.velocity = new Vector2(Random.Range(-60f, 60f), Random.Range(-60f, 60f)) * PX;
            o.life = Random.Range(0.3f, 0.7f);
            o.size = Random.Range(0.1f, 0.25f);
            fx.Particle(BoardWorld(p), o);
        }
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 16; burst.colors = new[] { color, Color.white };
        burst.speed = 220f * PX * Mathf.Sqrt(Mathf.Max(0.1f, width)); burst.life = 0.5f;
        fx.Burst(BoardWorld(to), burst);
    }

    // 範囲（マスの一覧）を囲む矩形を duration 秒ほど明滅させてから消す（Prototype の fx.zone。範囲魔法の予告など）
    void Zone(List<Vector2Int> cells, Color color, float duration)
    {
        if (gameBoard == null || cells.Count == 0) return;
        Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
        foreach (Vector2Int c in cells)
        {
            Vector2 p = GameHelper.CalcPanelLocation(c.x, c.y);
            min = Vector2.Min(min, p);
            max = Vector2.Max(max, p);
        }
        min -= Vector2.one * 0.5f;
        max += Vector2.one * 0.5f;
        Vector2 size = max - min, center = (min + max) * 0.5f;

        Transform root = new GameObject("Zone").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(center.x, center.y, -0.06f);
        List<SpriteRenderer> parts = new List<SpriteRenderer>();
        List<float> alphas = new List<float>();
        SpriteRenderer fill = CreateChild(root, MagicSprites.Square, WithAlpha(color, 0.3f), fxOrder - 1);
        fill.transform.localScale = new Vector3(size.x, size.y, 1f);
        parts.Add(fill); alphas.Add(0.3f);
        const float line = 4f * PX;
        Vector2 inner = size - Vector2.one * (4f * PX); // 縁は内側に 2px 寄せる
        Vector3[] edgePos =
        {
            new Vector3(0f, inner.y * 0.5f, -0.001f), new Vector3(0f, -inner.y * 0.5f, -0.001f),
            new Vector3(inner.x * 0.5f, 0f, -0.001f), new Vector3(-inner.x * 0.5f, 0f, -0.001f),
        };
        Vector3[] edgeScale =
        {
            new Vector3(inner.x, line, 1f), new Vector3(inner.x, line, 1f),
            new Vector3(line, inner.y, 1f), new Vector3(line, inner.y, 1f),
        };
        for (int i = 0; i < 4; i++)
        {
            SpriteRenderer e = CreateChild(root, MagicSprites.Square, color, fxOrder);
            e.transform.localPosition = edgePos[i];
            e.transform.localScale = edgeScale[i];
            parts.Add(e); alphas.Add(1f);
        }
        System.Action<float> setAlpha = a =>
        {
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i] != null) parts[i].color = WithAlpha(color, alphas[i] * a);
            }
        };
        int flicks = Mathf.Max(1, Mathf.RoundToInt(duration / 0.16f));
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(1f, 0.2f, 0.08f, v => setAlpha(v)).SetLoops(flicks * 2, LoopType.Yoyo));
        seq.Append(DOVirtual.Float(1f, 0f, 0.3f, v => setAlpha(v)));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // マスの一覧を color で塗った板を盤の子に作る（消すのは呼び出し側）
    Transform AreaCells(List<Vector2Int> cells, Color color, float alpha, out SpriteRenderer[] sprites)
    {
        Transform root = new GameObject("MagicArea").transform;
        if (gameBoard != null) root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(0f, 0f, -0.05f);
        sprites = new SpriteRenderer[cells.Count];
        for (int i = 0; i < cells.Count; i++)
        {
            SpriteRenderer sr = CreateChild(root, MagicSprites.Square, WithAlpha(color, alpha), fxOrder - 1);
            Vector2 p = GameHelper.CalcPanelLocation(cells[i].x, cells[i].y);
            sr.transform.localPosition = new Vector3(p.x, p.y, 0f);
            sprites[i] = sr;
        }
        return root;
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

    // 画面の上方向・右方向（カメラが180°回転していても見た目の「上」「右」）
    Vector3 ScreenUp => cam != null ? cam.transform.up : Vector3.up;
    Vector3 ScreenRight => cam != null ? cam.transform.right : Vector3.right;

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
    private static Sprite glow, arrow, tail, rock, spearGlow, spearFace;

    // 光の粒（中心が白く外側が透明な円。幅 64px。Prototype の TEX.glow）
    public static Sprite Glow => glow != null ? glow : (glow = Make(64, 32f * PX, (x, y) =>
    {
        float d = Mathf.Sqrt(x * x + y * y);
        float a = d < 0.25f ? Mathf.Lerp(1f, 0.85f, d / 0.25f) : Mathf.Lerp(0.85f, 0f, (d - 0.25f) / 0.75f);
        return new Color(1f, 1f, 1f, Mathf.Clamp01(a));
    }, FilterMode.Bilinear));

    // 槍の穂先（+X 向きの矢じり。中心 (0,0) が Prototype の thrust の L。先端 +10px・後ろ -12px・幅 ±8px）
    public static Sprite Arrow => arrow != null ? arrow : (arrow = Make(64, 12f * PX, (ux, uy) =>
    {
        Vector2 p = new Vector2(ux, uy) * 12f;
        float d = ArrowHeadDistance(p, new Vector2(10f, 0f), new Vector2(-12f, -8f), new Vector2(-7f, 0f), new Vector2(-12f, 8f));
        return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d));
    }, FilterMode.Bilinear));

    // 隕石の光の尾（右端が太く、左へ細くなる三角形。基準点は右端の中央。幅 1unit × 高さ 1unit）
    public static Sprite Tail => tail != null ? tail : (tail = Make(64, 0.5f, (ux, uy) =>
    {
        float k = (ux + 1f) * 0.5f;              // 0（尖った先）〜 1（太い根元）
        float edge = Mathf.Abs(uy) - k;          // 三角形の縁までの距離（負なら内側）
        return new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - edge * 32f));
    }, FilterMode.Bilinear, new Vector2(1f, 0.5f)));

    // 隕石の岩（半径 13〜19px の不揃いな 9 角形。焦げ茶の面と橙の縁）
    public static Sprite Rock
    {
        get
        {
            if (rock != null) return rock;
            Vector2[] pts = new Vector2[9];
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                pts[i] = Polar(Random.Range(13f, 19f), a);
            }
            Color fill = Hex(0x4e342e), line = Hex(0xffab00);
            rock = Make(64, 22f * PX, (ux, uy) =>
            {
                Vector2 p = new Vector2(ux, uy) * 22f;
                float d = PolygonDistance(p, pts);
                Color c = new Color(fill.r, fill.g, fill.b, Mathf.Clamp01(0.5f - d));
                return Over(c, line, Line(Mathf.Abs(d), 1.5f));
            }, FilterMode.Bilinear);
            return rock;
        }
    }

    // ファランクスの槍（前方 1×3 マス＝左前・前・右前へ構えた金色の穂先 3 本。駒のローカルの +Y が前。Prototype の pieceView.js の spear）：
    // ぼかした発光（白。SpriteRenderer の色で金色にする）と、柄・穂先（色つき）
    public static Sprite SpearGlow => spearGlow != null ? spearGlow : (spearGlow = MakeSpear(true));
    public static Sprite SpearFace => spearFace != null ? spearFace : (spearFace = MakeSpear(false));

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

    static Sprite MakeSpear(bool glow)
    {
        const float size = 66f;        // 駒の大きさ（px）
        const float half = 0.8f * 66f;
        Color dark = Hex(0x3e2100), light = Hex(0xffe082), gold = Hex(0xffd54a);
        // 左前・右前・前（角度は前から時計回り。k は長さの倍率）
        float[] degs = { -34f, 34f, 0f };
        float[] ks = { 0.82f, 0.82f, 1f };
        return Make(160, half * PX, (ux, uy) =>
        {
            Vector2 p = new Vector2(ux, uy) * half;
            Color c = new Color(0f, 0f, 0f, 0f);
            float glowDist = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float a = degs[i] * Mathf.Deg2Rad;
                Vector2 u = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                Vector2 side = new Vector2(u.y, -u.x);
                float k = ks[i];
                System.Func<float, float, Vector2> at = (r, sd) => u * (r * k) + side * sd;
                Vector2 tip = at(size * 0.72f, 0f), left = at(size * 0.56f, -size * 0.085f), notch = at(size * 0.6f, 0f), right = at(size * 0.56f, size * 0.085f);
                Vector2 s0 = at(size * 0.36f, 0f), s1 = at(size * 0.58f, 0f);
                float shaft = SegmentDistance(p, s0, s1);
                float headD = ArrowHeadDistance(p, tip, left, notch, right);
                if (glow)
                {
                    glowDist = Mathf.Min(glowDist, Mathf.Min(shaft - 3.5f, headD));
                    continue;
                }
                c = Over(c, dark, 0.8f * Line(shaft, 2.25f));                 // 柄の縁
                c = Over(c, light, Line(shaft, 1f));                          // 柄
                c = Over(c, gold, Mathf.Clamp01(0.5f - headD));                // 穂先
                c = Over(c, dark, 0.9f * Line(Mathf.Abs(headD), 0.75f));       // 穂先の縁
            }
            if (!glow) return c;
            // 太さ 7px の柄・穂先を 5px ぼかした光
            float g = glowDist <= 0f ? 1f : Mathf.Exp(-(glowDist * glowDist) / (2f * 5f * 5f));
            return new Color(1f, 1f, 1f, 0.9f * g);
        }, FilterMode.Bilinear);
    }

    // 矢じり（先端 tip・左の角 left・くぼみ notch・右の角 right の 4 点。くぼみで凹んだ形）までの符号付きの距離（内側が負）
    static float ArrowHeadDistance(Vector2 p, Vector2 tip, Vector2 left, Vector2 notch, Vector2 right)
    {
        return Mathf.Min(TriangleDistance(p, tip, left, notch), TriangleDistance(p, tip, notch, right));
    }

    // 多角形（頂点の順に結んだ形）までの符号付きの距離（内側が負）
    static float PolygonDistance(Vector2 p, Vector2[] pts)
    {
        float d = float.MaxValue;
        bool inside = false;
        for (int i = 0, j = pts.Length - 1; i < pts.Length; j = i++)
        {
            d = Mathf.Min(d, SegmentDistance(p, pts[j], pts[i]));
            if ((pts[i].y > p.y) != (pts[j].y > p.y) &&
                p.x < (pts[j].x - pts[i].x) * (p.y - pts[i].y) / (pts[j].y - pts[i].y) + pts[i].x)
            {
                inside = !inside;
            }
        }
        return inside ? -d : d;
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
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
        return Make(size, halfUnits, shader, filter, new Vector2(0.5f, 0.5f));
    }

    // pivot は画像の基準点（0〜1。既定は中心）
    static Sprite Make(int size, float halfUnits, System.Func<float, float, Color> shader, FilterMode filter, Vector2 pivot)
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
        return Sprite.Create(tex, new Rect(0, 0, size, size), pivot, ppu);
    }
}
