using UnityEngine;

public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn }
public enum PlayerType { Player1, Player2 }
public abstract class GamePiece : MonoBehaviour
{
    public PieceType type;
    public PlayerType player;
    public int piecePoint;

    public int X { get; set; }
    public int Y { get; set; }

    [Header("Promotion Visuals")]
    [SerializeField] protected GameObject normalVisual;
    [SerializeField] protected GameObject promotedVisual;
    public bool isPromoted = false;

    [Header("Tick Effects")]
    public bool isSpecialReady = false; // 必殺技を発動できる（オーラが光り、光の粒が立ち上る）
    public bool hasFuse = false;        // 火薬の導火線（先端で火花が散る）
    public Vector2 fuseOffset = new Vector2(0.06f, 0.6f); // 導火線の先端の位置（駒のローカル座標）

    /// <summary>
    /// (toX,toY) へ動けるか（駒の動きの形＋マスの状態）。盤外・破壊マスには動けない。
    /// 途中の駒・破壊マス・炎上/落とし穴で止まるかは BaseMoveRule（GameHelper.IsPathBlocked）で調べる
    /// </summary>
    public bool CanMove(int toX, int toY)
    {
        if (GameHelper.CalcPanelNum(toX, toY) == -1) return false;
        BoardCells cells = Cells;
        if (cells != null && cells.IsBroken(toX, toY)) return false;
        return CanMovePattern(toX, toY);
    }

    /// <summary>駒ごとの動きの形だけを調べる（各駒のクラスで実装する）</summary>
    protected abstract bool CanMovePattern(int toX, int toY);

    // 駒が置かれている盤のマスの状態（駒は GameBoard の子として作られる）
    private GameBoard ownerBoard;
    public BoardCells Cells
    {
        get
        {
            if (ownerBoard == null) ownerBoard = GetComponentInParent<GameBoard>();
            return ownerBoard != null ? ownerBoard.Cells : null;
        }
    }

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
