using System.Collections.Generic;
using UnityEngine;

// 必殺技 1 つ分（Prototype の src/defs/specials/*.js の 1 ファイル分）。1 技 1 クラスで、このクラスを継承して作る。
//
//  ・moveName      … 技名（カットイン・ボタンに出す）
//  ・requiredMoves … 発動に必要な移動回数（駒ごとに数える。GamePiece.moveCount）
//  ・color         … 演出の色（カットイン・光線など）
//  ・description   … 説明文（駒を選んだときに出す）
//  ・CanUse        … 今の盤面で発動できるか（効果の対象がないときは false）
//  ・GetTargets    … 対象のマスを選ぶ技は、選べるマスを返す（選ばない技は null）
//  ・Apply         … 効果。盤面の変更は SpecialContext を通す（駒を倒すのは必ず GameSystem.CapturePiece / VanishPiece）
//
// どの駒がどの技を使うかは SpecialMoveBook（Resources/SpecialMoveBook）で決める。
// 技を足すときは、このクラスを継承したクラスとアセットを作り、SpecialMoveBook に足す
public abstract class SpecialMove : ScriptableObject
{
    public string moveName;
    [Min(1)] public int requiredMoves = 2;
    public Color color = Color.white;
    [TextArea]
    public string description;

    // 対象のマスを選ぶ技か（GetTargets が null を返さない技）
    public bool NeedsTarget(SpecialContext context)
    {
        return GetTargets(context) != null;
    }

    // 今の盤面で発動できるか。既定では、対象を選ぶ技なら選べるマスが 1 つ以上あるか
    public virtual bool CanUse(SpecialContext context)
    {
        List<Vector2Int> targets = GetTargets(context);
        return targets == null || targets.Count > 0;
    }

    // 対象のマスを選ぶ技は、選べるマスを返す。選ばない技は null
    public virtual List<Vector2Int> GetTargets(SpecialContext context)
    {
        return null;
    }

    // 対象選択中に出す案内文
    public virtual string GetTargetPrompt()
    {
        return "対象のマスを選択";
    }

    // 効果。context.Target に選んだマスが入っている（選ばない技は null）
    public abstract void Apply(SpecialContext context);

    // 既定値を入れる（アセットが見つからないときに SpecialMoveBook が実行時に作る分にも使う）
    protected void SetDefaults(string name, int moves, uint rgb, string text)
    {
        moveName = name;
        requiredMoves = moves;
        color = Hex(rgb);
        description = text;
    }

    public static Color Hex(uint rgb)
    {
        return new Color(((rgb >> 16) & 0xff) / 255f, ((rgb >> 8) & 0xff) / 255f, (rgb & 0xff) / 255f, 1f);
    }
}
