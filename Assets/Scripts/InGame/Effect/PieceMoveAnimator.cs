using UnityEngine;
using DG.Tweening;

public enum PieceMoveStyle { Normal, Jump, BigJump, Dash }

// 駒の移動アニメーションと着地の演出
// プロトタイプ(index.html)の animateMove / landFx を DOTween で再現したもの。
// 距離は「1マス = 66px = Unity の 1」として換算している。
public class PieceMoveAnimator : MonoBehaviour
{
    [SerializeField] private PieceFx fx;

    [Header("Normal")]
    [SerializeField] private float normalDuration = 0.28f;
    [SerializeField] private Ease normalEase = Ease.InOutCubic;     // GSAP power2.inOut
    [SerializeField] private float normalPopScale = 1.15f;          // 移動中に一瞬この倍率まで拡大して戻る
    [SerializeField] private Ease normalPopEase = Ease.OutQuad;     // GSAP の既定 power1.out

    [Header("Jump")]
    [SerializeField] private float jumpDuration = 0.35f;
    [SerializeField] private float jumpHeight = 0.76f;              // 50px
    [SerializeField] private float jumpGrow = 0.3f;
    [SerializeField] private Ease jumpEase = Ease.InOutQuad;        // GSAP power1.inOut

    [Header("Big Jump")]
    [SerializeField] private float bigJumpDuration = 0.35f;
    [SerializeField] private float bigJumpHeight = 2.73f;           // 180px
    [SerializeField] private float bigJumpGrow = 0.8f;
    [SerializeField, Range(0f, 1f)] private float trailChance = 0.6f; // 1フレームでパーティクルを出す確率
    [SerializeField] private Color trailColor = new Color(0xb3 / 255f, 0x88 / 255f, 0xff / 255f);
    [SerializeField] private float trailLife = 0.5f;
    [SerializeField] private float trailSize = 0.3f;

    [Header("Dash")]
    [SerializeField] private float dashDuration = 0.08f;

    [Header("Front")]
    [SerializeField] private int frontSortingOffset = 100;          // 移動中の駒の sortingOrder に足す値

    [Header("Land")]
    [SerializeField] private int defaultLandPower = 2;
    [SerializeField] private Color player1Color = new Color(0x4f / 255f, 0xc3 / 255f, 0xff / 255f);
    [SerializeField] private Color player2Color = new Color(0xff / 255f, 0x52 / 255f, 0x77 / 255f);
    [SerializeField] private float landShockwaveScale = 1.4f;
    [SerializeField] private float landShockwaveScalePerPower = 0.1f;
    [SerializeField] private float landShockwaveDuration = 0.35f;
    [SerializeField] private int landBurstCount = 6;
    [SerializeField] private int landBurstCountPerPower = 2;
    [SerializeField] private float landBurstSpeed = 1.21f;           // 80px/秒
    [SerializeField] private float landBurstSpeedPerPower = 0.23f;  // 15px/秒
    [SerializeField] private float landBurstLife = 0.4f;
    [SerializeField] private float landBurstSize = 0.2f;
    [SerializeField] private float landBurstOffset = 0.27f;          // 駒の中心から画面下へずらす量（18px）
    [SerializeField] private Color[] landBurstColors =
    {
        new Color(0xd7 / 255f, 0xcc / 255f, 0xc8 / 255f),
        Color.white,
    };
    [SerializeField] private float landShakePerPower = 1f;
    [SerializeField] private float landShakeDuration = 0.15f;

    void Awake()
    {
        if (fx == null) fx = FindAnyObjectByType<PieceFx>();
        if (fx == null) fx = new GameObject("PieceFx").AddComponent<PieceFx>();
    }

    /// <summary>通常の指し手で使う動き方（成っていない桂馬は跳ぶ）</summary>
    public static PieceMoveStyle StyleFor(GamePiece piece)
    {
        return piece.type == PieceType.Knight && !piece.isPromoted ? PieceMoveStyle.Jump : PieceMoveStyle.Normal;
    }

    /// <summary>
    /// 最前面に出して移動 → 着地演出 までをまとめて再生する。
    /// targetLocation は GamePiece.MoveTo と同じ盤のローカル座標。landPower が 0 以下なら着地演出なし。
    /// </summary>
    public Sequence PlayMove(GamePiece piece, Vector2 targetLocation, PieceMoveStyle style, int landPower = -1, float duration = -1f)
    {
        if (landPower < 0) landPower = defaultLandPower;
        Transform t = piece.transform;

        // 前の移動が残っていれば終わらせてから始める（位置・大きさ・表示順が元に戻る）
        DOTween.Kill(t, true);

        SpriteRenderer[] renderers = piece.GetComponentsInChildren<SpriteRenderer>(true);
        int[] orders = new int[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            orders[i] = renderers[i].sortingOrder;
            renderers[i].sortingOrder += frontSortingOffset;
        }

        Sequence seq = DOTween.Sequence().SetId(t).SetLink(piece.gameObject);
        seq.Append(AnimateMove(piece, targetLocation, style, duration));
        if (landPower > 0) seq.AppendCallback(() => LandFx(piece, landPower));
        seq.OnKill(() =>
        {
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] != null) renderers[i].sortingOrder = orders[i];
            }
        });
        return seq;
    }

    /// <summary>移動アニメーション（Normal / Jump / BigJump / Dash）。duration が負なら各スタイルの既定値</summary>
    public Tween AnimateMove(GamePiece piece, Vector2 targetLocation, PieceMoveStyle style, float duration = -1f)
    {
        Transform t = piece.transform;
        Vector3 to = new Vector3(targetLocation.x, targetLocation.y, t.localPosition.z);
        Vector3 baseScale = t.localScale;

        switch (style)
        {
            case PieceMoveStyle.Jump:
            case PieceMoveStyle.BigJump:
            {
                bool big = style == PieceMoveStyle.BigJump;
                float height = big ? bigJumpHeight : jumpHeight;
                float grow = big ? bigJumpGrow : jumpGrow;
                if (duration < 0) duration = big ? bigJumpDuration : jumpDuration;

                Vector3 from = t.localPosition;
                // カメラが180°回っていても画面の上に向かって跳ぶよう、画面の上方向を盤のローカル座標に直す
                Vector3 up = t.parent != null ? t.parent.InverseTransformDirection(fx.ScreenUp) : (Vector3)fx.ScreenUp;
                up.z = 0f;
                up.Normalize();

                return DOVirtual.Float(0f, 1f, duration, k =>
                    {
                        float arc = Mathf.Sin(Mathf.PI * k);
                        t.localPosition = Vector3.LerpUnclamped(from, to, k) + up * (arc * height);
                        t.localScale = baseScale * (1f + arc * grow);
                        if (big && Random.value < trailChance)
                        {
                            fx.Particle(t.position, new PieceFx.ParticleOptions
                            {
                                color = trailColor, life = trailLife, size = trailSize, drag = 0.95f, alpha = 1f, shrink = true,
                            });
                        }
                    })
                    .SetEase(jumpEase)
                    .OnKill(() =>
                    {
                        t.localPosition = to;
                        t.localScale = baseScale;
                    })
                    .SetLink(piece.gameObject);
            }

            case PieceMoveStyle.Dash:
                if (duration < 0) duration = dashDuration;
                return t.DOLocalMove(to, duration).SetEase(Ease.Linear).SetLink(piece.gameObject);

            default:
            {
                if (duration < 0) duration = normalDuration;
                return DOTween.Sequence()
                    .Join(t.DOLocalMove(to, duration).SetEase(normalEase))
                    .Join(t.DOScale(baseScale * normalPopScale, duration * 0.5f).SetEase(normalPopEase).SetLoops(2, LoopType.Yoyo))
                    .OnKill(() => t.localScale = baseScale)
                    .SetLink(piece.gameObject);
            }
        }
    }

    /// <summary>着地の演出（衝撃波・土ぼこり・画面シェイク）</summary>
    public void LandFx(GamePiece piece, int power = 2)
    {
        if (piece == null) return;
        Vector3 pos = piece.transform.position;
        Color color = piece.player == PlayerType.Player1 ? player1Color : player2Color;

        fx.Shockwave(pos, color, landShockwaveScale + power * landShockwaveScalePerPower, landShockwaveDuration);

        PieceFx.BurstOptions burst = PieceFx.BurstOptions.Default;
        burst.count = landBurstCount + power * landBurstCountPerPower;
        burst.colors = landBurstColors;
        burst.speed = landBurstSpeed + power * landBurstSpeedPerPower;
        burst.life = landBurstLife;
        burst.size = landBurstSize;
        fx.Burst(pos - (Vector3)(fx.ScreenUp * landBurstOffset), burst);

        fx.Shake(power * landShakePerPower, landShakeDuration);
    }
}
