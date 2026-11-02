using UnityEngine;

// 末尾に足していく（シーン・プレハブ・通信では数値で保存しているため、途中に入れると既存の値がずれる）
// Gunpowder（火薬）・Sniper（狙撃手）は錬成で作るオリジナル駒（Prototype の src/defs/pieces.js の KAYAKU・SNIPER）
public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn, Gunpowder, Sniper }
public enum PlayerType { Player1, Player2 }
public abstract class GamePiece : MonoBehaviour
{
    public PieceType type;
    public PlayerType player;
    public int piecePoint; // 今は使っていない（駒の価値・エナジー配分は Match/PieceValues.cs）

    public int X { get; set; }
    public int Y { get; set; }

    // 初期配置の駒か（打った駒・スキルで出した駒は false）。倒されると MatchState.FallenInitial に記録される
    public bool IsInitial { get; set; }

    [Header("Promotion Visuals")]
    [SerializeField] protected GameObject normalVisual;
    [SerializeField] protected GameObject promotedVisual;
    public bool isPromoted = false;

    // 盤上で動いた回数（必殺技のチャージ。Prototype の moveCount）。
    // 駒ごとに数え、通常の移動のたびに +1（GameSystem.ApplyMove）。必殺技を使うと 0 に戻る。
    // 打った駒は 0 から。成っても数え直さない（成った駒も同じ必殺技を使える）
    public int moveCount = 0;

    [Header("Tick Effects")]
    public bool isSpecialReady = false; // 必殺技を発動できる（オーラが光り、光の粒が立ち上る）。moveCount から GameSystem が設定する
    public bool hasFuse = false;        // 火薬の導火線（先端で火花が散る）
    public Vector2 fuseOffset = new Vector2(0.06f, 0.6f); // 導火線の先端の位置（駒のローカル座標）
    public bool hasScope = false;       // 狙撃手のスコープ（赤い光が明滅する）
    public Vector2 scopeOffset = new Vector2(0f, 0.55f);  // スコープの光の位置（駒のローカル座標）

    // 盾状態（魔法「盾」。Prototype の shield）。相手の「取り」を1回だけ防ぎ、防ぐと割れる（GameSystem.CapturePiece）。
    // 消失（爆発・炎上・落とし穴など）は防げない。盾状態の間は水色のバリアが出る（GameTickEffects）
    public bool hasShield = false;
    // 盾が割れても、攻撃が届くこの時刻（Time.time）まではバリアを出しておく（MagicFx が設定する。見た目だけ）
    [System.NonSerialized] public float barrierHoldUntil = 0f;

    // ファランクス状態（必殺魔法「ファランクス」。Prototype の phalanx）。盾状態の駒にだけ付けられる（盾が割れても続く）。
    // 持ち主の手番の行動のあと・手番交代の前に自動で 1 マス前進し、前方 1×3 の相手の駒を取る（PhalanxAdvance）。
    // 取られて持ち駒になると駒の GameObject ごと消えるので、打ち直した駒は普通の駒に戻る。金色の槍のマーカーが出る（GameTickEffects）
    public bool hasPhalanx = false;
    // 槍のマーカーが弾む演出を始めた時刻（Time.time。MagicFx が自動行動のときに設定する。見た目だけ）
    [System.NonSerialized] public float spearPulseAt = -10f;

    // アンデッド（超常魔法「アンデッド・パラダイス」で復活した駒。Prototype の undead）。
    // 持ち主は操作できず、必殺技も使えず、自分の魔法の対象にも選べない。持ち主の手番の終わりに自動で 1 手動く（UndeadMarch）。
    // 相手の攻撃は通常どおり受け、倒されると消失する（「成仏」。持ち駒にもエナジーにもならない）。
    // 紫がかった半透明の駒になり（MakeUndead）、紫の瘴気と緑の鬼火をまとう（GameTickEffects）
    public bool isUndead = false;

    // アンデッドの駒の色（本体のスプライトに掛ける。紫がかった半透明）
    public static readonly Color UndeadTint = new Color(0.8f, 0.62f, 1f, 0.6f);

    // アンデッドにする（駒の本体を紫がかった半透明にする。倒された駒の残像などもこの色を写す）
    public void MakeUndead()
    {
        isUndead = true;
        IsInitial = false;
        isSpecialReady = false;
        foreach (GameObject visual in new[] { normalVisual, promotedVisual })
        {
            if (visual == null) continue;
            foreach (SpriteRenderer sr in visual.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = UndeadTint;
        }
    }

    // マスの状態（破壊・炎上・落とし穴）。GameBoard が駒を置くときに設定する
    public BoardCells Cells { get; set; }

    // 駒の動きで (toX, toY) へ行けるか。破壊マスには行けない（駒の種類に関係なく、ここで判定する）。
    // 途中のマス（走り駒の通り道）は BaseMoveRule / CheckRules が GameHelper.IsPathBlocked で調べる
    public bool CanMove(int toX, int toY)
    {
        if (Cells != null && Cells.IsBroken(toX, toY)) return false;
        return CanMoveByPattern(toX, toY);
    }

    // 駒ごとの動きの形（向き・距離だけ。マスの状態や途中の駒は見ない）
    protected abstract bool CanMoveByPattern(int toX, int toY);

    // 駒を移動させるメソッド
    public void MoveTo(Vector2 targetLocation)
    {
        transform.localPosition = targetLocation;
    }

    public void Promote()
    {
        isPromoted = true;
        UpdateVisual();
    }

    public void Unpromote()
    {
        isPromoted = false;
        UpdateVisual();
    }

    public void UpdateVisual()
    {
        if (normalVisual != null) normalVisual.SetActive(!isPromoted);
        if (promotedVisual != null) promotedVisual.SetActive(isPromoted);
    }

    [Header("Highlight Settings")]
    [SerializeField] private Material highlightMaterial;   // 光る（Emission等）ハイライト用マテリアル
    private Material originalNormalMaterial;
    private Material originalPromotedMaterial;

    public void SetHighlight(bool highlight)
    {
        if (highlightMaterial == null) return;

        // 表示されている方のビジュアルオブジェクトのRendererを差し替える
        GameObject activeVisual = isPromoted ? promotedVisual : normalVisual;
        if (activeVisual == null) return;

        Renderer rend = activeVisual.GetComponentInChildren<Renderer>();
        if (rend == null) return;

        if (highlight)
        {
            if (isPromoted)
            {
                if (originalPromotedMaterial == null) originalPromotedMaterial = rend.sharedMaterial;
            }
            else
            {
                if (originalNormalMaterial == null) originalNormalMaterial = rend.sharedMaterial;
            }
            rend.material = highlightMaterial;
        }
        else
        {
            if (isPromoted && originalPromotedMaterial != null)
            {
                rend.material = originalPromotedMaterial;
                originalPromotedMaterial = null;
            }
            else if (!isPromoted && originalNormalMaterial != null)
            {
                rend.material = originalNormalMaterial;
                originalNormalMaterial = null;
            }
        }
    }

    protected void Start()
    {
        UpdateVisual();
    }
}
