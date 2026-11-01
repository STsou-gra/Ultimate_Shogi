using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

// 動作確認用：カーソル位置のマスの状態をキーで切り替える（魔法が入るまでの仮）
//  1 … マス破壊（そこにいた駒は消失）
//  2 … 炎上（burnTurns ターン。すでに炎上していれば延長）
//  3 … 落とし穴（手番のプレイヤーが仕掛ける）
//  4 … 落とし穴（相手のプレイヤーが仕掛ける）
//  0 … 通常に戻す（デバッグ専用。破壊マスも戻る）
// GameSystem がエディタ・開発ビルドのときだけ自動で追加する。
// 駒の選択中・持ち駒を打つ場所の選択中などは移動先の表示が崩れるので、何も選んでいないときだけ受け付ける
public class CellDebugInput : MonoBehaviour
{
    [SerializeField] private int burnTurns = 3;

    private GameSystem gameSystem;
    private GameBoard gameBoard;
    private GameCursor gameCursor;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();
        gameBoard = FindAnyObjectByType<GameBoard>();
        gameCursor = FindAnyObjectByType<GameCursor>();
        Debug.Log("[マスのデバッグ] カーソル位置で 1:破壊 / 2:炎上 / 3:落とし穴(自分) / 4:落とし穴(相手) / 0:通常に戻す");
    }

    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || gameSystem == null || gameBoard == null || gameCursor == null) return;
        if (!gameSystem.IsInTurn) return;
        if (gameSystem.SelectedPiece != null || gameSystem.IsSelectingHand || gameSystem.IsDroppingHand ||
            gameSystem.IsWaitingForPromoteChoice || gameSystem.IsSelectingSkillTarget) return;

        int x = gameCursor.X;
        int y = gameCursor.Y;
        PlayerType me = gameSystem.IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
        PlayerType opponent = me == PlayerType.Player1 ? PlayerType.Player2 : PlayerType.Player1;

        bool changed;
        string label;
        if (Pressed(keyboard.digit1Key, keyboard.numpad1Key))
        {
            label = "マス破壊";
            changed = gameBoard.BreakCell(x, y);
        }
        else if (Pressed(keyboard.digit2Key, keyboard.numpad2Key))
        {
            label = $"炎上（{burnTurns}ターン）";
            changed = gameBoard.SetBurning(x, y, burnTurns);
        }
        else if (Pressed(keyboard.digit3Key, keyboard.numpad3Key))
        {
            label = $"落とし穴（{me}）";
            changed = gameBoard.SetTrap(x, y, me);
        }
        else if (Pressed(keyboard.digit4Key, keyboard.numpad4Key))
        {
            label = $"落とし穴（{opponent}）";
            changed = gameBoard.SetTrap(x, y, opponent);
        }
        else if (Pressed(keyboard.digit0Key, keyboard.numpad0Key))
        {
            label = "通常に戻す";
            gameBoard.DebugResetCell(x, y);
            changed = true;
        }
        else
        {
            return;
        }

        if (changed)
        {
            Debug.Log($"[マスのデバッグ] ({x},{y}) を {label}");
            gameSystem.RefreshAfterCellChange();
        }
        else
        {
            Debug.Log($"[マスのデバッグ] ({x},{y}) には {label} を使えません（破壊マス・駒がいるマスなど）");
        }
    }

    static bool Pressed(KeyControl a, KeyControl b)
    {
        return (a != null && a.wasPressedThisFrame) || (b != null && b.wasPressedThisFrame);
    }
}
