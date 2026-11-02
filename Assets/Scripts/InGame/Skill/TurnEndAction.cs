using UnityEngine;

// 手番交代の前の自動行動 1 つ分（Prototype の src/core/turn.js の runPhalanx・runUndeadPhase）。
//
// 手番を終える行動（移動・打つ・手番を使う魔法・必殺技・狙撃）のあと、手番を相手に渡す前に GameSystem が
// 登録順（ファランクス → アンデッド → …）に呼ぶ。2回行動で手番が続くときは呼ばない。
//  ・HasWork … 手番側 owner に動かすものがあるか（なければ飛ばす。カットインも出ない）
//  ・CutInName など … 動く前に出す小さなカットイン（CutInEffect.PlayMiniAsync。名前が空なら出さない）
//  ・Run … 効果。盤面の変更は context.Timeline（SpecialContext）を通す（駒を倒すのは必ず GameSystem.CapturePiece / VanishPiece）。
//          盤面はすぐ変わり、演出は Timeline の時刻どおりに再生される。GameSystem は演出が終わるのを待ってから勝敗を判定する
//
// 自動行動を足すときは、このクラスを継承して GameSystem.AddTurnEndAction で登録する（ファランクスは GameSystem が最初から登録している）
public abstract class TurnEndAction
{
    // 案内文の【】に出す名前
    public abstract string Title { get; }

    // 小さなカットインの名前・サブタイトル・色（名前が空なら出さない）
    public virtual string CutInName => "";
    public virtual string GetCutInSub(PlayerType owner) => "";
    public virtual Color Color => Color.white;

    // 動いている間の案内文
    public virtual string GetStatusText(PlayerType owner) => $"{SideName(owner)}の自動行動";

    public abstract bool HasWork(GameSystem system, PlayerType owner);

    public abstract void Run(TurnEndContext context);

    public static string SideName(PlayerType owner)
    {
        return owner == PlayerType.Player1 ? "先手" : "後手";
    }
}

// 自動行動 1 回分の実行環境
public class TurnEndContext
{
    public GameSystem GameSystem { get; }
    public GameBoard Board { get; }
    public PlayerType Owner { get; }       // 手番側（自動行動で動く駒の持ち主）
    public SpecialContext Timeline { get; } // 演出の時間軸（Step・Capture・Explode・Wait・Schedule）
    public MagicFx Fx { get; }             // 魔法の演出（null なら演出なし）

    public TurnEndContext(GameSystem system, GameBoard board, PlayerType owner, SpecialContext timeline, MagicFx fx)
    {
        GameSystem = system;
        Board = board;
        Owner = owner;
        Timeline = timeline;
        Fx = fx;
    }
}
