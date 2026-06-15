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

        if(gameSystem.IsPlayer1Turn())
        {
            // 上下左右の移動（wasPressedThisFrame で1回ずつ移動）
            if (keyboard.wKey.wasPressedThisFrame)    Move(1, 0);
            if (keyboard.sKey.wasPressedThisFrame)  Move(-1, 0);
            if (keyboard.aKey.wasPressedThisFrame)  Move(0, -1);
            if (keyboard.dKey.wasPressedThisFrame) Move(0, 1);
        }
        else
        {
            // プレイヤー2のターンの処理
            // カーソルを移動させて駒を選択
            if (keyboard.wKey.wasPressedThisFrame)    Move(-1, 0);
            if (keyboard.sKey.wasPressedThisFrame)  Move(1, 0);
            if (keyboard.aKey.wasPressedThisFrame)  Move(0, 1);
            if (keyboard.dKey.wasPressedThisFrame) Move(0, -1);

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
