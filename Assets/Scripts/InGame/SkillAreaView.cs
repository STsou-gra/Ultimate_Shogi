using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;

// 対象選択中の範囲の表示（Prototype の対象選択中の範囲プレビュー：Game.js の drawArea）
//  ・カーソル位置で選ぶ範囲（SkillEffect.GetAreaCells）を、スキルの色で塗って明滅させる
//  ・範囲を選べないとき（GetAreaBlockers が空でない）は範囲全体を赤くし、原因のマスをさらに濃い赤にする
// 見た目は実行時に GameBoard の子として作る（駒・マスのハイライトと同じ座標系。1マス = 1）。
// シーンに置かなくても GameSystem が自動で追加する。表示・非表示は GameSystem が Show / Hide で切り替える
public class SkillAreaView : MonoBehaviour
{
    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private SpriteRenderer boardImage; // 描画順の基準（未指定なら "BoardImage" を探す）

    [Header("Colors")]
    [SerializeField] private Color blockedColor = new Color(0xff / 255f, 0x17 / 255f, 0x44 / 255f); // 選べない範囲 #ff1744
    [SerializeField, Range(0f, 1f)] private float areaAlpha = 0.4f;
    [SerializeField, Range(0f, 1f)] private float blockedAlpha = 0.35f;
    [SerializeField, Range(0f, 1f)] private float blockerAlpha = 0.7f;

    [Header("Sorting")]
    [Tooltip("盤の描画順に足す（マスのハイライト(1)より上、駒(10)より下）")]
    [SerializeField] private int orderOffset = 2;
    [Tooltip("マスの状態(BoardCellView)よりカメラ側に寄せる")]
    [SerializeField] private float zOffset = -0.03f;

    private Transform root;
    private readonly SpriteRenderer[] cells = new SpriteRenderer[BoardCells.Size * BoardCells.Size];
    private readonly float[] baseAlpha = new float[BoardCells.Size * BoardCells.Size];
    private bool visible;
    private float time;
    private static Sprite whiteSprite;

    void Start()
    {
        if (gameBoard == null) gameBoard = FindAnyObjectByType<GameBoard>();
        if (boardImage == null)
        {
            GameObject obj = GameObject.Find("BoardImage");
            if (obj != null) boardImage = obj.GetComponent<SpriteRenderer>();
        }
        Build();
    }

    void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    void Build()
    {
        if (gameBoard == null || root != null) return;
        root = new GameObject("SkillArea").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(0f, 0f, zOffset);

        int order = (boardImage != null ? boardImage.sortingOrder : 0) + orderOffset;
        int layer = boardImage != null ? boardImage.sortingLayerID : 0;
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++)
            {
                GameObject obj = new GameObject($"Area_{x}_{y}");
                obj.transform.SetParent(root, false);
                Vector2 location = GameHelper.CalcPanelLocation(x, y);
                obj.transform.localPosition = new Vector3(location.x, location.y, 0f);
                obj.transform.localScale = new Vector3(0.96f, 0.96f, 1f);
                SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
                sr.sprite = GetWhiteSprite();
                sr.sortingLayerID = layer;
                sr.sortingOrder = order;
                obj.SetActive(false);
                cells[GameHelper.CalcPanelNum(x, y)] = sr;
            }
        }
    }

    // 範囲 area を color で塗る。blockers が1つでもあれば範囲全体を赤くし、blockers のマスを濃い赤にする
    public void Show(IList<Vector2Int> area, IList<Vector2Int> blockers, Color color)
    {
        if (root == null) Build();
        if (root == null) return;
        HideCells();

        bool blocked = blockers != null && blockers.Count > 0;
        if (area != null)
        {
            foreach (Vector2Int c in area) Paint(c, blocked ? blockedColor : color, blocked ? blockedAlpha : areaAlpha);
        }
        if (blocked)
        {
            foreach (Vector2Int c in blockers) Paint(c, blockedColor, blockerAlpha);
        }
        visible = true;
    }

    public void Hide()
    {
        HideCells();
        visible = false;
        if (root != null) root.DOKill(true);
    }

    // 選べない範囲で決定しようとした：範囲を小さく揺らす
    public void Shake()
    {
        if (root == null) return;
        root.DOKill(true);
        root.DOShakePosition(0.25f, new Vector3(0.08f, 0.08f, 0f), 30, 90f, false, true).SetLink(root.gameObject);
    }

    void Paint(Vector2Int c, Color color, float alpha)
    {
        int index = GameHelper.CalcPanelNum(c.x, c.y);
        if (index < 0 || index >= cells.Length || cells[index] == null) return;
        SpriteRenderer sr = cells[index];
        sr.gameObject.SetActive(true);
        sr.color = new Color(color.r, color.g, color.b, alpha);
        baseAlpha[index] = alpha;
    }

    void HideCells()
    {
        foreach (SpriteRenderer sr in cells)
        {
            if (sr != null) sr.gameObject.SetActive(false);
        }
    }

    // 明滅（マスのハイライトと同じ sin(t*6)）
    void LateUpdate()
    {
        if (!visible) return;
        time += Time.deltaTime;
        float k = 0.7f + 0.3f * Mathf.Sin(time * 6f);
        for (int i = 0; i < cells.Length; i++)
        {
            SpriteRenderer sr = cells[i];
            if (sr == null || !sr.gameObject.activeSelf) continue;
            Color c = sr.color;
            c.a = baseAlpha[i] * k;
            sr.color = c;
        }
    }

    // 1×1 の白い四角
    static Sprite GetWhiteSprite()
    {
        if (whiteSprite != null) return whiteSprite;
        const int size = 4;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Point;
        Color32[] pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = new Color32(255, 255, 255, 255);
        tex.SetPixels32(pixels);
        tex.Apply();
        whiteSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        return whiteSprite;
    }
}
