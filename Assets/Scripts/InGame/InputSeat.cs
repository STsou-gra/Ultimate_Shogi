using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 操作する席 1 つ分の入力の状態（カーソル・駒や持ち駒の選択・成りの確認・狙撃や魔法の対象選択）。
//
//  ・本将棋型：席は 1 つ（Shared）。手番のプレイヤーが、今までどおりのキー（SeatKeys.Shared）で操作する
//  ・合戦将棋型：席は先手・後手の 2 つ。1 台のキーボードを左右に分け（SeatKeys.KassenSente / KassenGote）、
//    それぞれのカーソルで同時に操作する。自分の駒・自分の持ち駒・自分の魔法のキーを使えば、その人の行動になる
//
// GameSystem の選択状態のフィールド（selectedPiece など）は、今の席（GameSystem の seat）のここを指す。
// 席の入力を処理する間と、その席の行動を実行する間だけ、GameSystem がその席に切り替える（GameSystem.Realtime.cs）
public class InputSeat
{
    public readonly PlayerType? Owner; // 合戦将棋型の席の持ち主（本将棋型の共通の席は null）
    public readonly SeatKeys Keys;
    public GameCursor Cursor;

    // ---- 選択の状態（もとは GameSystem のフィールド） ----
    public GamePiece SelectedPiece;
    public bool IsSelectingHand;
    public bool IsDroppingHand;
    public PieceType SelectedHandType;
    public readonly List<PieceType> AvailableHandTypes = new List<PieceType>();
    public int SelectedHandIndex;

    public bool IsWaitingForPromoteChoice;
    public int PendingFromX, PendingFromY, PendingToX, PendingToY;
    public bool PromoteChoice = true;

    public readonly List<Vector2Int> MovablePositions = new List<Vector2Int>();
    public readonly List<GamePiece> HighlightedPieces = new List<GamePiece>();

    public object TargetSelection;                 // GameSystem.TargetSelection（魔法・必殺技の対象選択中。なければ null）
    public Vector2Int LastPreviewCursor;
    public readonly List<Vector2Int> PreviewPanels = new List<Vector2Int>(); // 対象選択中に光らせているマス（合戦将棋型の表示の重ね合わせ用）

    public bool IsSelectingSnipeTarget;
    public GamePiece SnipingPiece;
    public readonly List<Vector2Int> SnipeTargets = new List<Vector2Int>();

    // この席で最後に出した案内文（合戦将棋型の HUD に席ごとに出す）
    public string Status = "";

    public InputSeat(PlayerType? owner, SeatKeys keys)
    {
        Owner = owner;
        Keys = keys;
    }

    // 何かを選んでいる途中か
    public bool IsBusySelecting =>
        SelectedPiece != null || IsSelectingHand || IsDroppingHand || IsWaitingForPromoteChoice || TargetSelection != null || IsSelectingSnipeTarget;
}

// 席のキーの割り当て
public class SeatKeys
{
    public string Name;
    public Key[] Up, Down, Left, Right;
    public Key[] Ok, Cancel;
    public Key[] Hand, Special, Snipe, Release, Orient, NoPromote;
    public Key[] Forge;  // 錬成（レシピの順）
    public Key[] Skills; // 魔法（編成の枠の順。空なら キーでは使えない＝スキルボタンをクリック）

    // 決定・取消を GameController（F・Space の押下の検出）で読むか（本将棋型の今までの操作）
    public bool UsesGameController;

    static Key[] K(params Key[] keys) => keys;

    // 本将棋型（今までどおり）：WASD・矢印でカーソル、F 決定、Space 取消、H 持ち駒、E 必殺技、G 狙撃、Q 解除、R 向き、J 成らない、Z・X・C・V 錬成
    public static readonly SeatKeys Shared = new SeatKeys
    {
        Name = "共通",
        Up = K(Key.W, Key.UpArrow), Down = K(Key.S, Key.DownArrow), Left = K(Key.A, Key.LeftArrow), Right = K(Key.D, Key.RightArrow),
        Ok = K(Key.F), Cancel = K(Key.Space),
        Hand = K(Key.H), Special = K(Key.E), Snipe = K(Key.G), Release = K(Key.Q), Orient = K(Key.R), NoPromote = K(Key.J),
        Forge = K(Key.Z, Key.X, Key.C, Key.V),
        Skills = K(),
        UsesGameController = true,
    };

    // 合戦将棋型・先手（キーボードの左側）：WASD でカーソル、F 決定、Space 取消、H 持ち駒、E 必殺技、G 狙撃、Q 解除、R 向き、Z・X 錬成、1〜5 魔法
    public static readonly SeatKeys KassenSente = new SeatKeys
    {
        Name = "先手",
        Up = K(Key.W), Down = K(Key.S), Left = K(Key.A), Right = K(Key.D),
        Ok = K(Key.F), Cancel = K(Key.Space),
        Hand = K(Key.H), Special = K(Key.E), Snipe = K(Key.G), Release = K(Key.Q), Orient = K(Key.R), NoPromote = K(),
        Forge = K(Key.Z, Key.X),
        Skills = K(Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5),
    };

    // 合戦将棋型・後手（キーボードの右側）：矢印でカーソル、Enter 決定、Backspace（右 Shift）取消、K 持ち駒、O 必殺技、L 狙撃、U 解除、P 向き、N・M 錬成、6〜0 魔法
    public static readonly SeatKeys KassenGote = new SeatKeys
    {
        Name = "後手",
        Up = K(Key.UpArrow), Down = K(Key.DownArrow), Left = K(Key.LeftArrow), Right = K(Key.RightArrow),
        Ok = K(Key.Enter, Key.NumpadEnter), Cancel = K(Key.Backspace, Key.RightShift),
        Hand = K(Key.K), Special = K(Key.O), Snipe = K(Key.L), Release = K(Key.U), Orient = K(Key.P), NoPromote = K(),
        Forge = K(Key.N, Key.M),
        Skills = K(Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9, Key.Digit0),
    };

    // keys のどれかがこのフレームに押されたか
    public static bool Pressed(Key[] keys)
    {
        Keyboard kb = Keyboard.current;
        if (kb == null || keys == null) return false;
        foreach (Key k in keys)
        {
            if (kb[k].wasPressedThisFrame) return true;
        }
        return false;
    }

    // この席のキーのどれかがこのフレームに押されたか（合戦将棋型で、どちらの席の入力を処理するかを決める）
    public bool AnyPressed()
    {
        foreach (Key[] keys in All())
        {
            if (Pressed(keys)) return true;
        }
        return false;
    }

    IEnumerable<Key[]> All()
    {
        yield return Up; yield return Down; yield return Left; yield return Right;
        yield return Ok; yield return Cancel;
        yield return Hand; yield return Special; yield return Snipe; yield return Release; yield return Orient; yield return NoPromote;
        yield return Forge; yield return Skills;
    }

    // keys[index] の表示名（なければ ""）
    public static string Label(Key[] keys, int index = 0)
    {
        if (keys == null || index < 0 || index >= keys.Length) return "";
        return KeyName(keys[index]);
    }

    // 複数のキーをまとめた表示名（「W・A・S・D」など）
    public static string Labels(params Key[][] groups)
    {
        List<string> names = new List<string>();
        foreach (Key[] keys in groups)
        {
            string n = Label(keys);
            if (!string.IsNullOrEmpty(n)) names.Add(n);
        }
        return string.Join("・", names);
    }

    public static string KeyName(Key k)
    {
        switch (k)
        {
            case Key.UpArrow: return "↑";
            case Key.DownArrow: return "↓";
            case Key.LeftArrow: return "←";
            case Key.RightArrow: return "→";
            case Key.Enter: return "Enter";
            case Key.NumpadEnter: return "Enter";
            case Key.Backspace: return "BackSpace";
            case Key.RightShift: return "右Shift";
            case Key.Space: return "Space";
            case Key.Digit0: return "0";
            case Key.Digit1: return "1";
            case Key.Digit2: return "2";
            case Key.Digit3: return "3";
            case Key.Digit4: return "4";
            case Key.Digit5: return "5";
            case Key.Digit6: return "6";
            case Key.Digit7: return "7";
            case Key.Digit8: return "8";
            case Key.Digit9: return "9";
            default: return k.ToString();
        }
    }
}
