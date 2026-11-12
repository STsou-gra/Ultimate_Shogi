using System.Collections.Generic;

// CPU の評価関数（Prototype の src/cpu/evaluate.js）：局面が me にとってどれだけ良いかを数値にする（大きいほど良い。単位はおおよそ「歩 1 枚」）。
// 盤面のスナップショット（BoardSnapshot）だけを見る（GameObject・UnityEngine の API に依存しないので、別スレッドでも動く）。
//
// Prototype との違い：
//  ・鳥・巨人・翼は Unity にないので入れていない
//  ・王の安全（相手の駒が王に届くか）は、王将が 1 枚のときだけ見る（Unity は王将が 2 枚以上なら王手・詰みがない。R-1）
//  ・超常魔法の「解放済み・未使用」は、超常魔法を編成しているときだけ数える
public static class CpuEvaluate
{
    public const double Win = 100000;

    // 盤上の駒の価値（[成っていない, 成った]）。王将は勝敗で扱うので 0
    static double BoardValue(PieceType type, bool promoted)
    {
        switch (type)
        {
            case PieceType.Pawn: return promoted ? 4 : 1;
            case PieceType.Lance: return promoted ? 4 : 2.5;
            case PieceType.Knight: return promoted ? 4 : 2.5;
            case PieceType.SilverGeneral: return promoted ? 4 : 3.5;
            case PieceType.GoldGeneral: return 4;
            case PieceType.Bishop: return promoted ? 8 : 6;
            case PieceType.Rook: return promoted ? 9 : 7;
            case PieceType.Sniper: return 3;
            case PieceType.Gunpowder: return 1.5;
            case PieceType.King: return 0;
            default: return 2;
        }
    }

    // 持ち駒の価値（盤上より少し高い：どこにでも打てるので）
    static double HandValue(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return 1.1;
            case PieceType.Lance: return 2.6;
            case PieceType.Knight: return 2.6;
            case PieceType.SilverGeneral: return 3.6;
            case PieceType.GoldGeneral: return 4.2;
            case PieceType.Bishop: return 6.2;
            case PieceType.Rook: return 7.2;
            case PieceType.Sniper: return 3;
            case PieceType.Gunpowder: return 1.5;
            default: return 2;
        }
    }

    // 盤上の駒 p の価値（アンデッドは自分では動かせないので 0.7 倍）
    public static double PieceValue(SnapshotPiece p)
    {
        double v = BoardValue(p.type, p.promoted);
        return p.undead ? v * 0.7 : v;
    }

    const double WEnergy = 0.2;        // エナジー 1 あたり
    const double WShield = 0.3;        // 盾の駒 1 つ（取られそうな駒を守る効果は hanging で別に入る）
    const double WPhalanx = 1.5;       // ファランクスの駒 1 つ
    const double WSpecial = 0.6;       // 必殺技が使える駒 1 つ
    const double WTranscendent = 4;    // 超常魔法が解放されていて、まだ使っていない
    const double WHanging = 0.7;       // 次に相手が取れる自分の駒（いちばん高いもの）
    const double WThreat = 0.3;        // 次に自分が取れる相手の駒（いちばん高いもの）
    const double WMobility = 0.03;     // 動ける手の数の差
    const double WKingDanger = 900;    // 相手の手番で、相手が王を取れる
    const double WCheck = 0.5;         // 王手をかけている

    // 局面 s の、me から見た評価値。勝敗がついていれば ±Win（引き分けは 0）。
    // 手番（s.current）によって、取られそうな駒・取れそうな駒の重みを変える（次に動くのがどちらか）
    public static double Evaluate(BoardSnapshot s, PlayerType me)
    {
        PlayerType opp = BoardZones.Opponent(me);
        if (s.gameOver)
        {
            if (s.winner == BoardSnapshot.Draw || s.winner < 0) return 0;
            return s.winner == (int)me ? Win : -Win;
        }
        bool meKing = s.KingCount(me) > 0, oppKing = s.KingCount(opp) > 0;
        if (!meKing || !oppKing) return meKing == oppKing ? 0 : meKing ? Win : -Win;

        double score = 0;
        // 持ち主ごとに、その持ち主の駒が届くマス（添字は GameHelper.CalcPanelNum）
        bool[][] attacks = { new bool[81], new bool[81] };
        int[] mobility = new int[2];
        SnapshotPiece[] grid = s.Grid;
        foreach (SnapshotPiece p in grid)
        {
            if (p == null) continue;
            double sign = p.Owner == me ? 1 : -1;
            score += sign * PieceValue(p);
            if (p.shield) score += sign * WShield;
            if (p.phalanx) score += sign * WPhalanx;
            if (IsSpecialReady(s, p)) score += sign * WSpecial;
            bool[] map = attacks[p.owner];
            int n = 0;
            for (int x = 0; x < BoardCells.Size; x++)
            {
                for (int y = 0; y < BoardCells.Size; y++)
                {
                    if (!BoardSimulator.CanMoveTo(s, p, x, y)) continue;
                    n++;
                    map[GameHelper.CalcPanelNum(x, y)] = true;
                }
            }
            mobility[p.owner] += n;
        }
        foreach (PlayerType o in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            double sign = o == me ? 1 : -1;
            SnapshotPlayer sp = s.Player(o);
            for (int h = 0; h < PieceRules.HandOrder.Length; h++)
            {
                if (sp.hand[h] > 0) score += sign * sp.hand[h] * HandValue(PieceRules.HandOrder[h]);
            }
            score += sign * sp.energy * WEnergy;
            if (sp.transcendentUnlocked && !sp.transcendentUsed && HasTranscendent(sp)) score += sign * WTranscendent;
        }
        score += (mobility[(int)me] - mobility[(int)opp]) * WMobility;

        // 次に動く側が取れる駒：動く側には得、動かれる側には損（いちばん高い 1 つだけ数える。盾の駒は取られない）
        PlayerType toMove = s.Current, waiting = BoardZones.Opponent(toMove);
        double best = 0;
        foreach (SnapshotPiece p in grid)
        {
            if (p == null || p.Owner != waiting || p.shield) continue;
            if (!attacks[(int)toMove][GameHelper.CalcPanelNum(p.x, p.y)]) continue;
            best = System.Math.Max(best, PieceValue(p));
        }
        score += (toMove == me ? WThreat : -WHanging) * best;

        // 王の安全：相手の手番で相手の駒が王に届くなら、ほぼ負け
        if (toMove == opp && IsKingThreatened(s, me, attacks[(int)opp])) score -= WKingDanger;
        else if (toMove == me && IsKingThreatened(s, opp, attacks[(int)me])) score += WKingDanger / 2; // こちらの手番で相手の王を取れる
        else if (IsKingThreatened(s, opp, attacks[(int)me])) score += WCheck;
        return score;
    }

    static bool IsSpecialReady(BoardSnapshot s, SnapshotPiece p)
    {
        int i = (int)p.type;
        bool has = s.specialKinds != null && i < s.specialKinds.Length ? s.specialKinds[i] != SpecialKind.None : s.SpecialMovesOf(p.type) > 0;
        return has && s.IsSpecialReady(p);
    }

    static bool HasTranscendent(SnapshotPlayer sp)
    {
        if (sp.skills == null || sp.skills.Count == 0) return true; // 編成が分からないスナップショット（テストなど）は Prototype と同じ
        foreach (SnapshotSkill k in sp.skills)
        {
            if (k != null && k.transcendent) return true;
        }
        return false;
    }

    // owner の王将（1 枚だけのとき）が、attacks のマスにいるか（Prototype の isKingThreatened）
    static bool IsKingThreatened(BoardSnapshot s, PlayerType owner, bool[] attacks)
    {
        SnapshotPiece king = null;
        foreach (SnapshotPiece p in s.Grid)
        {
            if (p == null || p.Owner != owner || p.type != PieceType.King) continue;
            if (king != null) return false; // 王将が 2 枚以上：1 枚取られても負けではない
            king = p;
        }
        return king != null && attacks[GameHelper.CalcPanelNum(king.x, king.y)];
    }
}
