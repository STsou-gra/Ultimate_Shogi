using UnityEngine;
using TMPro;

// 大将軍の陣の駒（Prototype の src/defs/pieces.js の GUNFU・GUNJU・BOUHEKI。討伐任務のボス「大将軍」の兵と防壁）。
//  ・軍歩兵（GunFu）：3 秒ごとに前（プレイヤー側）へ 1 マス進み、前のマスの駒を攻撃する（ボスの攻撃と同じ扱い）
//  ・軍銃兵（GunJu）：動かない。8 秒ごとに、照準で追ったプレイヤーの駒のマスを撃つ
//  ・防壁（Bouheki）：動かない障害物で HP 3。何で攻撃しても 1 減り、0 で崩れる。残りの HP を数字で出す
// どれもボスの席（後手）の駒として盤に置かれ、動かすのは Match/BossEngine.cs（自分では動けない）。プレイヤーの駒で取れる・狙撃・魔法・爆発で
// 倒せる（持ち駒にもエナジーにもならない）。プレハブを持たず、GameBoard.SpawnPiece が Create で実行時に作る（絵は MagicSprites。Effect/BossSprites.cs）
public class PieceSoldier : GamePiece
{
    private TextMeshPro hpLabel;   // 防壁の HP の数字
    private int shownHp = -1;

    protected override bool CanMoveByPattern(int toX, int toY)
    {
        return false; // 自分では動けない（PieceMoves と同じ）
    }

    public static PieceSoldier Create(Transform parent, PieceType type)
    {
        GameObject obj = new GameObject(type.ToString());
        obj.transform.SetParent(parent, false);
        PieceSoldier piece = obj.AddComponent<PieceSoldier>();
        piece.type = type;

        GameObject visual = new GameObject("Normal Visual");
        visual.transform.SetParent(obj.transform, false);
        SpriteRenderer sr = visual.AddComponent<SpriteRenderer>();
        sr.sprite = type == PieceType.Bouheki ? MagicSprites.Bouheki : type == PieceType.GunJu ? MagicSprites.GunJu : MagicSprites.GunFu;
        sr.sortingOrder = 10; // ほかの駒のプレハブと同じ
        piece.normalVisual = visual;

        if (type == PieceType.Bouheki)
        {
            // 防壁：残りの HP を数字で出す（Update で hp に合わせる）
            GameObject labelObj = new GameObject("HpLabel", typeof(RectTransform));
            labelObj.transform.SetParent(obj.transform, false);
            labelObj.transform.localPosition = new Vector3(0f, -0.02f, -0.05f);
            TextMeshPro t = labelObj.AddComponent<TextMeshPro>();
            TMP_FontAsset font = RuntimeUi.LoadFont();
            if (font != null) t.font = font;
            t.fontSize = 5.4f;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.color = Color.white;
            t.outlineColor = new Color32(40, 28, 20, 255);
            t.outlineWidth = 0.3f;
            t.sortingLayerID = sr.sortingLayerID;
            t.sortingOrder = 11;
            t.rectTransform.sizeDelta = new Vector2(1f, 1f);
            piece.hpLabel = t;
        }
        return piece;
    }

    void Update()
    {
        if (hpLabel == null || hp == shownHp) return;
        shownHp = hp;
        hpLabel.text = hp > 0 ? hp.ToString() : "";
        // HP が減るほど赤くする（3 = 白・2 = 黄・1 = 赤）
        hpLabel.color = hp >= WallRules.MaxHp ? Color.white : hp == 2 ? new Color(1f, 0.92f, 0.45f, 1f) : new Color(1f, 0.5f, 0.42f, 1f);
    }
}
