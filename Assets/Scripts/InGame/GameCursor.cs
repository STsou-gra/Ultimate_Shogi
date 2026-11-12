using UnityEngine;

// 盤のカーソル。席（InputSeat）ごとに 1 つ。
//  ・本将棋型：共通の席のカーソル 1 つ。手番が替わると盤が回るので、後手の手番は上下左右を逆にする
//  ・合戦将棋型：先手・後手の席ごとに 1 つ（後手のカーソルは GameSystem が複製して色を変える）。盤は回らないので逆にしない
// キーは席の割り当て（SeatKeys）を読む
public class GameCursor : MonoBehaviour
{
    // 現在のカーソル位置（0〜8）
    public int X { get; private set; } = 4;
    public int Y { get; private set; } = 4;

    [SerializeField] private GameSystem gameSystem;

    // このカーソルの席（GameSystem が対局の準備で設定する。未設定なら動かない）
    public InputSeat Seat { get; set; }

    public void Bind(GameSystem system, InputSeat seat)
    {
        gameSystem = system;
        Seat = seat;
        seat.Cursor = this;
        UpdatePosition();
    }

    void Start()
    {
        UpdatePosition();
    }

    void Update()
    {
        if (gameSystem == null || Seat == null) return;

        // ルール説明・棋譜メニューを開いている間・一時停止中・相手の時間停止中は動かさない
        if (RulesOverlayView.IsBlockingInput || KifuMenuView.IsBlockingInput) return;
        if (!gameSystem.CanSeatOperate(Seat)) return;

        // 持ち駒の選択中、または成り選択中は盤面のカーソル移動を行わない
        if (Seat.IsSelectingHand || Seat.IsWaitingForPromoteChoice) return;

        SeatKeys keys = Seat.Keys;
        bool moveUp = SeatKeys.Pressed(keys.Up);
        bool moveDown = SeatKeys.Pressed(keys.Down);
        bool moveLeft = SeatKeys.Pressed(keys.Left);
        bool moveRight = SeatKeys.Pressed(keys.Right);
        if (!moveUp && !moveDown && !moveLeft && !moveRight) return;

        if (!gameSystem.IsCursorFlipped(Seat))
        {
            // 上下左右の移動
            if (moveUp) Move(1, 0);
            if (moveDown) Move(-1, 0);
            if (moveLeft) Move(0, -1);
            if (moveRight) Move(0, 1);
        }
        else
        {
            // 盤が後手の側に回っている（本将棋型の後手の手番）
            if (moveUp) Move(-1, 0);
            if (moveDown) Move(1, 0);
            if (moveLeft) Move(0, 1);
            if (moveRight) Move(0, -1);
        }

        // 見た目の位置を更新
        UpdatePosition();
        gameSystem.NotifyCursorMoved(Seat);
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
        bool hasConstraint = Seat.SelectedPiece != null || Seat.IsDroppingHand;
        if (hasConstraint && Seat.MovablePositions.Count > 0)
        {
            Vector2Int bestPos = new Vector2Int(X, Y);
            float minDistance = float.MaxValue;
            bool found = false;

            foreach (var pos in Seat.MovablePositions)
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
