using UnityEngine;
using DG.Tweening;

// 普段の指し手の演出（Prototype の view/events.js の captured・toHand・dropped・promoted・check）
//  取り     … 攻撃が届いた瞬間に光の粒・攻撃側の色の衝撃波・画面の揺れ。取られた駒は回転しながら取った側の画面端へ飛んで消える
//  打つ     … 駒が上から落ちてきて、衝撃波・土ぼこり・画面の揺れ
//  成り     … 金色の衝撃波と光の粒、駒が弾んで「成！」
//  王手     … 画面中央に大きな「王手！」と画面の揺れ
//  王将を取った … 「王 討ち取り！」（王将が2枚以上あって対局が続くときも出る）
//  手番     … 対局の最初に「対局開始」、2手目からは手番の始まりに「☗ 先手の番」「☖ 後手の番」
//  打ち込み解禁 … 「敵陣への打ち込み 解禁！」
//  エナジー … 増減のたびに、そのプレイヤーの側の盤の外に「+N エナジー」「-N エナジー」
// 必殺技・魔法の中の取りは SpecialMoveFx、狙撃は NewPieceFx が演出するので、ここでは扱わない。
// シーンに置かなくても GameSystem が自動で追加する
public class BasicMoveFx : MonoBehaviour
{
    private const float PX = 1f / 66f;
    private static readonly Color Player1Color = Hex(0x4fc3ff);
    private static readonly Color Player2Color = Hex(0xff5277);
    private static readonly Color Gold = Hex(0xffd54a);

    private GameSystem gameSystem;
    private GameBoard gameBoard;
    private PieceFx fx;
    private NewPieceFx newPieceFx;
    private Camera cam;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();
        gameBoard = FindAnyObjectByType<GameBoard>();
        fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
        newPieceFx = FindAnyObjectByType<NewPieceFx>();
        cam = Camera.main;
        if (gameSystem != null) gameSystem.PieceDefeated += OnPieceDefeated;
    }

    void OnDestroy()
    {
        if (gameSystem != null) gameSystem.PieceDefeated -= OnPieceDefeated;
    }

    Vector3 ScreenUp => cam != null ? cam.transform.up : Vector3.up;

    // ------------------------------------------------------------
    //  取り
    // ------------------------------------------------------------
    void OnPieceDefeated(PieceDefeat defeat)
    {
        if (defeat.Kind != DefeatKind.Capture || !defeat.By.HasValue || defeat.Piece == null) return;
        if (gameSystem.ActiveSpecial != null || gameSystem.IsSnipeCapture) return;
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        if (newPieceFx == null) return;

        if (defeat.Type == PieceType.King)
        {
            Vector3 kp = CellWorld(defeat.X, defeat.Y);
            DOVirtual.DelayedCall(Mathf.Max(0.15f, gameSystem.CaptureFxDelay), () =>
                newPieceFx.PopText("王 討ち取り！", kp - ScreenUp * (20f * PX), 50f, Color.white, Hex(0xff1744), Hex(0x2a0000), 40f, 1.4f), false).SetLink(gameObject);
        }

        PlayerType by = defeat.By.Value;
        float delay = Mathf.Max(0.15f, gameSystem.CaptureFxDelay);
        GameObject ghost = newPieceFx.CreateGhost(defeat.Piece, false);
        if (ghost == null) return;
        Transform gt = ghost.transform;

        // 取った側の画面端（盤の外）へ
        Vector2 edge = GameHelper.CalcPanelLocation(defeat.X, defeat.Y - SpecialContext.Forward(by) * 12);
        Vector3 end = new Vector3(edge.x, edge.y, gt.localPosition.z);

        Sequence seq = DOTween.Sequence().SetLink(ghost);
        seq.AppendInterval(delay);
        seq.AppendCallback(() =>
        {
            if (ghost == null) return;
            Vector3 pos = gt.position;
            Color color = by == PlayerType.Player1 ? Player1Color : Player2Color;
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 26; burst.colors = new[] { Color.white, color, Hex(0xffe082) }; burst.speed = 280f * PX; burst.life = 0.6f;
            fx.Burst(pos, burst);
            fx.Shockwave(pos, color, 2.6f, 0.5f);
            fx.Shake(6f, 0.2f);
            foreach (SpriteRenderer sr in ghost.GetComponentsInChildren<SpriteRenderer>(true)) sr.sortingOrder += 100;
        });
        seq.Append(gt.DOLocalMove(end, 0.55f).SetEase(Ease.InQuad));
        seq.Join(gt.DOLocalRotate(new Vector3(0f, 0f, 720f), 0.55f, RotateMode.LocalAxisAdd));
        seq.Join(gt.DOScale(gt.localScale * 0.85f, 0.55f));
        seq.OnComplete(() => Destroy(ghost));
    }

    // ------------------------------------------------------------
    //  打つ（駒が置かれた直後に呼ぶ）
    // ------------------------------------------------------------
    public void Dropped(GamePiece piece)
    {
        if (piece == null) return;
        Transform t = piece.transform;
        Vector3 basePos = t.localPosition;
        Vector3 baseScale = t.localScale;
        Vector3 up = t.parent != null ? t.parent.InverseTransformDirection(ScreenUp) : ScreenUp;
        up.z = 0f;
        t.localPosition = basePos + up.normalized * (30f * PX);
        t.localScale = baseScale * 2.2f;

        Sequence seq = DOTween.Sequence().SetId(t).SetLink(piece.gameObject);
        seq.Insert(0f, t.DOLocalMove(basePos, 0.25f).SetEase(Ease.InCubic));
        seq.Insert(0f, t.DOScale(baseScale, 0.25f).SetEase(Ease.InCubic));
        seq.AppendCallback(() =>
        {
            if (t == null) return;
            Vector3 pos = t.position;
            fx.Shockwave(pos, piece.type == PieceType.Gunpowder ? Hex(0xff6d00) : Color.white, 2f, 0.5f);
            PieceFx.BurstOptions dust = PieceFx.BurstOptions.Default;
            dust.count = 14; dust.colors = new[] { Hex(0xd7ccc8), Hex(0xbcaaa4) }; dust.speed = 140f * PX; dust.life = 0.5f;
            dust.size = 0.3f; dust.alpha = 0.6f;
            fx.Burst(pos - ScreenUp * (20f * PX), dust);
            fx.Shake(4f, 0.15f);
        });
        seq.OnKill(() =>
        {
            if (t == null) return;
            t.localPosition = basePos;
            t.localScale = baseScale;
        });
    }

    // ------------------------------------------------------------
    //  成り（移動の見た目が終わってから）
    // ------------------------------------------------------------
    public void Promoted(GamePiece piece)
    {
        if (piece == null) return;
        DOVirtual.DelayedCall(0.3f, () =>
        {
            if (piece == null) return;
            Transform t = piece.transform;
            Vector3 pos = t.position;
            fx.Shockwave(pos, Gold, 3f, 0.5f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 30; burst.colors = new[] { Gold, Hex(0xff5252), Color.white }; burst.speed = 220f * PX; burst.life = 0.6f;
            fx.Burst(pos, burst);
            if (!DOTween.IsTweening(t))
            {
                Vector3 baseScale = t.localScale;
                t.localScale = baseScale * 1.7f;
                t.DOScale(baseScale, 0.45f).SetEase(Ease.OutBack, 3f).SetId(t)
                    .OnKill(() => { if (t != null) t.localScale = baseScale; }).SetLink(piece.gameObject);
            }
            if (newPieceFx != null) newPieceFx.PopText("成！", pos + ScreenUp * (36f * PX), 44f, Color.white, Hex(0xff5252), Hex(0x2a0000), 40f, 0.9f);
        }, false).SetLink(gameObject);
    }

    // ------------------------------------------------------------
    //  王手（画面中央）
    // ------------------------------------------------------------
    public void Check()
    {
        DOVirtual.DelayedCall(0.3f, () =>
        {
            Vector3 center = cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;
            if (newPieceFx != null) newPieceFx.PopText("王手！", center, 96f, Color.white, Hex(0xff1744), Hex(0x2a0000), 20f, 1.1f);
            fx.Shake(6f, 0.25f);
        }, false).SetLink(gameObject);
    }

    // ------------------------------------------------------------
    //  手番・エナジー・お知らせ
    // ------------------------------------------------------------
    Vector3 ScreenCenter => cam != null ? new Vector3(cam.transform.position.x, cam.transform.position.y, 0f) : Vector3.zero;

    Vector3 CellWorld(int x, int y)
    {
        Vector2 l = GameHelper.CalcPanelLocation(x, y);
        return gameBoard != null ? gameBoard.transform.TransformPoint(new Vector3(l.x, l.y, 0f)) : (Vector3)l;
    }

    public void GameStart()
    {
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        if (newPieceFx != null) newPieceFx.PopText("対局開始", ScreenCenter, 80f, Hex(0xffd54a), Hex(0xff6f00), Hex(0x2a0a00), 20f, 1.2f);
    }

    public void TurnStarted(PlayerType side)
    {
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        if (newPieceFx == null) return;
        bool p1 = side == PlayerType.Player1;
        string text = p1 ? "☗ 先手の番" : "☖ 後手の番";
        // カメラが回り終わる頃に出す
        DOVirtual.DelayedCall(0.35f, () => newPieceFx.PopText(text, ScreenCenter, 46f, Color.white, p1 ? Player1Color : Player2Color, Hex(0x0a0a1a), 10f, 0.6f), false).SetLink(gameObject);
    }

    public void DropUnlocked()
    {
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        if (newPieceFx == null) return;
        DOVirtual.DelayedCall(1f, () => newPieceFx.PopText("敵陣への打ち込み 解禁！", ScreenCenter + ScreenUp * 1.2f, 40f, Color.white, Gold, Hex(0x2a1a00), 10f, 1.2f), false).SetLink(gameObject);
    }

    // エナジーの増減：そのプレイヤーの側（自陣の奥の、盤の外）に出す
    public void Energy(PlayerType player, int delta)
    {
        if (delta == 0) return;
        if (newPieceFx == null) newPieceFx = FindAnyObjectByType<NewPieceFx>();
        if (newPieceFx == null) return;
        int backRank = SpecialContext.Forward(player) > 0 ? -1 : 9;
        Vector3 pos = CellWorld(4, backRank);
        Color color = player == PlayerType.Player1 ? Player1Color : Player2Color;
        if (delta > 0)
        {
            newPieceFx.PopText($"+{delta} エナジー", pos, 26f, Color.white, color, Hex(0x0a0a1a), 30f, 0.9f);
            PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
            burst.count = 10; burst.colors = new[] { color, Color.white }; burst.speed = 120f * PX; burst.life = 0.5f; burst.size = 0.18f;
            if (fx != null) fx.Burst(pos, burst);
        }
        else
        {
            newPieceFx.PopText($"-{-delta} エナジー", pos, 26f, Color.white, Hex(0xb388ff), Hex(0x0a0a1a), 30f, 0.9f);
        }
    }

    static Color Hex(int rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
