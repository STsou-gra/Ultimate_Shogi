using System.Reflection;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameCursor : MonoBehaviour
{
    // 現在のカーソル位置（0〜8）
    public int X { get; private set; } = 4;
    public int Y { get; private set; } = 4;

    [SerializeField] private GameSystem gameSystem;
    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard == null) return;

        // 持ち駒の選択中、または成り選択中は盤面のカーソル移動を行わない
        if (gameSystem != null && (gameSystem.IsSelectingHand || gameSystem.IsWaitingForPromoteChoice)) return;

        bool moveUp = keyboard.wKey.wasPressedThisFrame || keyboard.upArrowKey.wasPressedThisFrame;
        bool moveDown = keyboard.sKey.wasPressedThisFrame || keyboard.downArrowKey.wasPressedThisFrame;
        bool moveLeft = keyboard.aKey.wasPressedThisFrame || keyboard.leftArrowKey.wasPressedThisFrame;
        bool moveRight = keyboard.dKey.wasPressedThisFrame || keyboard.rightArrowKey.wasPressedThisFrame;

        if (gameSystem.IsPlayer1Turn())
        {
            // 上下左右の移動
            if (moveUp)    Move(1, 0);
            if (moveDown)  Move(-1, 0);
            if (moveLeft)  Move(0, -1);
            if (moveRight) Move(0, 1);
        }
        else
        {
            // プレイヤー2のターンの処理
            if (moveUp)    Move(-1, 0);
            if (moveDown)  Move(1, 0);
            if (moveLeft)  Move(0, 1);
            if (moveRight) Move(0, -1);
        }

        // 見た目の位置を更新
        UpdatePosition();
    }

    void Move(int dx, int dy)
    {
        // 0〜8の範囲内に収める（Clamp）
        X = Mathf.Clamp(X + dx, 0, 8);
        Y = Mathf.Clamp(Y + dy, 0, 8);
    }

    void UpdatePosition()
    {
        // GameHelper を使ってワールド座標に変換して移動
        transform.localPosition = GameHelper.CalcPanelLocation(X, Y);
    }
}
