using UnityEngine;
using UnityEngine.InputSystem;

// 動作確認用：カーソル位置のマスの状態をキーで切り替える（マスを変える魔法がまだないため）
//   1 … 破壊（破壊マスならデバッグ用に通常へ戻す）
//   2 … 炎上 burnTurns ターン（炎上中なら消す）
//   3 … 落とし穴（手番のプレイヤーが仕掛ける。穴があれば消す）
//   4 … 壁（手番のプレイヤーが建てる。壁があれば消す）
//   0 … 通常に戻す
// 破壊・炎上・落とし穴・壁は GameSystem の BreakCell / SetBurning / SetTrap / SetWall を通すので、対局中と同じ処理になる
// （破壊したマスにいた駒は消失する。王将が消えたら勝敗も決まる）。
// エディタと Development Build でだけ動く。駒・持ち駒・スキルの対象を選んでいる間とオンライン対戦中は使えない。
// シーンに置かなくても GameSystem が自動で追加する
public class CellDebugTool : MonoBehaviour
{
    [SerializeField] private bool enableInReleaseBuild = false; // 出展用のビルドでも使いたいときだけオンにする
    [SerializeField] private int burnTurns = 3;

    private GameSystem gameSystem;
    private GameCursor gameCursor;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();
        gameCursor = FindAnyObjectByType<GameCursor>();
        if (!(Application.isEditor || Debug.isDebugBuild || enableInReleaseBuild)) enabled = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || gameSystem == null || gameCursor == null) return;

        bool breakKey = keyboard.digit1Key.wasPressedThisFrame;
        bool burnKey = keyboard.digit2Key.wasPressedThisFrame;
        bool trapKey = keyboard.digit3Key.wasPressedThisFrame;
        bool wallKey = keyboard.digit4Key.wasPressedThisFrame;
        bool clearKey = keyboard.digit0Key.wasPressedThisFrame;
        if (!breakKey && !burnKey && !trapKey && !wallKey && !clearKey) return;
        if (RulesOverlayView.IsBlockingInput) return;

        if (!CanUse()) return;

        BoardCells cells = gameSystem.Cells;
        if (cells == null) return;
        int x = gameCursor.X;
        int y = gameCursor.Y;
        BoardCell cell = cells.At(x, y);
        if (cell == null) return;

        string message;
        if (clearKey)
        {
            message = cells.ClearForDebug(x, y) ? "通常に戻した" : "変化なし";
        }
        else if (breakKey)
        {
            if (cell.IsBroken)
            {
                cells.ClearForDebug(x, y);
                message = "破壊を元に戻した（デバッグのみ）";
            }
            else
            {
                message = gameSystem.BreakCell(x, y) ? "破壊した" : "破壊できない";
            }
        }
        else if (burnKey)
        {
            if (cell.IsBurning)
            {
                cells.ClearForDebug(x, y);
                message = "炎上を消した";
            }
            else
            {
                message = gameSystem.SetBurning(x, y, burnTurns) ? $"{burnTurns} ターン炎上させた" : "炎上できない（破壊マス・壁）";
            }
        }
        else if (wallKey)
        {
            if (cell.IsWall)
            {
                cells.ClearForDebug(x, y);
                message = "壁を消した";
            }
            else
            {
                PlayerType owner = gameSystem.IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
                message = gameSystem.SetWall(x, y, owner)
                    ? $"{(owner == PlayerType.Player1 ? "先手" : "後手")}の壁を建てた"
                    : "壁を建てられない（駒がいる・破壊・炎上・落とし穴のマス）";
            }
        }
        else
        {
            if (cell.HasTrap)
            {
                cells.ClearForDebug(x, y);
                message = "落とし穴を消した";
            }
            else
            {
                PlayerType owner = gameSystem.IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;
                message = gameSystem.SetTrap(x, y, owner)
                    ? $"{(owner == PlayerType.Player1 ? "先手" : "後手")}の落とし穴を仕掛けた"
                    : "落とし穴を置けない（駒がいる・破壊・炎上のマス）";
            }
        }

        Debug.Log($"[CellDebug] ({x}, {y}) {message}");
        gameSystem.RefreshAfterBoardChange(); // 破壊で王将が消えたときの勝敗・王手の表示
        if (gameSystem.IsInTurn) gameSystem.ShowIdleStatus($"【デバッグ】({x}, {y}) {message}");
    }

    // 盤面を選んでいない手番中だけ使える（選択中に変えると、光らせている移動先とずれるため）
    bool CanUse()
    {
        if (!gameSystem.IsInTurn) return false;
        if (NetworkManager.Instance != null && NetworkManager.Instance.IsOnlineMatch) return false;
        return gameSystem.SelectedPiece == null && !gameSystem.IsSelectingHand && !gameSystem.IsDroppingHand
            && !gameSystem.IsWaitingForPromoteChoice && !gameSystem.IsSelectingSkillTarget;
    }
}
