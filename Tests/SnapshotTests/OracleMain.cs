using UnityEngine;
// 駒の動きの「正解」：PieceMoves に移す前の各駒のクラス（main 1af054c の Piece/*.cs）の CanMoveByPattern をそのまま写したもの。
// PieceMoves を変えるときは、意図した違いならここも同じように変える
public abstract class OracleBase { public int X, Y; public PlayerType player; public bool isPromoted; public abstract bool CanMoveByPattern(int toX, int toY); }
public class OraclePawn : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;

        // プレイヤー1は上(-x)、2は下(+x)
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;

        if (isPromoted)
        {
            // と金（成金）：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        return (dx == forwardX && dy == 0);
} }
public class OracleLance : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 成香：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 香：前どこまでも（本来は間に駒がないか判定が必要ですが、まずは方向だけ）
        return (forwardX < 0 && forwardY == 0);
} }
public class OracleKnight : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 成桂：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 桂：2つ前、左右1つ
        return (forwardX == -2 && Mathf.Abs(forwardY) == 1);
} }
public class OracleSilverGeneral : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 成銀：金将の動きと同じ。前後左右4方向 ＋ 斜め前2方向
            int relativeX = (player == PlayerType.Player1) ? -dx : dx;
            int relativeY = dy;
            return (Mathf.Abs(relativeX) <= 1 && Mathf.Abs(relativeY) <= 1) && !(relativeX == 1 && Mathf.Abs(relativeY) == 1);
        }

        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        // 銀：前3方向 ＋ 斜め後ろ2方向
        return (forwardX == -1 && Mathf.Abs(forwardY) <= 1) || (forwardX == 1 && Mathf.Abs(forwardY) == 1);
} }
public class OracleGoldGeneral : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        // 金：前後左右4方向 ＋ 斜め前2方向
        int forwardX = (player == PlayerType.Player1) ? -dx : dx;
        int forwardY = (player == PlayerType.Player1) ? -dy : dy;
        return (Mathf.Abs(forwardX) <= 1 && Mathf.Abs(forwardY) <= 1) && !(forwardX == 1 && Mathf.Abs(forwardY) == 1);
} }
public class OracleBishop : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 竜馬：斜めどこまでも ＋ 周囲1マス（上下左右4方向も1マスだけ動ける）
            return (Mathf.Abs(dx) == Mathf.Abs(dy)) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1);
        }

        // 角：斜めどこまでも
        return (Mathf.Abs(dx) == Mathf.Abs(dy));
} }
public class OracleRook : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx = toX - X;
        int dy = toY - Y;
        if (isPromoted)
        {
            // 竜王：縦横どこまでも ＋ 周囲1マス（斜め4方向も1マスだけ動ける）
            return (dx == 0 || dy == 0) || (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1);
        }

        // 飛：縦横どこまでも
        return (dx == 0 || dy == 0);
} }
public class OracleKing : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        int dx =  Mathf.Abs(toX - X);
        int dy = Mathf.Abs(toY - Y);
        // 全方向に1マス以内
        return (dx <= 1 && dy <= 1);
} }
public class OracleGunpowder : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        return false;
} }
public class OracleSniper : OracleBase { public override bool CanMoveByPattern(int toX, int toY) {
        return false;
} }
