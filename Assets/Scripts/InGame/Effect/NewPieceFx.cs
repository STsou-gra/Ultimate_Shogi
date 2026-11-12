using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using TMPro;

// 火薬の爆発・狙撃の演出（Prototype の view/events.js の exploded・destroyed・snipeShot、view/fx.js の explosion・
// crosshair・snipeBeam・impact・flash・popText）
//
//  爆発 … 画面の閃光 → 火球 → 衝撃波 2 重 → 火の粉・破片・煙 → 画面シェイク。3×3 の範囲が赤く光り、「爆発!!」。
//         巻き込まれた駒は中心から外へ回転しながら吹き飛ぶ（火薬に入った駒は、まず火薬のマスまで進む）
//  狙撃 … 照準（赤い十字が回転しながら収束 → 点滅してロックオン）→ 狙撃手の反動・銃口の閃光 → 細いビーム →
//         着弾（火球・衝撃波・火花・煙）→「SNIPE!」。撃たれた駒は着弾と同時に弾き飛ばされる
//
// 盤面のデータはすぐに変わる（駒の GameObject もすぐ破棄される）ので、消えた駒は写しを作って演出する。
// 見た目の順番は GameSystem から届く ExplosionInfo.Delay・SnipeShot.ImpactDelay で合わせる（手番の交代は GameSystem が待つ）。
// シーンに置かなくても GameSystem が自動で追加する
public class NewPieceFx : MonoBehaviour
{
    // プロトタイプの1マス(66px)を Unity の1マス(1unit)に合わせて、ピクセル値をそのまま使う
    private const float PX = 1f / 66f;

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private PieceFx fx;
    [SerializeField] private TMP_FontAsset font; // 「爆発!!」「SNIPE!」の文字（未指定なら NotoSansJP を Resources から読む）

    [Header("Sorting")]
    [SerializeField] private int fxOrder = 210;    // 駒(10)・移動中の駒(110)・パーティクル(200)より手前
    [SerializeField] private int textOrder = 400;  // 文字は一番手前
    [SerializeField] private int flashCanvasOrder = 450;

    private static readonly Color SnipeColor = Hex(0xff1744);
    private static readonly Color[] ExplosionSparkColors = { Hex(0xff6d00), Hex(0xffab00), Hex(0xffea00), Hex(0xff1744) };
    private static readonly Color[] ExplosionFlashColors = { Color.white, Hex(0xfff59d) };
    private static readonly Color[] SmokeColors = { Hex(0x3e2723), Hex(0x4e342e), Hex(0x212121) };
    private static readonly Color[] DebrisColors = { Hex(0xff6d00), Hex(0x5d4037), Hex(0xffd54a) };

    private Camera cam;
    private Image flashImage;
    private Sprite glowSprite;
    private Sprite squareSprite;
    private Sprite reticleSprite;

    // 直前の爆発・狙撃（その直後に届く PieceDefeated を、どの演出で倒された駒か見分けるため）
    private ExplosionInfo lastExplosion;
    private int lastExplosionFrame = -1;
    private SnipeShot lastSnipe;
    private int lastSnipeFrame = -1;

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (fx == null) fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        if (font == null) font = Resources.Load<TMP_FontAsset>("Fonts & Materials/NotoSansJP-Regular SDF");
        cam = Camera.main;

        glowSprite = CreateGlowSprite(64);
        squareSprite = CreateSquareSprite();
        reticleSprite = CreateReticleSprite();
        BuildFlashCanvas();

        if (gameSystem != null)
        {
            gameSystem.Exploded += OnExploded;
            gameSystem.Sniped += OnSniped;
            gameSystem.PieceDefeated += OnPieceDefeated;
        }
    }

    void OnDestroy()
    {
        if (gameSystem != null)
        {
            gameSystem.Exploded -= OnExploded;
            gameSystem.Sniped -= OnSniped;
            gameSystem.PieceDefeated -= OnPieceDefeated;
        }
    }

    // ------------------------------------------------------------
    //  爆発
    // ------------------------------------------------------------
    void OnExploded(ExplosionInfo info)
    {
        lastExplosion = info;
        lastExplosionFrame = Time.frameCount;

        Vector3 center = CellWorld(info.X, info.Y);
        int r = MatchConfig.ExplosionRadius;
        float size = 2 * r + 1; // 爆発の範囲の一辺（マス）
        DOVirtual.DelayedCall(info.Delay, () =>
        {
            PlayExplosion(center, size * 66f);
            AreaGlow(info.X, info.Y, r, Hex(0xff3d00), 0.6f, 0.8f);
            PopText("爆発!!", center + ScreenUpVector * (-20f * PX), 76f, Hex(0xfff59d), Hex(0xd50000), Hex(0x2a0000), 40f, 1.1f);
        }, false).SetLink(gameObject);
    }

    // 爆発の見た目だけ（閃光・火球・衝撃波・火の粉・煙・シェイク）。隕石の着弾などから使う。size はピクセル（3マス = 198px）。
    // quiet なら閃光・シェイクを出さない（ボムのように爆発をいくつも重ねるとき、呼び出し側で 1 回にまとめる。Prototype の quiet）
    public void Explosion(Vector3 pos, float size, bool quiet = false)
    {
        PlayExplosion(pos, size, quiet);
    }

    // Prototype の fx.explosion（size はピクセル。範囲の一辺 = 3マス = 198px）
    void PlayExplosion(Vector3 pos, float size, bool quiet = false)
    {
        if (!quiet) Flash(Hex(0xfff3e0), 0.75f, 0.35f);

        // 火球と白い芯（光の粒の画像は 64px。Prototype の scale を Unity のスケールに直す）
        GlowBall(pos, Hex(0xff9100), 0.5f, size / 26f, 0.45f, 0.7f, 0.1f);
        GlowBall(pos, Color.white, 0.3f, size / 45f, 0.25f, 0.4f, 0f);

        // 衝撃波
        fx.Shockwave(pos, Hex(0xffe082), size / 12f, 0.6f);
        fx.Shockwave(pos, Hex(0xff3d00), size / 18f, 0.45f);

        // 火の粉・破片
        PieceFx.BurstOptions sparks = PieceFx.BurstOptions.Default;
        sparks.count = 70; sparks.colors = ExplosionSparkColors; sparks.speed = 520f * PX; sparks.life = 0.9f;
        sparks.size = 0.35f; sparks.gravity = 320f * PX;
        fx.Burst(pos, sparks);
        PieceFx.BurstOptions flashes = PieceFx.BurstOptions.Default;
        flashes.count = 30; flashes.colors = ExplosionFlashColors; flashes.speed = 750f * PX; flashes.life = 0.4f; flashes.size = 0.15f;
        fx.Burst(pos, flashes);

        // 煙（膨らみながら薄くなって立ち上る）
        PieceFx.BurstOptions smoke = PieceFx.BurstOptions.Default;
        smoke.count = 18; smoke.colors = SmokeColors; smoke.speed = 110f * PX; smoke.life = 1.6f; smoke.size = 0.9f;
        smoke.gravity = -40f * PX; smoke.grow = 1.5f; smoke.shrink = false; smoke.alpha = 0.6f; smoke.drag = 0.97f;
        fx.Burst(pos, smoke);

        if (!quiet) fx.Shake(20f, 0.7f);
    }

    // ------------------------------------------------------------
    //  狙撃
    // ------------------------------------------------------------
    void OnSniped(SnipeShot shot)
    {
        lastSnipe = shot;
        lastSnipeFrame = Time.frameCount;

        Vector3 from = CellWorld(shot.From.x, shot.From.y);
        Vector3 to = CellWorld(shot.To.x, shot.To.y);
        float impact = Mathf.Max(0.3f, shot.ImpactDelay);
        const float beamTime = 0.07f;
        float fire = impact - beamTime; // 撃つ瞬間（照準のロックオンの直後）

        Crosshair(to, fire);

        GamePiece sniper = shot.Sniper;
        DOVirtual.DelayedCall(fire, () =>
        {
            Vector3 dir = to - from;
            dir.z = 0f;
            float len = dir.magnitude;
            Vector3 u = len > 0.001f ? dir / len : Vector3.up;

            // 反動（狙撃手が撃った方向と逆へ少し下がって戻る）
            if (sniper != null)
            {
                Transform st = sniper.transform;
                Vector3 basePos = st.localPosition;
                Vector3 back = st.parent != null ? st.parent.InverseTransformVector(-u * (7f * PX)) : -u * (7f * PX);
                back.z = 0f;
                st.DOLocalMove(basePos + back, 0.05f).SetLoops(2, LoopType.Yoyo)
                    .OnKill(() => { if (st != null) st.localPosition = basePos; })
                    .SetLink(sniper.gameObject);
            }
            SnipeBeam(from + u * 0.3f, to, beamTime);
        }, false).SetLink(gameObject);

        DOVirtual.DelayedCall(impact, () =>
        {
            Impact(to, SnipeColor);
            if (shot.Guarded) return; // 盾に防がれた（「GUARD!」は MagicFx が出す）
            PopText("SNIPE!", to + ScreenUpVector * (40f * PX), 44f, Color.white, SnipeColor, Hex(0x2a0000), shot.Explosive ? 70f : 40f, 0.9f);
        }, false).SetLink(gameObject);
    }

    // 照準マーカー（赤い十字が回転しながら収束 → 点滅してロックオン → 少し残って消える）。lockTime に撃つ
    void Crosshair(Vector3 pos, float lockTime)
    {
        SpriteRenderer sr = CreateSprite("Crosshair", reticleSprite, pos, fxOrder + 2);
        Transform t = sr.transform;
        t.rotation = ScreenRotation * Quaternion.Euler(0f, 0f, 90f);
        t.localScale = Vector3.one * 2.6f;
        sr.color = WithAlpha(Color.white, 0f);

        float intro = Mathf.Min(0.4f, lockTime * 0.55f);
        float blinkStart = intro + 0.02f;
        float blink = Mathf.Max(0.04f, (lockTime - blinkStart) / 4f);
        Sequence seq = DOTween.Sequence().SetLink(sr.gameObject);
        seq.Insert(0f, sr.DOFade(1f, intro).SetEase(Ease.OutCubic));
        seq.Insert(0f, t.DOScale(1f, intro).SetEase(Ease.OutCubic));
        seq.Insert(0f, t.DORotateQuaternion(ScreenRotation, intro).SetEase(Ease.OutCubic));
        seq.Insert(blinkStart, sr.DOFade(0.25f, blink).SetLoops(4, LoopType.Yoyo));
        seq.Insert(lockTime + 0.15f, t.DOScale(0.6f, 0.25f).SetEase(Ease.InQuad));
        seq.Insert(lockTime + 0.15f, sr.DOFade(0f, 0.25f));
        seq.OnComplete(() => Destroy(sr.gameObject));
    }

    // 狙撃のビーム（細く鋭い光線が一瞬で伸び、細くなって消える）
    void SnipeBeam(Vector3 from, Vector3 to, float extendTime)
    {
        Flash(SnipeColor, 0.12f, 0.15f);

        // 銃口の閃光
        PieceFx.BurstOptions muzzle = PieceFx.BurstOptions.Default;
        muzzle.count = 10; muzzle.colors = new[] { SnipeColor, Color.white }; muzzle.speed = 160f * PX; muzzle.life = 0.25f; muzzle.size = 0.15f;
        fx.Burst(from, muzzle);

        Vector3 d = to - from;
        d.z = 0f;
        float len = d.magnitude;
        if (len < 0.001f) return;
        float angle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

        GameObject root = new GameObject("SnipeBeam");
        root.transform.position = new Vector3(from.x, from.y, from.z);
        root.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // 外側のぼかし・本体・白い芯（Prototype の太さ 9px・4px・1.5px）
        float[] widths = { 9f * PX, 4f * PX, 1.5f * PX };
        Color[] colors = { WithAlpha(SnipeColor, 0.25f), WithAlpha(SnipeColor, 0.85f), Color.white };
        Transform[] layers = new Transform[widths.Length];
        for (int i = 0; i < widths.Length; i++)
        {
            SpriteRenderer sr = CreateSprite("Beam", squareSprite, Vector3.zero, fxOrder + i);
            sr.transform.SetParent(root.transform, false);
            sr.color = colors[i];
            layers[i] = sr.transform;
            layers[i].localPosition = Vector3.zero;
            layers[i].localScale = new Vector3(0f, widths[i], 1f);
        }

        Sequence seq = DOTween.Sequence().SetLink(root);
        // 伸びる（左端を銃口に固定するため、長さの半分だけ中心をずらす）
        seq.Append(DOVirtual.Float(0f, 1f, extendTime, k =>
        {
            for (int i = 0; i < layers.Length; i++)
            {
                layers[i].localScale = new Vector3(len * k, widths[i], 1f);
                layers[i].localPosition = new Vector3(len * k * 0.5f, 0f, 0f);
            }
        }).SetEase(Ease.Linear));
        // 光線に沿った粒子
        seq.AppendCallback(() =>
        {
            int n = Mathf.CeilToInt(len / (28f * PX));
            for (int i = 0; i < n; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, Random.value);
                fx.Particle(p, new PieceFx.ParticleOptions
                {
                    velocity = new Vector2(Random.Range(-30f, 30f), Random.Range(-30f, 30f)) * PX,
                    color = Random.value < 0.5f ? SnipeColor : Color.white,
                    life = Random.Range(0.2f, 0.45f), size = Random.Range(0.06f, 0.14f), drag = 0.95f, alpha = 1f, shrink = true,
                });
            }
        });
        // 細くなって消える
        seq.AppendInterval(0.05f);
        seq.Append(DOVirtual.Float(1f, 0f, 0.3f, w =>
        {
            for (int i = 0; i < layers.Length; i++)
            {
                Vector3 sc = layers[i].localScale;
                layers[i].localScale = new Vector3(sc.x, widths[i] * w, 1f);
            }
        }).SetEase(Ease.InQuad));
        seq.OnComplete(() => Destroy(root));
    }

    // 着弾の小さな爆発（火球・衝撃波・火花・煙）
    public void Impact(Vector3 pos, Color color)
    {
        GlowBall(pos, Hex(0xffab40), 0.3f, 1.5f, 0.25f, 0.4f, 0.05f);
        fx.Shockwave(pos, color, 1.8f, 0.35f);

        PieceFx.BurstOptions sparks = PieceFx.BurstOptions.Default;
        sparks.count = 24; sparks.colors = new[] { color, Hex(0xffab00), Color.white }; sparks.speed = 280f * PX; sparks.life = 0.45f;
        sparks.size = 0.18f; sparks.gravity = 200f * PX;
        fx.Burst(pos, sparks);

        PieceFx.BurstOptions smoke = PieceFx.BurstOptions.Default;
        smoke.count = 6; smoke.colors = new[] { Hex(0x3e2723), Hex(0x424242) }; smoke.speed = 60f * PX; smoke.life = 0.8f; smoke.size = 0.5f;
        smoke.gravity = -40f * PX; smoke.grow = 1f; smoke.shrink = false; smoke.alpha = 0.5f; smoke.drag = 0.97f;
        fx.Burst(pos, smoke);

        fx.Shake(7f, 0.25f);
    }

    // ------------------------------------------------------------
    //  倒された駒（爆発で吹き飛ぶ・狙撃で弾き飛ばされる）
    //  駒の GameObject は倒された直後に破棄されるので、写しを作って演出する
    // ------------------------------------------------------------
    void OnPieceDefeated(PieceDefeat defeat)
    {
        GamePiece source = defeat.Piece;
        if (source == null) return;

        if (defeat.Kind == DefeatKind.Vanish && defeat.Reason == GameSystem.ReasonExplosion
            && lastExplosion != null && lastExplosionFrame == Time.frameCount)
        {
            BlowAway(source, defeat, lastExplosion);
        }
        else if (defeat.Kind == DefeatKind.Capture && lastSnipe != null && lastSnipeFrame == Time.frameCount
                 && defeat.X == lastSnipe.To.x && defeat.Y == lastSnipe.To.y)
        {
            Shot(source, lastSnipe);
        }
    }

    // 爆発で吹き飛ぶ（Prototype の events.js の destroyed：中心から外へ、回転しながら小さく薄くなる）
    void BlowAway(GamePiece source, PieceDefeat defeat, ExplosionInfo info)
    {
        bool entering = source == info.Entering;
        GameObject ghost = CreateGhost(source, entering);
        if (ghost == null) return;
        Transform gt = ghost.transform;

        Vector2 centerLocal = GameHelper.CalcPanelLocation(info.X, info.Y);
        Vector2 cellLocal = GameHelper.CalcPanelLocation(defeat.X, defeat.Y);
        Sequence seq = DOTween.Sequence().SetLink(ghost);

        // 火薬に入った駒は、まず火薬のマスまで進む（爆発はその瞬間に起きる）
        Vector3 startLocal = gt.localPosition;
        if (entering)
        {
            Vector3 target = new Vector3(centerLocal.x, centerLocal.y, startLocal.z);
            if (info.Delay > 0f) seq.Append(gt.DOLocalMove(target, info.Delay).SetEase(Ease.InOutCubic));
            else gt.localPosition = target;
            cellLocal = centerLocal;
        }
        else if (info.Delay > 0f)
        {
            seq.AppendInterval(info.Delay);
        }

        // 中心から外へ（中心の駒は向きをランダムに）
        Vector2 away = cellLocal - centerLocal;
        float ang = away.sqrMagnitude > 0.01f ? Mathf.Atan2(away.y, away.x) : Random.value * Mathf.PI * 2f;
        float dist = Random.Range(90f, 160f) * PX;
        Vector3 up = LocalScreenUp(gt);
        Vector3 end = new Vector3(cellLocal.x + Mathf.Cos(ang) * dist, cellLocal.y + Mathf.Sin(ang) * dist, startLocal.z) + up * (40f * PX);

        SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = BaseColors(sprites);
        Vector3 baseScale = gt.localScale;
        const float dur = 0.8f;
        float start = seq.Duration(false);
        seq.InsertCallback(start, () =>
        {
            if (ghost == null) return;
            PieceFx.BurstOptions debris = PieceFx.BurstOptions.Default;
            debris.count = 10; debris.colors = DebrisColors; debris.speed = 200f * PX; debris.life = 0.6f;
            fx.Burst(gt.position, debris);
        });
        seq.Insert(start, gt.DOLocalMove(end, dur).SetEase(Ease.OutQuad));
        seq.Insert(start, gt.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-8f, 8f) * Mathf.Rad2Deg), dur, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));
        seq.Insert(start, gt.DOScale(baseScale * 0.3f, dur));
        seq.Insert(start, FadeSprites(sprites, baseColors, dur).SetEase(Ease.OutQuad));
        seq.OnComplete(() => Destroy(ghost));
    }

    // 狙撃で撃ち抜かれる：着弾までその場に残り、着弾の瞬間に弾が来た方向へ弾き飛ばされて消える
    void Shot(GamePiece source, SnipeShot shot)
    {
        GameObject ghost = CreateGhost(source, false);
        if (ghost == null) return;
        Transform gt = ghost.transform;

        Vector2 fromLocal = GameHelper.CalcPanelLocation(shot.From.x, shot.From.y);
        Vector2 toLocal = GameHelper.CalcPanelLocation(shot.To.x, shot.To.y);
        Vector2 dir = toLocal - fromLocal;
        dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.up;

        SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = BaseColors(sprites);
        Vector3 start = gt.localPosition;
        Vector3 end = start + (Vector3)(dir * (70f * PX)) + LocalScreenUp(gt) * (20f * PX);
        Vector3 baseScale = gt.localScale;
        const float dur = 0.5f;

        Sequence seq = DOTween.Sequence().SetLink(ghost);
        seq.AppendInterval(shot.ImpactDelay);
        seq.Append(gt.DOLocalMove(end, dur).SetEase(Ease.OutQuad));
        seq.Join(gt.DOLocalRotate(new Vector3(0f, 0f, Random.Range(-200f, 200f)), dur, RotateMode.LocalAxisAdd).SetEase(Ease.OutQuad));
        seq.Join(gt.DOScale(baseScale * 0.6f, dur));
        seq.Join(FadeSprites(sprites, baseColors, dur).SetEase(Ease.InQuad));
        seq.OnComplete(() => Destroy(ghost));
    }

    // 駒の見た目だけの写しを作る（moving：移動の途中なら、その位置から始める）。必殺技の演出（SpecialMoveFx）からも使う
    public GameObject CreateGhost(GamePiece source, bool moving)
    {
        Transform st = source.transform;
        Vector3 from = st.localPosition;
        Vector3 scale = st.localScale;
        DOTween.Kill(st, false); // 移動の演出を止める（表示順も元に戻る）
        if (!moving)
        {
            Vector2 cell = GameHelper.CalcPanelLocation(source.X, source.Y);
            from = new Vector3(cell.x, cell.y, from.z);
        }

        GameObject ghost = Instantiate(source.gameObject, st.parent);
        ghost.name = $"{source.name}_Ghost";
        // 写しは盤の駒として扱われないよう、GamePiece をすぐに外す（見た目だけ残す）
        foreach (GamePiece gp in ghost.GetComponentsInChildren<GamePiece>(true)) DestroyImmediate(gp);
        ghost.transform.localPosition = from;
        ghost.transform.localScale = scale;
        return ghost;
    }

    // ------------------------------------------------------------
    //  共通の部品
    // ------------------------------------------------------------

    // 画面全体の閃光（Prototype の fx.flash）
    public void Flash(Color color, float alpha, float duration)
    {
        if (flashImage == null) return;
        flashImage.DOKill();
        flashImage.color = WithAlpha(color, alpha);
        flashImage.DOFade(0f, duration).SetLink(flashImage.gameObject);
    }

    // 光の玉が広がりながら消える（scale は Prototype の光の粒 64px に対する倍率）
    void GlowBall(Vector3 pos, Color color, float fromScale, float toScale, float growTime, float fadeTime, float fadeDelay)
    {
        SpriteRenderer sr = CreateSprite("GlowBall", glowSprite, pos, fxOrder);
        sr.color = color;
        float unit = 64f * PX;
        sr.transform.localScale = Vector3.one * (fromScale * unit);
        Sequence seq = DOTween.Sequence().SetLink(sr.gameObject);
        seq.Insert(0f, sr.transform.DOScale(toScale * unit, growTime).SetEase(Ease.OutExpo));
        seq.Insert(fadeDelay, sr.DOFade(0f, fadeTime));
        seq.OnComplete(() => Destroy(sr.gameObject));
    }

    // 範囲 (cx±r, cy±r) のマスを光らせて消す（盤の子に作るので、盤と一緒に回る）
    void AreaGlow(int cx, int cy, int r, Color color, float alpha, float duration)
    {
        if (gameBoard == null) return;
        SpriteRenderer sr = CreateSprite("ExplosionArea", squareSprite, Vector3.zero, fxOrder - 1);
        Transform t = sr.transform;
        t.SetParent(gameBoard.transform, false);
        Vector2 center = GameHelper.CalcPanelLocation(cx, cy);
        t.localPosition = new Vector3(center.x, center.y, -0.05f);
        t.localRotation = Quaternion.identity;
        t.localScale = new Vector3(2 * r + 1, 2 * r + 1, 1f);
        sr.color = WithAlpha(color, alpha);
        sr.DOFade(0f, duration).SetLink(sr.gameObject).OnComplete(() => Destroy(sr.gameObject));
    }

    // 飛び出して上へ流れて消える文字（Prototype の fx.popText。size は Prototype のピクセル）
    public void PopText(string text, Vector3 pos, float size, Color top, Color bottom, Color outline, float rise, float duration)
    {
        GameObject obj = new GameObject("PopText", typeof(RectTransform));
        obj.transform.position = new Vector3(pos.x, pos.y, pos.z - 0.5f);
        obj.transform.rotation = ScreenRotation;
        TextMeshPro tmp = obj.AddComponent<TextMeshPro>();
        if (font != null) tmp.font = font;
        tmp.text = text;
        tmp.fontSize = size * 0.153f; // BoardCellView と同じ換算（17px ≒ 2.6）
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.enableVertexGradient = true;
        tmp.colorGradient = new VertexGradient(top, top, bottom, bottom);
        tmp.outlineWidth = 0.25f;
        tmp.outlineColor = outline;
        tmp.sortingOrder = textOrder;
        tmp.alpha = 0f;

        Transform t = obj.transform;
        t.localScale = Vector3.one * 0.2f;
        Vector3 end = t.position + ScreenUpVector * (rise * PX);
        Sequence seq = DOTween.Sequence().SetLink(obj);
        seq.Insert(0f, DOVirtual.Float(0f, 1f, 0.1f, a => tmp.alpha = a));
        seq.Insert(0f, t.DOScale(1.25f, 0.16f).SetEase(Ease.OutBack, 3f));
        seq.Insert(0.16f, t.DOScale(1f, 0.12f));
        seq.Insert(0.1f, t.DOMove(end, duration).SetEase(Ease.OutQuad));
        seq.Insert(Mathf.Max(0.1f, duration - 0.1f), DOVirtual.Float(1f, 0f, 0.3f, a => tmp.alpha = a));
        seq.OnComplete(() => Destroy(obj));
    }

    SpriteRenderer CreateSprite(string name, Sprite sprite, Vector3 position, int order)
    {
        GameObject obj = new GameObject(name);
        obj.transform.position = position;
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = order;
        return sr;
    }

    void BuildFlashCanvas()
    {
        GameObject canvasObj = new GameObject("NewPieceFxFlash");
        canvasObj.transform.SetParent(transform, false);
        Canvas canvas = canvasObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = flashCanvasOrder;

        GameObject imageObj = new GameObject("Flash", typeof(RectTransform));
        imageObj.transform.SetParent(canvasObj.transform, false);
        RectTransform rt = imageObj.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        flashImage = imageObj.AddComponent<Image>();
        flashImage.raycastTarget = false;
        flashImage.color = new Color(1f, 1f, 1f, 0f);
    }

    Vector3 CellWorld(int x, int y)
    {
        Vector2 location = GameHelper.CalcPanelLocation(x, y);
        if (gameBoard == null) return location;
        return gameBoard.transform.TransformPoint(new Vector3(location.x, location.y, 0f));
    }

    // 画面の上方向（カメラが180°回転していても見た目の「上」）
    Vector3 ScreenUpVector => cam != null ? cam.transform.up : Vector3.up;
    Quaternion ScreenRotation => cam != null ? Quaternion.Euler(0f, 0f, cam.transform.eulerAngles.z) : Quaternion.identity;

    // 画面の上方向を、駒（盤の子）のローカル座標に直す
    Vector3 LocalScreenUp(Transform t)
    {
        Vector3 up = t.parent != null ? t.parent.InverseTransformDirection(ScreenUpVector) : ScreenUpVector;
        up.z = 0f;
        return up.normalized;
    }

    static Color[] BaseColors(SpriteRenderer[] sprites)
    {
        Color[] colors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) colors[i] = sprites[i].color;
        return colors;
    }

    static Tween FadeSprites(SpriteRenderer[] sprites, Color[] baseColors, float duration)
    {
        return DOVirtual.Float(1f, 0f, duration, a =>
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] == null) continue;
                sprites[i].color = WithAlpha(baseColors[i], baseColors[i].a * a);
            }
        });
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

    // ------------------------------------------------------------
    //  画像の生成
    // ------------------------------------------------------------

    // 中心が白く、外側へ透明になる丸い光（幅 1unit）
    static Sprite CreateGlowSprite(int size)
    {
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        float half = size * 0.5f;
        Color32[] pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(half, half)) / half;
                float a = d >= 1f ? 0f : d < 0.25f ? Mathf.Lerp(1f, 0.85f, d / 0.25f) : Mathf.Lerp(0.85f, 0f, (d - 0.25f) / 0.75f);
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f));
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
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

    // 照準（Prototype の shapes.js の drawReticle：円＋内側から外へ伸びる十字＋中心の点。黒い影の上に赤い線）
    static Sprite CreateReticleSprite()
    {
        const int size = 128;
        const float R = 0.42f;                 // 円の半径（マス）
        const float extent = R * 1.35f + 0.05f; // 十字の先まで
        float ppu = size / (extent * 2f);
        float px = ppu * PX;                    // Prototype の 1px が何テクセルか
        float radius = R * ppu;
        float half = size * 0.5f;
        Color shadow = new Color(0f, 0f, 0f, 0.45f);
        Color red = SnipeColor;

        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x + 0.5f - half;
                float dy = y + 0.5f - half;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // 線までの距離（円・十字）
                float ring = Mathf.Abs(d - radius);
                float cross = float.MaxValue;
                float ax = Mathf.Abs(dx), ay = Mathf.Abs(dy);
                if (ay >= radius * 0.25f && ay <= radius * 1.35f) cross = Mathf.Min(cross, ax);
                if (ax >= radius * 0.25f && ax <= radius * 1.35f) cross = Mathf.Min(cross, ay);
                float line = Mathf.Min(ring, cross);

                float shadowA = Mathf.Clamp01(5f * px * 0.5f - line + 0.5f) * shadow.a;
                float redA = Mathf.Clamp01(2.5f * px * 0.5f - line + 0.5f);
                float dotA = Mathf.Clamp01(2.5f * px - d + 0.5f);
                redA = Mathf.Max(redA, dotA);
                Color c = new Color(shadow.r, shadow.g, shadow.b, shadowA);
                c = Color.Lerp(c, new Color(red.r, red.g, red.b, 1f), redA);
                c.a = Mathf.Max(shadowA, redA);
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels(pixels);
        tex.Apply();
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), ppu);
    }
}
