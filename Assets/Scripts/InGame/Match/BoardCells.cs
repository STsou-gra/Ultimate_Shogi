using System.Collections.Generic;
using UnityEngine;

// マスの状態（Prototype の src/core/state.js の cells / board.js の cellAt・isBroken・isHazard /
// effects.js の breakCell・setBurning・setTrap のうち、データを書き換える部分）
//
// GameBoard の駒の配列（GamePiece[81]）とは別に、マスごとの状態を持つプレーンなクラス。
// 添字は駒の配列と同じ GameHelper.CalcPanelNum(x, y)（x = 段、y = 筋）。
// ここではマスのデータだけを書き換える。そこにいる駒を消すなどの処理は GameSystem の
// BreakCell / SetBurning / SetTrap / EnterCell を通すこと（盤上の駒の消失は GameSystem.VanishPiece が受け持つ）
public enum CellState
{
    Normal,  // 通常
    Broken,  // 破壊：移動先にも通過にもできない・持ち駒も打てない。対局終了まで戻らない
    Burning, // 炎上：入った駒は消失する。BurnUntil のターンの終わりに通常に戻る
}

// マスの状態が変わったときの種類（見た目の更新・演出に使う）
public enum CellChange
{
    Broken,        // 破壊された
    Burning,       // 炎上した（炎上中のマスの期限が延びたときも）
    Extinguished,  // 炎上が終わって通常に戻った
    TrapSet,       // 落とし穴が仕掛けられた
    TrapSprung,    // 駒が落とし穴に落ちて、穴が消えた
    Cleared,       // デバッグ用に通常へ戻した
}

// 1マスの状態
public class BoardCell
{
    public CellState State { get; internal set; } = CellState.Normal;

    // 炎上が終わるターン（このターンの終わりに鎮火する）。炎上中でなければ null
    public int? BurnUntil { get; internal set; }

    // 落とし穴の持ち主（仕掛けたプレイヤー）。落とし穴がなければ null。
    // 穴は両プレイヤーに見え、敵味方に関係なく入った駒が落ちる
    public PlayerType? Trap { get; internal set; }

    public bool IsBroken => State == CellState.Broken;
    public bool IsBurning => State == CellState.Burning;
    public bool HasTrap => Trap.HasValue;

    // 入った駒が消失するマス（炎上・落とし穴）。移動先には選べるが、走り駒はここで止まる
    public bool IsHazard => IsBurning || HasTrap;

    internal void Reset()
    {
        State = CellState.Normal;
        BurnUntil = null;
        Trap = null;
    }
}

public class BoardCells
{
    public const int Size = 9;

    // 消失の理由（PieceDefeat.Reason。演出で見分けるのに使う）
    public const string ReasonBroken = "マス破壊";
    public const string ReasonBurning = "炎上";
    public const string ReasonTrap = "落とし穴";

    private readonly BoardCell[] cells = new BoardCell[Size * Size];

    // マスの状態が変わった（x = 段, y = 筋, 変化の種類）
    public event System.Action<int, int, CellChange> Changed;

    public BoardCells()
    {
        for (int i = 0; i < cells.Length; i++) cells[i] = new BoardCell();
    }

    public static bool InBounds(int x, int y)
    {
        return x >= 0 && x < Size && y >= 0 && y < Size;
    }

    // (x, y) のマス。盤の外なら null
    public BoardCell At(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        return index < 0 ? null : cells[index];
    }

    public bool IsBroken(int x, int y)
    {
        BoardCell cell = At(x, y);
        return cell != null && cell.IsBroken;
    }

    public bool IsHazard(int x, int y)
    {
        BoardCell cell = At(x, y);
        return cell != null && cell.IsHazard;
    }

    // 炎上の残りターン数（発動したターンを含めて数える）。炎上中でなければ 0
    public int BurnRemaining(int x, int y, int turnNumber)
    {
        BoardCell cell = At(x, y);
        if (cell == null || !cell.IsBurning || !cell.BurnUntil.HasValue) return 0;
        return cell.BurnUntil.Value - turnNumber + 1;
    }

    // マスを破壊する（対局終了まで戻らない）。炎上・落とし穴は消える。破壊したら true
    public bool Break(int x, int y)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.IsBroken) return false;
        cell.State = CellState.Broken;
        cell.BurnUntil = null;
        cell.Trap = null;
        Changed?.Invoke(x, y, CellChange.Broken);
        return true;
    }

    // マスを turns ターンのあいだ炎上させる（turnNumber を 1 ターン目として数え、BurnUntil のターンの終わりに鎮火）。
    // すでに炎上中なら、終わるターンの遅い方を残す。破壊マスは炎上しない。炎上させたら true
    public bool SetBurning(int x, int y, int turns, int turnNumber)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.IsBroken || turns <= 0) return false;
        int until = turnNumber + turns - 1;
        cell.BurnUntil = cell.IsBurning && cell.BurnUntil.HasValue ? Mathf.Max(cell.BurnUntil.Value, until) : until;
        cell.State = CellState.Burning;
        Changed?.Invoke(x, y, CellChange.Burning);
        return true;
    }

    // 落とし穴を仕掛ける。通常のマス（破壊・炎上でない）で、まだ穴がないときだけ。仕掛けたら true。
    // 駒がいるマスには置けない（駒の有無は GameSystem.SetTrap が調べる）
    public bool SetTrap(int x, int y, PlayerType owner)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.State != CellState.Normal || cell.HasTrap) return false;
        cell.Trap = owner;
        Changed?.Invoke(x, y, CellChange.TrapSet);
        return true;
    }

    // 駒が落とし穴に落ちた：穴を消す。穴があったら true
    public bool SpringTrap(int x, int y)
    {
        BoardCell cell = At(x, y);
        if (cell == null || !cell.HasTrap) return false;
        cell.Trap = null;
        Changed?.Invoke(x, y, CellChange.TrapSprung);
        return true;
    }

    // 1ターンの終わり：BurnUntil が turnNumber 以下の炎上を鎮火する。鎮火したマスを返す
    public List<Vector2Int> ExtinguishExpired(int turnNumber)
    {
        List<Vector2Int> ended = new List<Vector2Int>();
        for (int x = 0; x < Size; x++)
        {
            for (int y = 0; y < Size; y++)
            {
                BoardCell cell = At(x, y);
                if (!cell.IsBurning || (cell.BurnUntil.HasValue && cell.BurnUntil.Value > turnNumber)) continue;
                cell.State = CellState.Normal;
                cell.BurnUntil = null;
                ended.Add(new Vector2Int(x, y));
                Changed?.Invoke(x, y, CellChange.Extinguished);
            }
        }
        return ended;
    }

    // デバッグ用：マスを通常に戻す（破壊マスも戻る。対局のルールでは使わない）。変わったら true
    public bool ClearForDebug(int x, int y)
    {
        BoardCell cell = At(x, y);
        if (cell == null || (cell.State == CellState.Normal && !cell.HasTrap)) return false;
        cell.Reset();
        Changed?.Invoke(x, y, CellChange.Cleared);
        return true;
    }

    // 千日手の「同じ局面」の判定用に、マスの状態を文字列にする（Prototype の hash.js と同じく、状態・終わるターン・穴の持ち主）
    public void AppendKey(System.Text.StringBuilder sb)
    {
        sb.Append('#');
        foreach (BoardCell cell in cells)
        {
            switch (cell.State)
            {
                case CellState.Broken: sb.Append('x'); break;
                case CellState.Burning: sb.Append('f').Append(cell.BurnUntil ?? 0); break;
                default: sb.Append('.'); break;
            }
            if (cell.HasTrap) sb.Append(cell.Trap.Value == PlayerType.Player1 ? 'p' : 'q');
        }
    }
}
