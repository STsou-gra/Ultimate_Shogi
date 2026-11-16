using UnityEngine;
using System.Collections.Generic;

public interface IGameRule
{
    bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board);
    bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board);
}

/// <summary>
/// 駒自体の動き、進路上に障害物がないか、移動先に自分の駒がないかなどの基本移動ルール。
/// 破壊マス・壁のマスは移動先にも通過にもできない（移動先は各駒の CanMove、通り道は IsPathBlocked で判定。桂馬は壁を飛び越せる）。
/// 炎上・落とし穴のマスは移動先に選べる（入った駒は消失する）が、走り駒はそこで止まる。
/// 火薬のマスには、敵味方に関係なく入れる（入ると爆発する）。
/// 翼状態の駒（鳥・魔法「翼」。PieceRules.HasWing）は破壊マスにも入れ、走り駒なら破壊・炎上・落とし穴のマスと火薬の上を通り抜ける。
/// 鳥は火薬を爆発させずに取る（味方の火薬のマスには入れない。PieceRules.CanLandOn）
/// </summary>
public class BaseMoveRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        // 巨人：前へまっすぐ 1〜5 マス（移動先は先頭の 1 列の中央。GiantRules）。時間停止中は動けない
        if (PieceRules.IsGiant(piece))
        {
            if (board.TimeStop != null) return false;
            return CheckRules.GiantMoves(board.GetBoardData(), board.Cells, piece, null).Contains(new MatchSquare(toX, toY));
        }

        // 基本の動き（各駒の CanMove。破壊マス・壁には行けない）
        if (!piece.CanMove(toX, toY)) return false;

        // 障害物チェック（桂馬以外）：途中の駒・破壊マス・壁・炎上・落とし穴
        if (piece.type != PieceType.Knight)
        {
            if (GameHelper.IsPathBlocked(board.GetBoardData(), board.Cells, piece, piece.X, piece.Y, toX, toY))
            {
                return false;
            }
        }

        // 移動先に自分の駒があるか（自分の火薬には入れる）
        GamePiece targetPiece = board.GetPieceAt(toX, toY);
        if (targetPiece == piece || !PieceRules.CanLandOn(piece, targetPiece))
        {
            return false; // 自分の駒があるので移動できない
        }
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        return true; // 移動ルールなのでDrop判定は常にパス
    }
}

/// <summary>
/// すでに駒が置かれているマスには持ち駒を打てないルール
/// </summary>
public class EmptyCellDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (PieceRules.IsGiant(type))
        {
            // 巨人は中央のマスを選ぶ：9 マスとも盤内の、駒のいない通常状態のマス（破壊・炎上・落とし穴・壁・時間停止の予定の駒がない）。
            // 前へ 1 マスも動けない位置には打てない（序盤の敵陣は EnemyZoneDropLockRule）
            return GiantRules.CanDropAt(player, toX, toY, (x, y) =>
            {
                BoardCell c = board.Cells.At(x, y);
                return !board.HasPieceAt(x, y) && c != null && c.State == CellState.Normal && !c.HasTrap
                    && !(board.TimeStop != null && board.TimeStop.IsHeldSquare(x, y));
            }, false);
        }
        return !board.HasPieceAt(toX, toY);
    }
}

/// <summary>
/// 破壊マス・落とし穴・壁のマスには持ち駒を打てないルール（Prototype の moves.js の getDropSquares）。
/// 炎上のマスには打てる（打った駒はすぐ消失する）。翼状態の駒（鳥）は破壊マス・落とし穴にも打てる（壁には打てない）
/// </summary>
public class CellStateDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        BoardCell cell = board.Cells.At(toX, toY);
        if (cell == null || PieceRules.IsBlockedForType(board.Cells, type, toX, toY)) return false;
        return !cell.HasTrap || PieceRules.IsWingType(type); // 落とし穴に打つと落ちるので除外（鳥は落ちない）
    }
}

/// <summary>
/// 歩・香車・桂馬・鳥が盤外に出て進めなくなる位置に打つことを禁止するルール（鳥は Prototype の dropMinDistance: 1）
/// </summary>
public class NoLegalMoveDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (type == PieceType.Pawn || type == PieceType.Lance || type == PieceType.Bird)
        {
            // 先手 (Player1, 前進方向は +X) -> X = 8 は一番奥なので打てない
            if (player == PlayerType.Player1 && toX == 8)
            {
                return false;
            }
            // 後手 (Player2, 前進方向は -X) -> X = 0 は一番奥なので打てない
            if (player == PlayerType.Player2 && toX == 0)
            {
                return false;
            }
        }
        else if (type == PieceType.Knight)
        {
            // 先手 -> X >= 7 (7, 8) は桂馬の行き所がないので打てない
            if (player == PlayerType.Player1 && toX >= 7)
            {
                return false;
            }
            // 後手 -> X <= 1 (0, 1) は桂馬の行き所がないので打てない
            if (player == PlayerType.Player2 && toX <= 1)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// 同じ縦列（筋）に自分の歩がすでにある場合、新たに歩を打つことを禁止するルール (二歩)
/// </summary>
public class NifuDropRule : IGameRule
{
    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true;
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (type == PieceType.Pawn)
        {
            for (int x = 0; x < 9; x++)
            {
                GamePiece piece = board.GetPieceAt(x, toY);
                // 成った歩（と金）は数えない（本将棋・Prototype の moves.js の getDropSquares と同じ。2026-11-15 まで数えていた）
                if (piece != null && piece.player == player && piece.type == PieceType.Pawn && !piece.isPromoted)
                {
                    return false;
                }
            }
        }
        return true;
    }
}

/// <summary>
/// 序盤は、敵陣（相手側の3段）に持ち駒を打てないルール。
/// 期間は戦闘型で違う（本将棋型：第 MatchConfig.EnemyZoneDropLockTurns ターンまで ／ 合戦将棋型：開始から 60 秒）ので、MatchPolicy.IsDropLocked を見る
/// </summary>
public class EnemyZoneDropLockRule : IGameRule
{
    private readonly MatchPolicy policy;

    public EnemyZoneDropLockRule(MatchPolicy policy)
    {
        this.policy = policy;
    }

    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        // 段は X（BoardZones の説明を参照）。巨人は占めるマスが 1 つでも敵陣なら打てない
        if (!(policy != null && policy.IsDropLocked)) return true;
        if (PieceRules.IsGiant(type)) return !BoardZones.IsEnemyZone(player, toX - GiantRules.Radius) && !BoardZones.IsEnemyZone(player, toX + GiantRules.Radius);
        return !BoardZones.IsEnemyZone(player, toX);
    }
}

/// <summary>
/// 打ち歩詰めの禁止（相手の王将が1枚だけのときに適用）
/// 王手放置や、王将を取られるマスへの移動は禁止しない（放置したら次の手で王将を取られて負け）。
/// 合戦将棋型は詰みがない（王将を取ったら勝ち）ので禁止しない（MatchPolicy.ForbidsUchifuzume）
/// </summary>
public class UchifuzumeRule : IGameRule
{
    private readonly MatchPolicy policy;

    public UchifuzumeRule(MatchPolicy policy = null)
    {
        this.policy = policy;
    }

    public bool IsLegalMove(GamePiece piece, int toX, int toY, GameBoard board)
    {
        return true; // 配置ルールなのでMove判定は常にパス
    }

    public bool IsLegalDrop(PieceType type, PlayerType player, int toX, int toY, GameBoard board)
    {
        if (policy != null && !policy.ForbidsUchifuzume) return true;
        return !(type == PieceType.Pawn && CheckRules.IsUchifuzume(board, player, toX, toY));
    }
}

/// <summary>
/// 王手・打ち歩詰めの判定。実際の駒は動かさず、盤面の配列をコピーして「動かした後」を調べる。
/// 王将が2枚以上ある間は、そのプレイヤーに王手・詰みは発生しない（1枚のときのみ適用）。
/// マスの状態も見る：走り駒の利きは破壊マス・壁・炎上・落とし穴で止まり（桂馬の利きは壁を飛び越す）、炎上・落とし穴に入った駒・打った駒は消失する。
/// 火薬のマスに入る手は爆発（3×3 の駒が消える。王将は残る）まで読む（鳥は爆発させずに火薬を取る）。火薬・狙撃手は動けないので利きはない。
/// 翼状態の駒（PieceRules.HasWing）は炎上・落とし穴で消失せず、走り駒の利きは破壊・炎上・落とし穴・火薬を通り抜ける。
/// 破壊マスの上の翼の駒には、翼のない駒は利かない（CanMove が PieceRules.IsBlockedFor を見る）。
/// 巨人（3×3 マス。GiantRules）は、前へ動いたときの先頭の 1 列に利く（王将がいれば王手）。HP が残る巨人のマスへ入る手は跳ね返る（取れない）。
/// 巨人を動かす手・打つ手（9 マスをふさぐ）も王手を防ぐ手として読む。
/// 盤面のスナップショットの同じ判定は Match/SnapshotRules.cs（こちらを変えたら、あちらも同じように変えること）
/// </summary>
public static class CheckRules
{
    const int NONE = -1;

    public static PlayerType Opponent(PlayerType player)
    {
        return (player == PlayerType.Player1) ? PlayerType.Player2 : PlayerType.Player1;
    }

    // 指定プレイヤーが王手されているか
    public static bool IsInCheck(GameBoard board, PlayerType player)
    {
        return IsInCheck(board.GetBoardData(), board.Cells, board.TimeStop != null, null, NONE, player);
    }

    // その移動の後に、自分の王将が取られる状態にならないか（詰みの判定で、王手を防げる手かを調べる）
    public static bool IsMoveSafe(GameBoard board, GamePiece piece, int toX, int toY)
    {
        GamePiece[] cells = board.GetBoardData();
        BoardCells cellStates = board.Cells;
        bool stopped = board.TimeStop != null;
        if (CountKings(cells, piece.player) != 1) return true;

        int from = GameHelper.CalcPanelNum(piece.X, piece.Y);
        int to = GameHelper.CalcPanelNum(toX, toY);
        if (from == NONE || to == NONE) return true;

        if (PieceRules.IsGiant(piece))
        {
            // 巨人：先頭の 1 列に相手の最後の王将がいれば、取って勝ち
            foreach (MatchSquare c in GiantRules.FrontRowAt(piece.player, new MatchSquare(toX, toY)))
            {
                GamePiece q = At(cells, c.x, c.y);
                if (q != null && q.player != piece.player && q.type == PieceType.King && CountKings(cells, q.player) == 1) return true;
            }
            bool moved;
            return !IsInCheck(SimulateGiantMove(cells, cellStates, piece, toX, toY, out moved), cellStates, stopped, null, NONE, piece.player);
        }

        // 相手の最後の王将を取る手は、その時点で勝ちなので指せる
        GamePiece target = cells[to];
        if (target != null && target.player != piece.player && target.type == PieceType.King && CountKings(cells, target.player) == 1)
        {
            return true;
        }

        // 最後の王将が炎上・落とし穴のマスに入ると消失して負けになる（翼状態の王将は消失しない）
        if (piece.type == PieceType.King && PieceRules.IsHazardFor(cellStates, piece, toX, toY) && !Bounces(piece, target)) return false;

        GamePiece[] sim = SimulateMove(cells, cellStates, piece, from, to);
        return !IsInCheck(sim, cellStates, stopped, null, NONE, piece.player);
    }

    // 狙撃した後に、自分の王将が取られる状態にならないか（詰みの判定で、王手をかけている駒を撃てるかを調べる）
    public static bool IsSnipeSafe(GameBoard board, GamePiece sniper, int targetX, int targetY)
    {
        GamePiece[] cells = board.GetBoardData();
        bool stopped = board.TimeStop != null;
        if (CountKings(cells, sniper.player) != 1) return true;

        int target = GameHelper.CalcPanelNum(targetX, targetY);
        if (target == NONE || cells[target] == null) return true;

        GamePiece[] sim = (GamePiece[])cells.Clone();
        if (PieceRules.IsExplosive(sim[target].type))
        {
            Explode(sim, targetX, targetY);
        }
        else if (GiantSurvives(sim[target], 1))
        {
            return !IsInCheck(cells, board.Cells, stopped, null, NONE, sniper.player); // 巨人の HP が残る
        }
        else
        {
            RemoveFrom(sim, sim[target]);
        }
        return !IsInCheck(sim, board.Cells, stopped, null, NONE, sniper.player);
    }

    // 持ち駒 type を打った後に、自分の王将が取られる状態にならないか（詰みの判定で、合駒で王手を防げるかを調べる）
    public static bool IsDropSafe(GameBoard board, PlayerType player, PieceType type, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        bool stopped = board.TimeStop != null;
        if (CountKings(cells, player) != 1) return true;

        // 炎上のマスに打った駒はすぐ消失するので、通り道をふさげない（鳥は消失しないので、ふさげる。巨人は通常のマスにしか打てない）
        if (!PieceRules.IsGiant(type) && PieceRules.IsHazardForType(board.Cells, type, x, y)) return !IsInCheck(cells, board.Cells, stopped, null, NONE, player);

        // 打った駒は利きの通り道をふさぐ「仮の駒」として扱う（巨人は 9 マス）
        return !IsInCheck(cells, board.Cells, stopped, DropBlockers(type, x, y), NONE, player);
    }

    // 打った駒がふさぐマス（巨人は 3×3）
    static int[] DropBlockers(PieceType type, int x, int y)
    {
        if (!PieceRules.IsGiant(type)) return new[] { GameHelper.CalcPanelNum(x, y) };
        List<MatchSquare> c = GiantRules.Cells(x, y);
        int[] res = new int[c.Count];
        for (int i = 0; i < c.Count; i++) res[i] = GameHelper.CalcPanelNum(c[i].x, c[i].y);
        return res;
    }

    // 歩を打って相手を詰ませる手か（打ち歩詰め）
    public static bool IsUchifuzume(GameBoard board, PlayerType player, int x, int y)
    {
        GamePiece[] cells = board.GetBoardData();
        BoardCells cellStates = board.Cells;
        PlayerType opponent = Opponent(player);
        if (CountKings(cells, opponent) != 1) return false;

        // 炎上のマスに打った歩はすぐ消失するので、王手にならない
        if (cellStates.IsHazard(x, y)) return false;

        // 打った歩のすぐ前に相手の王将がいなければ王手ではない
        int forwardX = (player == PlayerType.Player1) ? 1 : -1;
        int front = GameHelper.CalcPanelNum(x + forwardX, y);
        if (front == NONE) return false;
        GamePiece king = cells[front];
        if (king == null || king.player != opponent || king.type != PieceType.King) return false;

        int pawn = GameHelper.CalcPanelNum(x, y);
        return !CanEscapePawnCheck(cells, cellStates, board.TimeStop != null, pawn, front, opponent);
    }

    // 歩で王手された側が、駒を動かして王手を回避できるか
    // （歩の王手は王将に隣接しているので、持ち駒を打って防ぐことはできない）
    static bool CanEscapePawnCheck(GamePiece[] cells, BoardCells cellStates, bool stopped, int pawn, int pawnAttack, PlayerType defender)
    {
        int[] pawnBlocker = { pawn };
        for (int from = 0; from < cells.Length; from++)
        {
            GamePiece piece = cells[from];
            if (piece == null || piece.player != defender || piece.isUndead) continue; // アンデッドは持ち主が動かせない
            if (!IsAnchor(piece, from)) continue;                                       // 巨人は中央のマスで 1 回だけ

            // 狙撃手がいれば、打った歩を撃ち抜いて王手を外せる
            if (PieceRules.IsSniper(piece.type)) return true;

            if (PieceRules.IsGiant(piece))
            {
                // 巨人：前へ動く手（打った歩は先頭の 1 列で取れる）
                if (stopped) continue;
                foreach (MatchSquare m in GiantMoves(cells, cellStates, piece, pawnBlocker))
                {
                    bool moved;
                    GamePiece[] gsim = SimulateGiantMove(cells, cellStates, piece, m.x, m.y, out moved);
                    bool tookPawn = moved && GiantRules.FrontRowAt(piece.player, m).Exists(c => GameHelper.CalcPanelNum(c.x, c.y) == pawn);
                    if (!IsInCheck(gsim, cellStates, stopped, tookPawn ? null : pawnBlocker, tookPawn ? NONE : pawnAttack, defender)) return true;
                }
                continue;
            }

            for (int to = 0; to < cells.Length; to++)
            {
                if (to == from) continue;
                GamePiece target = cells[to];
                bool intoBomb = PieceRules.Detonates(piece, target); // 鳥は火薬を爆発させない（味方の火薬には入れない）
                if (target != null && target.player == defender && !intoBomb) continue;
                if (!CanAttack(cells, cellStates, pawnBlocker, from, to)) continue;

                // 王将が炎上・落とし穴のマスへ逃げても消失するだけなので、回避にならない
                int toX, toY;
                GameHelper.CalcPanelPosition(out toX, out toY, to);
                if (piece.type == PieceType.King && PieceRules.IsHazardFor(cellStates, piece, toX, toY) && !Bounces(piece, target)) continue;

                GamePiece[] sim = SimulateMove(cells, cellStates, piece, from, to);

                // 打った歩を取ったら（火薬の爆発に巻き込んだら）、仮の歩は盤上から消える
                int pawnX, pawnY;
                GameHelper.CalcPanelPosition(out pawnX, out pawnY, pawn);
                bool capturedPawn = (to == pawn) || (intoBomb && PieceRules.IsInExplosion(toX, toY, pawnX, pawnY));
                if (!IsInCheck(sim, cellStates, stopped, capturedPawn ? null : pawnBlocker, capturedPawn ? NONE : pawnAttack, defender))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // 盤の添字 i が駒 p の代表のマス（巨人は中央。ふつうの駒はいるマス）か
    static bool IsAnchor(GamePiece p, int i)
    {
        return p != null && GameHelper.CalcPanelNum(p.X, p.Y) == i;
    }

    // piece が target のマスへ入ろうとして跳ね返るか（HP が残る相手の巨人）
    static bool Bounces(GamePiece piece, GamePiece target)
    {
        return target != null && target.player != piece.player && GiantSurvives(target, 1);
    }

    // 巨人 g が hits 回の攻撃を受けても残るか（巨人でなければ false）
    static bool GiantSurvives(GamePiece g, int hits)
    {
        if (!PieceRules.IsGiant(g)) return false;
        int hp = g.hp > 0 ? g.hp : GiantRules.MaxHp;
        return hp - hits > 0;
    }

    static GamePiece At(GamePiece[] cells, int x, int y)
    {
        int i = GameHelper.CalcPanelNum(x, y);
        return i == NONE ? null : cells[i];
    }

    // 配列から駒 q を取り除く（巨人は占めるマス全部）
    static void RemoveFrom(GamePiece[] sim, GamePiece q)
    {
        if (q == null) return;
        for (int i = 0; i < sim.Length; i++)
        {
            if (sim[i] == q) sim[i] = null;
        }
    }

    // 盤面の配列をコピーして、from の駒を to へ動かした後の配列を作る（炎上・落とし穴のマスに入った駒は消失する。翼状態の駒は残る）。
    // 火薬のマスに入ったら爆発させる（範囲の駒と入った駒が消える。王将は残って、そのマスに入る）。鳥は爆発させずに火薬を取る。
    // HP が残る相手の巨人のマスなら跳ね返って動かない。最後の HP の巨人なら取って入る
    static GamePiece[] SimulateMove(GamePiece[] cells, BoardCells cellStates, GamePiece piece, int from, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        GamePiece[] sim = (GamePiece[])cells.Clone();
        GamePiece target = sim[to];
        if (Bounces(piece, target)) return sim;
        if (target != null && target != piece && PieceRules.IsGiant(target)) RemoveFrom(sim, target);
        sim[from] = null;
        bool landed = true;
        if (target != null && target != piece && PieceRules.Detonates(piece, target))
        {
            Explode(sim, toX, toY);
            landed = !PieceRules.IsDestroyedByExplosion(piece.type) && sim[to] == null;
        }
        if (landed) sim[to] = PieceRules.IsHazardFor(cellStates, piece, toX, toY) ? null : piece;
        return sim;
    }

    // 巨人 g が配列 cells の上で動ける先（GiantRules.Moves。blockers は仮の駒のマス：相手の駒として扱う）。時間停止中かは呼び出し側が見る。
    // 本編の移動（BaseMoveRule・GameSystem）もこれを使う
    public static List<MatchSquare> GiantMoves(GamePiece[] cells, BoardCells cellStates, GamePiece g, int[] blockers)
    {
        return GiantRules.Moves(g.player, g.X, g.Y,
            (x, y) =>
            {
                int i = GameHelper.CalcPanelNum(x, y);
                GamePiece q = cells[i];
                if (q == null) return IsBlocker(blockers, i) ? GiantRules.Occupant.Enemy : GiantRules.Occupant.Empty;
                if (q == g) return GiantRules.Occupant.Empty;
                if (PieceRules.IsExplosive(q.type)) return GiantRules.Occupant.Bomb;
                return q.player == g.player ? GiantRules.Occupant.Own : GiantRules.Occupant.Enemy;
            },
            (x, y) => PieceRules.IsBlockedFor(cellStates, g, x, y),
            (x, y) => cellStates != null && cellStates.IsHazard(x, y));
    }

    // 巨人 g を移動先 (toX, toY)（先頭の 1 列の中央）へ動かした後の配列（GameSystem.MoveGiant と同じ流れ）：
    // 先頭の 1 列の相手の駒を取り（盾・HP が残る巨人がいれば跳ね返って動かない）、火薬は爆発、
    // 新しく入ったマスが落とし穴・炎上なら 1 回の攻撃。moved：動けたか
    static GamePiece[] SimulateGiantMove(GamePiece[] cells, BoardCells cellStates, GamePiece g, int toX, int toY, out bool moved)
    {
        GamePiece[] sim = (GamePiece[])cells.Clone();
        moved = false;
        MatchSquare center = GiantRules.CenterAfter(g.player, new MatchSquare(toX, toY));
        List<MatchSquare> row = GiantRules.FrontRow(g.player, center.x, center.y);
        bool blocked = false;
        List<MatchSquare> bombs = new List<MatchSquare>();
        foreach (MatchSquare c in row)
        {
            GamePiece q = At(sim, c.x, c.y);
            if (q == null) continue;
            if (PieceRules.IsExplosive(q.type)) { bombs.Add(c); continue; }
            if (q.player == g.player) { blocked = true; continue; }
            if (q.hasShield || GiantSurvives(q, 1)) { blocked = true; continue; }
            RemoveFrom(sim, q);
        }
        if (blocked) return sim;
        int hits = 0;
        RemoveFrom(sim, g);
        foreach (MatchSquare c in bombs) sim[GameHelper.CalcPanelNum(c.x, c.y)] = null;
        foreach (MatchSquare c in GiantRules.Cells(center.x, center.y)) sim[GameHelper.CalcPanelNum(c.x, c.y)] = g;
        foreach (MatchSquare c in bombs)
        {
            hits++;
            ExplodeAround(sim, c.x, c.y, g);
        }
        bool hazard = false;
        foreach (MatchSquare c in row)
        {
            if (cellStates != null && cellStates.IsHazard(c.x, c.y)) hazard = true;
        }
        if (hazard) hits++;
        if (!GiantSurvives(g, hits) && hits > 0) RemoveFrom(sim, g);
        moved = true;
        return sim;
    }

    // 爆発（巨人 self の 1 回の攻撃は呼び出し側で数える。self 以外は Explode と同じ）
    static void ExplodeAround(GamePiece[] sim, int cx, int cy, GamePiece self)
    {
        int r = MatchConfig.ExplosionRadius;
        List<GamePiece> victims = new List<GamePiece>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                GamePiece q = At(sim, x, y);
                if (q != null && q != self && !victims.Contains(q)) victims.Add(q);
            }
        }
        foreach (GamePiece q in victims)
        {
            if (!PieceRules.IsDestroyedByExplosion(q.type) || GiantSurvives(q, 1)) continue;
            RemoveFrom(sim, q);
        }
    }

    // 盤面の配列の上で爆発させる（GameSystem.Explode と同じ範囲・同じ王将の扱い。誘爆は MatchConfig.ChainExplosion のときだけ。巨人は HP が残れば残る）
    static void Explode(GamePiece[] sim, int cx, int cy)
    {
        int r = MatchConfig.ExplosionRadius;
        List<Vector2Int> chain = new List<Vector2Int>();
        for (int x = cx - r; x <= cx + r; x++)
        {
            for (int y = cy - r; y <= cy + r; y++)
            {
                int i = GameHelper.CalcPanelNum(x, y);
                if (i == NONE || sim[i] == null || !PieceRules.IsDestroyedByExplosion(sim[i].type)) continue;
                if (PieceRules.IsGiant(sim[i]))
                {
                    if (!GiantSurvives(sim[i], 1)) RemoveFrom(sim, sim[i]);
                    continue;
                }
                if (MatchConfig.ChainExplosion && PieceRules.IsExplosive(sim[i].type) && (x != cx || y != cy)) chain.Add(new Vector2Int(x, y));
                sim[i] = null;
            }
        }
        foreach (Vector2Int c in chain) Explode(sim, c.x, c.y);
    }

    // blockers: 駒オブジェクトのない仮の駒（打つ駒）がいるマス
    // blockerAttack: その仮の駒が利きを持つマス（打ち歩詰めの判定で、歩が王将を狙うマス）
    static bool IsInCheck(GamePiece[] cells, BoardCells cellStates, bool stopped, int[] blockers, int blockerAttack, PlayerType player)
    {
        if (CountKings(cells, player) != 1) return false;

        int king = FindKing(cells, player);
        if (king == NONE) return false;
        if (king == blockerAttack) return true;

        PlayerType attacker = Opponent(player);
        for (int i = 0; i < cells.Length; i++)
        {
            GamePiece p = cells[i];
            if (p == null || p.player != attacker) continue;
            if (PieceRules.IsGiant(p))
            {
                if (!stopped && IsAnchor(p, i) && GiantAttacks(cells, cellStates, blockers, p, king)) return true;
                continue;
            }
            if (CanAttack(cells, cellStates, blockers, i, king)) return true;
        }
        return false;
    }

    // 巨人 g の先頭の 1 列が、動いたときに to のマスへ届くか（取れる）
    static bool GiantAttacks(GamePiece[] cells, BoardCells cellStates, int[] blockers, GamePiece g, int to)
    {
        int toX, toY;
        GameHelper.CalcPanelPosition(out toX, out toY, to);
        foreach (MatchSquare m in GiantMoves(cells, cellStates, g, blockers))
        {
            foreach (MatchSquare c in GiantRules.FrontRowAt(g.player, m))
            {
                if (c.x == toX && c.y == toY) return true;
            }
        }
        return false;
    }

    // cells[from] の駒が to のマスに利いているか（駒の動きと、途中に駒がないか）
    static bool CanAttack(GamePiece[] cells, BoardCells cellStates, int[] blockers, int from, int to)
    {
        GamePiece piece = cells[from];
        if (piece == null || from == to) return false;

        int fromX, fromY, toX, toY;
        GameHelper.CalcPanelPosition(out fromX, out fromY, from);
        GameHelper.CalcPanelPosition(out toX, out toY, to);

        // CanMove は駒自身の座標を使うので、一時的に配列上の位置に合わせる
        int savedX = piece.X;
        int savedY = piece.Y;
        piece.X = fromX;
        piece.Y = fromY;
        bool canMove = piece.CanMove(toX, toY);
        piece.X = savedX;
        piece.Y = savedY;
        if (!canMove) return false;

        // 障害物チェック（桂馬以外）
        if (piece.type != PieceType.Knight && IsPathBlocked(cells, cellStates, blockers, fromX, fromY, toX, toY))
        {
            return false;
        }
        return true;
    }

    static bool IsBlocker(int[] blockers, int index)
    {
        if (blockers == null) return false;
        foreach (int b in blockers)
        {
            if (b == index) return true;
        }
        return false;
    }

    static bool IsPathBlocked(GamePiece[] cells, BoardCells cellStates, int[] blockers, int fromX, int fromY, int toX, int toY)
    {
        if (GameHelper.IsPathBlocked(cells, cellStates, cells[GameHelper.CalcPanelNum(fromX, fromY)], fromX, fromY, toX, toY)) return true;
        if (blockers == null) return false;

        int dx = toX - fromX;
        int dy = toY - fromY;
        if (Mathf.Abs(dx) <= 1 && Mathf.Abs(dy) <= 1) return false;

        int stepX = System.Math.Sign(dx);
        int stepY = System.Math.Sign(dy);
        int x = fromX + stepX;
        int y = fromY + stepY;
        for (int i = 0; i < 9 && (x != toX || y != toY); i++)
        {
            if (IsBlocker(blockers, GameHelper.CalcPanelNum(x, y))) return true;
            x += stepX;
            y += stepY;
        }
        return false;
    }

    static int CountKings(GamePiece[] cells, PlayerType player)
    {
        int count = 0;
        foreach (GamePiece piece in cells)
        {
            if (piece != null && piece.player == player && piece.type == PieceType.King) count++;
        }
        return count;
    }

    static int FindKing(GamePiece[] cells, PlayerType player)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] != null && cells[i].player == player && cells[i].type == PieceType.King) return i;
        }
        return NONE;
    }
}
