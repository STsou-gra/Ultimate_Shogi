using System.Collections.Generic;
using UnityEngine;

// 錬成できる駒の一覧（全員が同じ一覧を使う）。
// GameSystem に設定しなければ Resources/ForgeRecipeBook を読む
[CreateAssetMenu(menuName = "UltimateShogi/Forge Recipe Book")]
public class ForgeRecipeBook : ScriptableObject
{
    public const string ResourcePath = "ForgeRecipeBook";

    public List<ForgeRecipe> recipes = new List<ForgeRecipe>();

    public static ForgeRecipeBook LoadDefault()
    {
        return Resources.Load<ForgeRecipeBook>(ResourcePath);
    }
}
