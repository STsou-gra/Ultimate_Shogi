using UnityEngine;

// 通常魔法「カウンター」の演出（Prototype の view/events.js の counterGiven・counterGuard・counterStrike と pieceView.js の counter）。MagicFx の続き（partial）
//
//  カウンター状態になる … 魔法陣・赤橙の光の粒・「カウンター！」。駒を囲んで回る赤橙の輪は GameTickEffects が出す
//  取りを無効にした   … 赤橙の衝撃波・光の粒・閃光・画面シェイク・「COUNTER!」。攻撃した駒は元のマスへ跳ね返る（移動は Bounce、必殺技は Bump）
//  反撃               … 反撃する駒に衝撃波と「反撃！」→ 攻撃した駒のマスへ斬り込む（移動・取りの見た目は SpecialMoveFx）
public partial class MagicFx
{
    // Prototype の counter.js の color 0xff6e40 と、events.js の光の粒の色
    public static readonly Color CounterColor = Hex(0xff6e40);
    public static readonly Color CounterLightColor = Hex(0xffab91);
    public static readonly Color CounterPaleColor = Hex(0xffccbc);
    private static readonly Color CounterStrikeColor = Hex(0xff3d00);
    private static readonly Color CounterOutlineColor = Hex(0x3e1000);

    void OnCounterGiven(GamePiece piece)
    {
        if (piece == null) return;
        Vector3 pos = piece.transform.position;
        MagicCircle(pos, CounterColor, 1.1f);
        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = 24; burst.colors = new[] { CounterColor, CounterLightColor, Color.white };
        burst.speed = 180f * PX; burst.life = 0.6f; burst.size = 0.2f;
        fx.Burst(pos, burst);
        PopText("カウンター！", pos + ScreenUp * (36f * PX), 32f, Color.white, CounterColor, CounterOutlineColor, 40f, 0.9f);
    }

    // カウンターが取りを無効にした（攻撃が届く info.Delay 秒後。輪はそれまで残す）
    void OnCounterGuarded(CounterGuard info)
    {
        GamePiece piece = info.Piece;
        if (piece == null) return;
        piece.counterHoldUntil = Time.time + info.Delay;
        At(info.Delay, () =>
        {
            if (piece == null) return;
            Vector3 pos = piece.transform.position;
            fx.Shockwave(pos, CounterColor, 2.4f, 0.4f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 22; burst.colors = new[] { CounterColor, CounterPaleColor, Color.white };
            burst.speed = 260f * PX; burst.life = 0.5f; burst.size = 0.2f;
            fx.Burst(pos, burst);
            if (newPieceFx != null) newPieceFx.Flash(CounterColor, 0.18f, 0.2f);
            fx.Shake(8f, 0.25f);
            PopText("COUNTER!", pos + ScreenUp * (34f * PX), 42f, Color.white, CounterColor, CounterOutlineColor, 40f, 0.9f);
        });
    }

    // 反撃：反撃する駒に衝撃波、駒と攻撃した駒のあいだに「反撃！」（続く斬り込みは SpecialContext.Step の演出）
    void OnCounterStruck(CounterStrike info)
    {
        GamePiece piece = info.Piece;
        if (piece == null) return;
        Vector3 target = CellWorld(info.At.x, info.At.y);
        At(info.Time, () =>
        {
            if (piece == null) return;
            Vector3 pos = piece.transform.position;
            fx.Shockwave(pos, CounterColor, 1.8f, 0.3f);
            PopText("反撃！", (pos + target) * 0.5f + ScreenUp * (30f * PX), 36f, Color.white, CounterStrikeColor, CounterOutlineColor, 40f, 0.9f);
        });
    }
}

// カウンターの輪の画像（Prototype の pieceView.js の counter：駒を囲む、矢じり付きの 3 本の赤橙の弧）
public static partial class MagicSprites
{
    private static Sprite counterGlow, counterFace;

    // ぼかした発光（白。SpriteRenderer の色で赤橙にする）と、弧・矢じり（色つき）。回転は GameTickEffects
    public static Sprite CounterGlow => counterGlow != null ? counterGlow : (counterGlow = MakeCounterRing(true));
    public static Sprite CounterFace => counterFace != null ? counterFace : (counterFace = MakeCounterRing(false));

    static Sprite MakeCounterRing(bool glow)
    {
        const float size = 66f;              // 駒の大きさ（px）
        const float R = size * 0.56f;        // 輪の半径
        const float half = 0.8f * 66f;
        Color dark = Hex(0x4a1400), line = Hex(0xff5722), head = Hex(0xff7043);
        return Make(128, half * PX, (ux, uy) =>
        {
            // Prototype（PIXI）は y が下向きなので、上下を返して同じ向き（時計回り）の弧にする
            Vector2 p = new Vector2(ux, -uy) * half;
            Color c = new Color(0f, 0f, 0f, 0f);
            float glowDist = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                float a0 = i * Mathf.PI * 2f / 3f;
                float a1 = a0 + Mathf.PI * 2f / 3f * 0.62f;
                Vector2 tip = Polar(R, a1 + 0.2f);
                Vector2 outer = Polar(R + size * 0.1f, a1), inner = Polar(R - size * 0.1f, a1);
                float arc = ArcDistance(p, R, a0, a1);
                float headD = TriangleDistance(p, tip, outer, inner);
                if (glow)
                {
                    glowDist = Mathf.Min(glowDist, Mathf.Min(arc - 3.5f, headD));
                    continue;
                }
                c = Over(c, dark, 0.85f * Line(arc, 3f));          // 弧の縁
                c = Over(c, line, Line(arc, 1.75f));               // 弧
                c = Over(c, head, Mathf.Clamp01(0.5f - headD));    // 矢じり
                c = Over(c, dark, 0.9f * Line(Mathf.Abs(headD), 1f)); // 矢じりの縁
            }
            if (!glow) return c;
            // 太さ 7px の弧・矢じりを 5px ぼかした光
            float g = glowDist <= 0f ? 1f : Mathf.Exp(-(glowDist * glowDist) / (2f * 5f * 5f));
            return new Color(1f, 1f, 1f, 0.85f * g);
        }, FilterMode.Bilinear);
    }

    // 中心 (0,0)・半径 R の、角度 a0 → a1（a0 < a1。ラジアン）の弧までの距離
    static float ArcDistance(Vector2 p, float R, float a0, float a1)
    {
        float a = Mathf.Atan2(p.y, p.x);
        while (a < a0) a += Mathf.PI * 2f;
        while (a >= a0 + Mathf.PI * 2f) a -= Mathf.PI * 2f;
        if (a <= a1) return Mathf.Abs(p.magnitude - R);
        return Mathf.Min(Vector2.Distance(p, Polar(R, a0)), Vector2.Distance(p, Polar(R, a1)));
    }
}
