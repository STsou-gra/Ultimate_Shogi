using System.Collections.Generic;
using UnityEngine;

// マスの状態（Prototype の src/core/state.js の cells / board.js の cellAt・isBroken・isHazard /
// effects.js の breakCell・setBurning・setTrap・setWall のうち、データを書き換える部分）
//
// GameBoard の駒の配列（GamePiece[81]）とは別に、マスごとの状態を持つプレーンなクラス。
// 添字は駒の配列と同じ GameHelper.CalcPanelNum(x, y)（x = 段、y = 筋）。
// ここではマスのデータだけを書き換える。そこにいる駒を消すなどの処理は GameSystem の
// BreakCell / SetBurning / SetTrap / SetWall / EnterCell を通すこと（盤上の駒の消失は GameSystem.VanishPiece が受け持つ）
public enum CellState
{
    Normal,  // 通常
    Broken,  // 破壊：移動先にも通過にもできない・持ち駒も打てない。対局終了まで戻らない
    Burning, // 炎上：入った駒は消失する。BurnUntil のターンの終わりに通常に戻る
    Wall,    // 壁（通常魔法「壁」。Prototype の state 'wall'）：敵味方問わず移動先にも通過にもできない・持ち駒も打てない。
             // 対局終了まで残る（取り・爆発・隕石・炎上では壊れない）。マスが破壊されると壁ごと崩れる。天変地異ではマスと一緒に動く
}

// マスの状態が変わったときの種類（見た目の更新・演出に使う）
public enum CellChange
{
    Broken,        // 破壊された
    Burning,       // 炎上した（炎上中のマスの期限が延びたときも）
    Extinguished,  // 炎上が終わって通常に戻った
    TrapSet,       // 落とし穴が仕掛けられた
    TrapSprung,    // 駒が落とし穴に落ちて、穴が消えた
    WallSet,       // 壁が建った
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

    // 壁を建てたプレイヤー（Prototype の cell.wall.owner）。壁のマス（State == Wall）だけ値がある
    public PlayerType? WallOwner { get; internal set; }

    public bool IsBroken => State == CellState.Broken;
    public bool IsBurning => State == CellState.Burning;
    public bool HasTrap => Trap.HasValue;
    public bool IsWall => State == CellState.Wall;

    // 駒が入れない・通れないマス（破壊マス・壁。Prototype の isBlocked）。走り駒は手前で止まり、持ち駒も打てない
    public bool IsBlocked => IsBroken || IsWall;

    // 入った駒が消失するマス（炎上・落とし穴）。移動先には選べるが、走り駒はここで止まる
    public bool IsHazard => IsBurning || HasTrap;

    internal void Reset()
    {
        State = CellState.Normal;
        BurnUntil = null;
        Trap = null;
        WallOwner = null;
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

    // マスの状態が入れ替わった（Rearrange。from[i] にあった状態が to[i] へ移った）。見た目もマスごと付け替える
    public event System.Action<IReadOnlyList<Vector2Int>, IReadOnlyList<Vector2Int>> Rearranged;

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

    public bool IsWall(int x, int y)
    {
        BoardCell cell = At(x, y);
        return cell != null && cell.IsWall;
    }

    // 駒が入れない・通れないマスか（破壊マス・壁）。盤の外は false（盤の外かどうかは呼び出し側で調べる）
    public bool IsBlocked(int x, int y)
    {
        BoardCell cell = At(x, y);
        return cell != null && cell.IsBlocked;
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

    // マスを破壊する（対局終了まで戻らない）。炎上・落とし穴は消え、壁は崩れる。破壊したら true
    public bool Break(int x, int y)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.IsBroken) return false;
        cell.State = CellState.Broken;
        cell.BurnUntil = null;
        cell.Trap = null;
        cell.WallOwner = null;
        Changed?.Invoke(x, y, CellChange.Broken);
        return true;
    }

    // マスを turns ターンのあいだ炎上させる（turnNumber を 1 ターン目として数え、BurnUntil のターンの終わりに鎮火）。
    // すでに炎上中なら、終わるターンの遅い方を残す。破壊マス・壁のマスは炎上しない。炎上させたら true
    public bool SetBurning(int x, int y, int turns, int turnNumber)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.IsBlocked || turns <= 0) return false;
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

    // 壁を建てる（owner は建てたプレイヤー）。通常のマス（破壊・炎上・壁でない）で、落とし穴がないときだけ。建てたら true。
    // 駒がいるマスには建てられない（駒の有無は GameSystem.SetWall が調べる）
    public bool SetWall(int x, int y, PlayerType owner)
    {
        BoardCell cell = At(x, y);
        if (cell == null || cell.State != CellState.Normal || cell.HasTrap) return false;
        cell.State = CellState.Wall;
        cell.BurnUntil = null;
        cell.WallOwner = owner;
        Changed?.Invoke(x, y, CellChange.WallSet);
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

    // マスの状態（炎上と終わるターン・落とし穴・壁）を丸ごと入れ替える（超常魔法「天変地異」。Prototype の effects.js の shuffleCells）。
    // from[i] のマスの状態が to[i] へ移る（from と to は同じマスの集まりを並べ替えたもの）。
    // マスごと動くので、炎上・落とし穴は発動しない（壁もマスと一緒に動く）（そこにいる駒は GameBoard.RearrangePieces で一緒に動かす）
    public void Rearrange(IReadOnlyList<Vector2Int> from, IReadOnlyList<Vector2Int> to)
    {
        if (from == null || to == null || from.Count != to.Count) return;
        BoardCell[] before = (BoardCell[])cells.Clone();
        for (int i = 0; i < from.Count; i++)
        {
            int src = GameHelper.CalcPanelNum(from[i].x, from[i].y);
            int dst = GameHelper.CalcPanelNum(to[i].x, to[i].y);
            if (src < 0 || dst < 0) continue;
            cells[dst] = before[src];
        }
        Rearranged?.Invoke(from, to);
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

    // 千日手の「同じ局面」の判定用に、マスの状態を文字列にする（Prototype の hash.js と同じく、状態・終わるターン・穴の持ち主・壁の持ち主）
    public void AppendKey(System.Text.StringBuilder sb)
    {
        sb.Append('#');
        foreach (BoardCell cell in cells)
        {
            switch (cell.State)
            {
                case CellState.Broken: sb.Append('x'); break;
                case CellState.Burning: sb.Append('f').Append(cell.BurnUntil ?? 0); break;
                case CellState.Wall: sb.Append(cell.WallOwner == PlayerType.Player2 ? 'W' : 'w'); break;
                default: sb.Append('.'); break;
            }
            if (cell.HasTrap) sb.Append(cell.Trap.Value == PlayerType.Player1 ? 'p' : 'q');
        }
    }
}
