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
            if (moveUp) Move(1, 0);
            if (moveDown) Move(-1, 0);
            if (moveLeft) Move(0, -1);
            if (moveRight) Move(0, 1);
        }
        else
        {
            // プレイヤー2のターンの処理
            if (moveUp) Move(-1, 0);
            if (moveDown) Move(1, 0);
            if (moveLeft) Move(0, 1);
            if (moveRight) Move(0, -1);
        }

        // 見た目の位置を更新
        UpdatePosition();
    }

    public void SetPosition(int x, int y)
    {
        X = Mathf.Clamp(x, 0, 8);
        Y = Mathf.Clamp(y, 0, 8);
        UpdatePosition();
    }

    void Move(int dx, int dy)
    {
        // 駒移動の選択中、または持ち駒配置マス選択中
        bool hasConstraint = gameSystem != null && (gameSystem.SelectedPiece != null || gameSystem.IsDroppingHand);
        if (hasConstraint && gameSystem.CurrentMovablePositions != null && gameSystem.CurrentMovablePositions.Count > 0)
        {
            Vector2Int bestPos = new Vector2Int(X, Y);
            float minDistance = float.MaxValue;
            bool found = false;

            foreach (var pos in gameSystem.CurrentMovablePositions)
            {
                // 自分自身のマスはスキップ対象外
                if (pos.x == X && pos.y == Y) continue;

                bool matchDirection = false;

                // 入力方向(dx, dy)に対するフィルタリング
                if (dx > 0 && pos.x > X) matchDirection = true;
                else if (dx < 0 && pos.x < X) matchDirection = true;
                else if (dy > 0 && pos.y > Y) matchDirection = true;
                else if (dy < 0 && pos.y < Y) matchDirection = true;

                if (matchDirection)
                {
                    // マンハッタン距離を計算
                    float dist = Mathf.Abs(pos.x - X) + Mathf.Abs(pos.y - Y);
                    if (dist < minDistance)
                    {
                        minDistance = dist;
                        bestPos = pos;
                        found = true;
                    }
                }
            }

            if (found)
            {
                X = bestPos.x;
                Y = bestPos.y;
            }
        }
        else
        {
            // 通常時：0〜8の範囲内に収める（Clamp）
            X = Mathf.Clamp(X + dx, 0, 8);
            Y = Mathf.Clamp(Y + dy, 0, 8);
        }
    }

    void UpdatePosition()
    {
        // GameHelper を使ってワールド座標に変換して移動
        transform.localPosition = GameHelper.CalcPanelLocation(X, Y);
    }
}
