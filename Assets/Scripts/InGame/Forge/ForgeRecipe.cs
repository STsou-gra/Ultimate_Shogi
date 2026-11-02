using UnityEngine;

// 駒錬成のレシピ 1 つ分（Prototype の src/defs/forge.js の FORGE_RECIPES の 1 行）
// エナジーを cost だけ払って pieceType の駒を 1 枚作り、持ち駒に加える。
// 新しい駒を錬成できるようにするときは、このアセットを作って ForgeRecipeBook に足す
[CreateAssetMenu(menuName = "UltimateShogi/Forge Recipe")]
public class ForgeRecipe : ScriptableObject
{
    public PieceType pieceType;
    public string label;   // ボタン・演出に出す名前（「火薬」など）
    public int cost;       // 必要なエナジー
    public Sprite icon;    // 錬成ボタンに出す駒の画像
    [TextArea]
    public string description;
}
