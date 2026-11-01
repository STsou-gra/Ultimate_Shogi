using UnityEngine;

// 陣地の色分けと境界線（Prototype の Game.js の盤の描画：陣地の色分け・陣地の境界線）
//  先手陣（X = 0〜2、盤の手前）… 先手の色で薄く塗る
//  中立（X = 3〜5）             … 塗らない
//  後手陣（X = 6〜8、盤の奥）   … 後手の色で薄く塗る
//  陣地の境目に、その陣地の色の線（太くぼんやりした線＋細く濃い線）を引く
// 見た目は実行時に GameBoard の子として作る（駒・マスのハイライトと同じ座標系。1マス = 1）。
// シーンに置かなくても GameSystem が自動で追加する
public class BoardZoneView : MonoBehaviour
{
    // プロトタイプの1マス(66px)を Unity の1マス(1unit)に合わせて、ピクセル値をそのまま使う
    private const float PX = 1f / 66f;

    [Header("References (未指定ならシーンから探す)")]
    [SerializeField] private GameBoard gameBoard;
    [SerializeField] private SpriteRenderer boardImage; // 描画順の基準（未指定なら "BoardImage" を探す）

    [Header("Colors (Prototype の PLAYER の色)")]
    [SerializeField] private Color player1Color = new Color(0x4f / 255f, 0xc3 / 255f, 0xff / 255f); // 先手 #4fc3ff
    [SerializeField] private Color player2Color = new Color(0xff / 255f, 0x52 / 255f, 0x77 / 255f); // 後手 #ff5277
    [SerializeField, Range(0f, 1f)] private float tintAlpha = 0.1f;

    [Header("Border Lines")]
    [SerializeField] private float glowLineWidth = 5f * PX;
    [SerializeField, Range(0f, 1f)] private float glowLineAlpha = 0.35f;
    [SerializeField] private float lineWidth = 2f * PX;
    [SerializeField, Range(0f, 1f)] private float lineAlpha = 0.9f;

    [Header("Sorting")]
    [Tooltip("盤の画像と同じ描画順に置き、少しだけカメラ側に寄せて盤の上・マスのハイライト(1)と駒(10)の下に描く")]
    [SerializeField] private float zOffset = -0.01f;

    private Transform root;
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
        if (gameBoard == null) return;

        root = new GameObject("BoardZones").transform;
        root.SetParent(gameBoard.transform, false);
        root.localPosition = new Vector3(0f, 0f, zOffset);

        int order = boardImage != null ? boardImage.sortingOrder : 0;
        int layer = boardImage != null ? boardImage.sortingLayerID : 0;

        // 陣地の色分け
        AddCampTint(BoardZone.Player1Camp, player1Color, layer, order);
        AddCampTint(BoardZone.Player2Camp, player2Color, layer, order);

        // 陣地の境界線（段 rank と rank + 1 の間で陣地が変わるところ）
        for (int rank = 0; rank < 8; rank++)
        {
            BoardZone a = BoardZones.ZoneOf(rank);
            BoardZone b = BoardZones.ZoneOf(rank + 1);
            if (a == b) continue;
            Color color = (a == BoardZone.Neutral ? b : a) == BoardZone.Player1Camp ? player1Color : player2Color;
            AddBorderLine(rank, color, layer, order);
        }
    }

    void AddCampTint(BoardZone zone, Color color, int layer, int order)
    {
        int minRank = -1, maxRank = -1;
        for (int rank = 0; rank < 9; rank++)
        {
            if (BoardZones.ZoneOf(rank) != zone) continue;
            if (minRank < 0) minRank = rank;
            maxRank = rank;
        }
        if (minRank < 0) return;

        // GameHelper.CalcPanelLocation は (段 X, 筋 Y) → ローカル座標 (Y, X) に置く
        Vector2 a = GameHelper.CalcPanelLocation(minRank, 0);
        Vector2 b = GameHelper.CalcPanelLocation(maxRank, 8);
        Vector2 center = (a + b) * 0.5f;
        Vector2 size = new Vector2(Mathf.Abs(b.x - a.x) + 1f, Mathf.Abs(b.y - a.y) + 1f);
        AddRect($"Tint_{zone}", center, size, WithAlpha(color, tintAlpha), layer, order, 0f);
    }

    void AddBorderLine(int rank, Color color, int layer, int order)
    {
        // 段 rank と rank + 1 のマスの中間に、盤の端から端まで引く
        Vector2 a = GameHelper.CalcPanelLocation(rank, 0);
        Vector2 b = GameHelper.CalcPanelLocation(rank + 1, 8);
        Vector2 center = (a + b) * 0.5f;
        float length = Mathf.Abs(b.x - a.x) + 1f;
        AddRect($"Border_{rank}_{rank + 1}_Glow", center, new Vector2(length, glowLineWidth), WithAlpha(color, glowLineAlpha), layer, order, -0.001f);
        AddRect($"Border_{rank}_{rank + 1}", center, new Vector2(length, lineWidth), WithAlpha(color, lineAlpha), layer, order, -0.002f);
    }

    void AddRect(string name, Vector2 center, Vector2 size, Color color, int layer, int order, float z)
    {
        GameObject obj = new GameObject(name);
        obj.transform.SetParent(root, false);
        obj.transform.localPosition = new Vector3(center.x, center.y, z);
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sprite = GetWhiteSprite();
        sr.color = color;
        sr.sortingLayerID = layer;
        sr.sortingOrder = order;
    }

    // 1×1 の白い四角（色と大きさは SpriteRenderer の color と localScale で決める）
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

    static Color WithAlpha(Color c, float a)
    {
        c.a = a;
        return c;
    }
}
