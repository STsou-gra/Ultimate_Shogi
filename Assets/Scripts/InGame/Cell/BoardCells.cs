using System.Collections.Generic;
using UnityEngine;

// マスの状態（プロトタイプ src/core/state.js の cells の移植）
//  Normal  … 通常
//  Broken  … 破壊。移動先にも通過にもできず、持ち駒も打てない。対局終了まで戻らない
//  Burning … 炎上。入った駒は消失する。burnUntil のターンが終わると通常に戻る
public enum CellState { Normal, Broken, Burning }

/// <summary>1マスの状態。trap は落とし穴の持ち主（なければ null）</summary>
public class CellData
{
    public CellState state = CellState.Normal;
    public int burnUntil = -1;      // 炎上が終わるターン（このターンの終わりに鎮火する）。炎上していなければ -1
    public PlayerType? trap = null; // 落とし穴の持ち主。両プレイヤーに見え、敵味方に関係なく入った駒が落ちる

    public bool IsBroken => state == CellState.Broken;
    public bool IsBurning => state == CellState.Burning;
    public bool HasTrap => trap.HasValue;

    /// <summary>入った駒が消失するマス（炎上・落とし穴）。移動先には選べるが、走り駒はここで止まる</summary>
    public bool IsHazard => IsBurning || HasTrap;

    public void Reset()
    {
        state = CellState.Normal;
        burnUntil = -1;
        trap = null;
    }
}

/// <summary>
/// 盤の 9×9 マスの状態。GameBoard の駒の配列（boardData）とは別に持つ、MonoBehaviour ではないプレーンなクラス。
/// 添字は駒の配列と同じ GameHelper.CalcPanelNum(x, y)。
/// ここではマスの状態だけを変える。駒の消失・見た目は GameBoard が行う
/// </summary>
public class BoardCells
{
    public const int Size = 9;

    private readonly CellData[] cells = new CellData[Size * Size];

    public BoardCells()
    {
        for (int i = 0; i < cells.Length; i++) cells[i] = new CellData();
    }

    /// <summary>(x,y) のマス。盤外なら null</summary>
    public CellData Get(int x, int y)
    {
        int index = GameHelper.CalcPanelNum(x, y);
        return index == -1 ? null : cells[index];
    }

    public bool IsBroken(int x, int y) { CellData c = Get(x, y); return c != null && c.IsBroken; }
    public bool IsBurning(int x, int y) { CellData c = Get(x, y); return c != null && c.IsBurning; }
    public bool HasTrap(int x, int y) { CellData c = Get(x, y); return c != null && c.HasTrap; }
    public bool IsHazard(int x, int y) { CellData c = Get(x, y); return c != null && c.IsHazard; }

    /// <summary>持ち駒を打てるマスか（破壊マス・落とし穴には打てない）。駒があるかは見ない</summary>
    public bool CanDropOn(int x, int y)
    {
        CellData c = Get(x, y);
        return c != null && !c.IsBroken && !c.HasTrap;
    }

    /// <summary>炎上の残りターン数（いまのターンを含めて数える）。炎上していなければ 0</summary>
    public int BurnRemaining(int x, int y, int turnNumber)
    {
        CellData c = Get(x, y);
        if (c == null || !c.IsBurning) return 0;
        return Mathf.Max(0, c.burnUntil - turnNumber + 1);
    }

    /// <summary>破壊状態にする（炎上・落とし穴は消える）。戻り値: 破壊したか（すでに破壊されていれば false）</summary>
    public bool Break(int x, int y)
    {
        CellData c = Get(x, y);
        if (c == null || c.IsBroken) return false;
        c.state = CellState.Broken;
        c.burnUntil = -1;
        c.trap = null;
        return true;
    }

    /// <summary>
    /// turns ターンのあいだ炎上させる（turnNumber のターンを 1 ターン目として数える）。
    /// すでに炎上していれば、終わるターンが遅い方を残す。破壊マスは炎上しない。戻り値: 炎上させたか
    /// </summary>
    public bool SetBurning(int x, int y, int turns, int turnNumber)
    {
        CellData c = Get(x, y);
        if (c == null || c.IsBroken || turns <= 0) return false;
        int until = turnNumber + turns - 1;
        c.burnUntil = c.IsBurning ? Mathf.Max(c.burnUntil, until) : until;
        c.state = CellState.Burning;
        return true;
    }

    /// <summary>
    /// 落とし穴を仕掛ける。通常のマスにだけ置ける（破壊・炎上・すでに落とし穴があるマスには置けない）。
    /// 駒がいるマスかどうかは GameBoard 側で確かめる。戻り値: 仕掛けたか
    /// </summary>
    public bool SetTrap(int x, int y, PlayerType owner)
    {
        CellData c = Get(x, y);
        if (c == null || c.state != CellState.Normal || c.HasTrap) return false;
        c.trap = owner;
        return true;
    }

    /// <summary>落とし穴を消す（駒が落ちたとき）</summary>
    public void ClearTrap(int x, int y)
    {
        CellData c = Get(x, y);
        if (c != null) c.trap = null;
    }

    /// <summary>ターンの終わり：turnNumber までで炎上が終わるマスを通常に戻す。戻り値: 鎮火したマス</summary>
    public List<Vector2Int> ExtinguishExpired(int turnNumber)
    {
        List<Vector2Int> ended = new List<Vector2Int>();
        for (int i = 0; i < cells.Length; i++)
        {
            CellData c = cells[i];
            if (!c.IsBurning || c.burnUntil > turnNumber) continue;
            c.state = CellState.Normal;
            c.burnUntil = -1;
            int x, y;
            GameHelper.CalcPanelPosition(out x, out y, i);
            ended.Add(new Vector2Int(x, y));
        }
        return ended;
    }

    /// <summary>デバッグ用：マスを通常に戻す（破壊マスも戻る。対局のルールでは使わない）</summary>
    public void DebugReset(int x, int y)
    {
        CellData c = Get(x, y);
        if (c != null) c.Reset();
    }
}
