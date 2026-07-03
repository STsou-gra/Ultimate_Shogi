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

    public abstract bool CanMove(int toX, int toY);

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
