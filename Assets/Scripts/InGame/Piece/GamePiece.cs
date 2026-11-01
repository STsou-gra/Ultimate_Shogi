using UnityEngine;

public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn }
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

    [Header("Tick Effects")]
    public bool isSpecialReady = false; // 必殺技を発動できる（オーラが光り、光の粒が立ち上る）
    public bool hasFuse = false;        // 火薬の導火線（先端で火花が散る）
    public Vector2 fuseOffset = new Vector2(0.06f, 0.6f); // 導火線の先端の位置（駒のローカル座標）

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
