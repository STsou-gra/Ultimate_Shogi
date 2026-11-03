using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 超常魔法「アンデッド・パラダイス」の演出（Prototype の view/skillFx.js の undead.veil・undead.done、
// view/events.js の undeadRisen・undeadMarch・destroyed のアンデッドの分）。MagicFx の続き（partial）
//
//  発動       … 画面が紫に光り、自陣が瘴気に染まる（紫の光・立ち上る緑の火の粉・揺れ）
//  復活       … 1 体ずつ、土くれが飛び、緑の火が吸い込まれ、紫の衝撃波 → 駒が地面から這い上がる →「アンデッド 復活！」
//  進軍       … 小さなカットイン「アンデッド進軍！」（GameSystem が UndeadMarch の CutInStyle で出す）→ 各アンデッドから緑の火が散る
//  成仏       … 倒されたアンデッドから緑の鬼火が天へ昇り、駒の形の光がふわりと昇って消える →「成仏！」
// アンデッドの駒そのものの見た目（紫がかった半透明・瘴気・鬼火）は GamePiece.MakeUndead と GameTickEffects
public partial class MagicFx
{
    public static readonly Color UndeadColor = Hex(0x43a047);
    private static readonly Color UndeadMiasmaColor = Hex(0x9c4dff);
    private static readonly Color UndeadVeilColor = Hex(0x4a148c);
    private static readonly Color[] UndeadFireColors = { Hex(0x76ff03), Hex(0xb2ff59), Hex(0x69f0ae), Hex(0xccff90) };
    private static readonly Color UndeadTextColor = Hex(0xb9f6ca);
    private static readonly Color UndeadOutlineColor = Hex(0x1a0026);

    private const int UndeadZoneOrderOffset = 6; // 自陣の瘴気：盤・マスの状態（+3 まで）より手前、駒（10）より奥

    // 這い上がる時間（Prototype の undeadRisen の 0.5 秒）
    public const float UndeadRiseTime = 0.5f;

    // ------------------------------------------------------------
    //  発動・復活
    // ------------------------------------------------------------

    // 自陣が瘴気に染まる（このあと駒が n 体、次々と地面から這い上がる）
    public void UndeadVeil(PlayerType owner, int n, float time)
    {
        At(time, () =>
        {
            if (newPieceFx != null) newPieceFx.Flash(UndeadVeilColor, 0.45f, 0.6f);
            List<Vector2Int> zone = new List<Vector2Int>();
            for (int x = 0; x < BoardCells.Size; x++)
            {
                if (!BoardZones.IsOwnZone(owner, x)) continue;
                for (int y = 0; y < BoardCells.Size; y++) zone.Add(new Vector2Int(x, y));
            }
            ZoneGlow(zone, UndeadMiasmaColor, 0.4f, 0.4f + UndeadParadiseEffect.RiseInterval * n + UndeadRiseTime);

            for (int i = 0; i < 24; i++)
            {
                Vector2Int c = zone[Random.Range(0, zone.Count)];
                Vector2 local = GameHelper.CalcPanelLocation(c.x, c.y) + new Vector2(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f));
                PieceFx.ParticleOptions o = PieceFx.ParticleOptions.Default;
                o.color = UndeadFireColors[Random.Range(0, UndeadFireColors.Length)];
                o.velocity = (Vector2)(ScreenRight * Random.Range(-10f, 10f) + ScreenUp * Random.Range(30f, 70f)) * PX;
                o.life = Random.Range(0.8f, 1.4f);
                o.size = Random.Range(0.12f, 0.26f);
                o.drag = 1f;
                fx.Particle(BoardWorld(local), o);
            }
            fx.Shake(6f, 0.4f);
        });
    }

    // 駒 piece が地面から這い上がる（time 秒後。それまでは見えないようにしておく）
    public void UndeadRise(GamePiece piece, float time)
    {
        if (piece == null) return;
        Transform t = piece.transform;
        Vector3 baseScale = t.localScale;
        Vector2 cell = GameHelper.CalcPanelLocation(piece.X, piece.Y);
        Vector3 basePos = new Vector3(cell.x, cell.y, t.localPosition.z);
        t.localScale = Vector3.zero; // 這い上がるまでは見せない

        At(time, () =>
        {
            if (piece == null) return;
            Vector3 c = BoardWorld(cell);

            // 土くれが飛び、緑の火が吸い込まれ、紫の衝撃波
            PieceFx.BurstOptions dirt = PieceFx.BurstOptions.Default;
            dirt.count = 12; dirt.colors = DirtColors; dirt.speed = 130f * PX; dirt.life = 0.6f; dirt.size = 0.2f; dirt.gravity = 380f * PX;
            fx.Burst(c - ScreenUp * (14f * PX), dirt);
            Implode(c, 16, 56f * PX, UndeadFireColors, 0.45f, 0.2f);
            fx.Shockwave(c, UndeadMiasmaColor, 1.5f, 0.45f);

            // 少し下から、潰れた形で現れて伸び上がる（back.out(2)）。本体は透明から今の色（紫がかった半透明）へ
            List<SpriteRenderer> body = BodyRenderers(piece);
            Color[] baseColors = body.ConvertAll(sr => sr.color).ToArray();
            Vector3 down = gameBoard != null ? gameBoard.transform.InverseTransformVector(-ScreenUp) : -ScreenUp;
            down.z = 0f;
            t.localPosition = basePos + down.normalized * (22f * PX);
            t.localScale = new Vector3(baseScale.x * 0.5f, baseScale.y * 0.2f, baseScale.z);
            System.Action<float> setAlpha = a =>
            {
                for (int i = 0; i < body.Count; i++)
                {
                    if (body[i] != null) body[i].color = WithAlpha(baseColors[i], baseColors[i].a * a);
                }
            };
            setAlpha(0f);

            Sequence seq = DOTween.Sequence().SetId(t).SetLink(piece.gameObject);
            seq.Insert(0f, t.DOLocalMove(basePos, UndeadRiseTime).SetEase(Ease.OutBack, 2f));
            seq.Insert(0f, t.DOScale(baseScale, UndeadRiseTime).SetEase(Ease.OutBack, 2f));
            seq.Insert(0f, DOVirtual.Float(0f, 1f, UndeadRiseTime, v => setAlpha(v)));
            seq.OnKill(() =>
            {
                if (t == null) return;
                t.localPosition = basePos;
                t.localScale = baseScale;
                setAlpha(1f);
            });

            PieceFx.BurstOptions fire = PieceFx.BurstOptions.Default;
            fire.count = 14; fire.colors = UndeadFireColors; fire.speed = 90f * PX; fire.life = 1f; fire.size = 0.2f; fire.gravity = -140f * PX;
            fx.Burst(c, fire);
        });
    }

    // 復活が終わった：自陣の中央に「アンデッド 復活！」
    public void UndeadDone(PlayerType owner, float time)
    {
        At(time, () =>
        {
            int mid = owner == PlayerType.Player1 ? 1 : BoardCells.Size - 2;
            Vector3 c = CellWorld(mid, 4);
            PopText("アンデッド 復活！", c + ScreenUp * (20f * PX), 40f, Color.white, UndeadTextColor, UndeadOutlineColor, 40f, 1.1f);
        });
    }

    // ------------------------------------------------------------
    //  進軍・成仏
    // ------------------------------------------------------------

    // 自動行動の始まり：各アンデッドから緑の火が散る
    public void UndeadMarchStart(List<GamePiece> pieces, float time)
    {
        At(time, () =>
        {
            foreach (GamePiece p in pieces)
            {
                if (p == null) continue;
                PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
                burst.count = 14; burst.colors = UndeadFireColors; burst.speed = 110f * PX; burst.life = 0.7f; burst.size = 0.2f;
                burst.gravity = -120f * PX;
                fx.Burst(p.transform.position, burst);
            }
        });
    }

    // アンデッドが倒された（取り・消失どちらも）：鬼火が天へ昇る。
    // 普通の移動・狙撃で取られたとき（成仏）は、駒の形の光がふわりと昇って消える（必殺技・自動行動・爆発などは、ほかの演出が駒を飛ばす）
    void OnUndeadDefeated(PieceDefeat defeat)
    {
        SpecialContext ctx = gameSystem != null ? gameSystem.ActiveSpecial : null;
        bool plainCapture = ctx == null && defeat.Reason == GameSystem.ReasonUndeadRest;
        float delay = ctx != null ? Mathf.Max(0f, ctx.HitTime) : (plainCapture ? Mathf.Max(0.15f, gameSystem.CaptureFxDelay) : 0.3f);
        Vector3 pos = CellWorld(defeat.X, defeat.Y);

        if (plainCapture && newPieceFx != null && defeat.Piece != null) RisingGhost(defeat.Piece, delay);

        At(delay, () =>
        {
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 22; burst.colors = UndeadFireColors; burst.speed = 140f * PX; burst.life = 1.1f; burst.size = 0.24f;
            burst.gravity = -160f * PX;
            fx.Burst(pos, burst);
            fx.Shockwave(pos, UndeadMiasmaColor, 1.4f, 0.4f);
        });
        // 「成仏！」（消失の理由ごとの色。MagicFx.Vanish.cs）
        PopVanish(defeat, delay);
    }

    // 駒の形の光が、攻撃が届く delay 秒後から昇りながら薄くなって消える（それまではそのマスに残る）
    void RisingGhost(GamePiece source, float delay)
    {
        GameObject ghost = newPieceFx.CreateGhost(source, false);
        if (ghost == null) return;
        Transform gt = ghost.transform;
        SpriteRenderer[] sprites = ghost.GetComponentsInChildren<SpriteRenderer>(true);
        Color[] baseColors = new Color[sprites.Length];
        for (int i = 0; i < sprites.Length; i++) baseColors[i] = sprites[i].color;
        Vector3 up = gameBoard != null ? gameBoard.transform.InverseTransformVector(ScreenUp) : ScreenUp;
        up.z = 0f;
        Vector3 end = gt.localPosition + up.normalized * (60f * PX);

        Sequence seq = DOTween.Sequence().SetLink(ghost);
        seq.Insert(delay, gt.DOLocalMove(end, 0.9f).SetEase(Ease.OutQuad));
        seq.Insert(delay, gt.DOScale(gt.localScale * 1.15f, 0.9f));
        seq.Insert(delay, DOVirtual.Float(1f, 0f, 0.9f, a =>
        {
            for (int i = 0; i < sprites.Length; i++)
            {
                if (sprites[i] != null) sprites[i].color = WithAlpha(baseColors[i], baseColors[i].a * a);
            }
        }).SetEase(Ease.InQuad));
        seq.OnComplete(() => Destroy(ghost));
    }

    // ------------------------------------------------------------
    //  部品
    // ------------------------------------------------------------

    // マスの一覧を color でうっすら塗り、hold 秒ほど残してから消す（駒より奥。Prototype の fx.areaGlow）
    void ZoneGlow(List<Vector2Int> cells, Color color, float alpha, float hold)
    {
        SpriteRenderer[] sprites;
        Transform root = AreaCells(cells, color, 0f, out sprites);
        int order = UndeadZoneOrderOffset;
        foreach (SpriteRenderer sr in sprites) sr.sortingOrder = order;
        System.Action<float> setAlpha = a =>
        {
            foreach (SpriteRenderer sr in sprites)
            {
                if (sr != null) sr.color = WithAlpha(color, a);
            }
        };
        Sequence seq = DOTween.Sequence().SetLink(root.gameObject);
        seq.Append(DOVirtual.Float(0f, alpha, 0.25f, v => setAlpha(v)));
        seq.AppendInterval(hold);
        seq.Append(DOVirtual.Float(alpha, 0f, 0.6f, v => setAlpha(v)));
        seq.OnComplete(() => { if (root != null) Destroy(root.gameObject); });
    }

    // 駒の本体のスプライト（バリア・槍・瘴気などの重ねた表示を除く）
    static List<SpriteRenderer> BodyRenderers(GamePiece piece)
    {
        List<SpriteRenderer> list = new List<SpriteRenderer>();
        foreach (SpriteRenderer sr in piece.GetComponentsInChildren<SpriteRenderer>(true))
        {
            if (!GameTickEffects.IsPieceOverlay(sr.transform)) list.Add(sr);
        }
        return list;
    }
}
