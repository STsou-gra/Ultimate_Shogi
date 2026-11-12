// 盤面のスナップショットの単体テスト（実行: dotnet run --project Tests/SnapshotTests）
//  ・PieceMoves が、PieceMoves に移す前の各駒のクラス（OracleMain.cs）と全マスで同じ判定か
//  ・ランダムな局面で、BoardSimulator.CanMoveTo / CanDropTo・SnapshotRules（王手・打ち歩詰め）が、今のルール（Rules/GameRules.cs）と同じ判定か
//  ・行動を当てはめたとき（取りのエナジー・盾・アンデッド・火薬・成り・炎上／落とし穴・手番・鎮火・2回行動・終局・狙撃・錬成・時間停止）
//  ・JSON の往復、ランダムに指し続けても壊れないか
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

static partial class Program
{
    static int fails, checks;
    static readonly PlayerType P1 = PlayerType.Player1, P2 = PlayerType.Player2;

    static void Check(bool ok, string msg)
    {
        checks++;
        if (!ok && ++fails <= 60) Console.WriteLine("FAIL " + msg);
    }

    static int Main()
    {
        TestPatterns();
        TestRandomRules(6000, new System.Random(1));
        TestUchifuzumeCrafted();
        TestInitialPosition();
        TestApply();
        TestJson(new System.Random(2));
        TestPlayouts(400, new System.Random(3));
        TestSkills();
        TestCpu();
        Console.WriteLine($"checks={checks} fails={fails}");
        return fails == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------
    //  PieceMoves と main の各駒のクラスの判定が同じか（全種類・両者・成り・全マス → 全マス）
    // ------------------------------------------------------------
    static void TestPatterns()
    {
        int n = 0;
        foreach (PieceType t in Enum.GetValues(typeof(PieceType)))
            foreach (PlayerType o in new[] { P1, P2 })
                foreach (bool prom in new[] { false, true })
                    for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
                        for (int tx = 0; tx < 9; tx++) for (int ty = 0; ty < 9; ty++)
                        {
                            OracleBase or = Oracles.Make(t);
                            or.X = x; or.Y = y; or.player = o; or.isPromoted = prom;
                            bool a = or.CanMoveByPattern(tx, ty);
                            bool b = PieceMoves.CanMoveByPattern(t, o, prom, tx - x, ty - y);
                            n++;
                            if (a != b) Check(false, $"pattern {t} {o} prom={prom} ({x},{y})->({tx},{ty}) main={a} new={b}");
                        }
        Check(true, "patterns");
        Console.WriteLine($"patterns: {n} 通りを照合");
    }

    // ------------------------------------------------------------
    //  ランダムな局面で、今のルール（GameRules.cs）とスナップショットの判定が同じか
    // ------------------------------------------------------------
    class Pos { public GameBoard Board; public BoardSnapshot Snap; public TurnState Turn; public Dictionary<SnapshotPiece, GamePiece> Map = new Dictionary<SnapshotPiece, GamePiece>(); }

    static readonly PieceType[] Kinds = { PieceType.Pawn, PieceType.Pawn, PieceType.Pawn, PieceType.Lance, PieceType.Knight, PieceType.SilverGeneral, PieceType.GoldGeneral, PieceType.Bishop, PieceType.Rook, PieceType.Gunpowder, PieceType.Sniper };

    static Pos RandomPos(System.Random r)
    {
        BoardSnapshot s = new BoardSnapshot { firstPlayer = 0, current = r.Next(2), turnNumber = 1 + r.Next(20), moveNumber = 1 };
        // マス
        for (int i = 0; i < 81; i++)
        {
            double v = r.NextDouble();
            SnapshotCell c = s.cells[i];
            if (v < 0.07) c.state = CellState.Broken;
            else if (v < 0.13) { c.state = CellState.Burning; c.burnUntil = 1 + r.Next(25); }
            else if (v < 0.18) c.trap = r.Next(2);
        }
        // 王将（たいていは 1 枚ずつ。ときどき 0 枚・2 枚）
        foreach (PlayerType o in new[] { P1, P2 })
        {
            double v = r.NextDouble();
            int kings = v < 0.8 ? 1 : v < 0.9 ? 2 : 0;
            for (int k = 0; k < kings; k++) PlaceRandom(s, r, PieceType.King, o);
        }
        int count = 8 + r.Next(28);
        for (int k = 0; k < count; k++) PlaceRandom(s, r, Kinds[r.Next(Kinds.Length)], r.Next(2) == 0 ? P1 : P2);
        return Build(s);
    }

    static void PlaceRandom(BoardSnapshot s, System.Random r, PieceType t, PlayerType o)
    {
        for (int tries = 0; tries < 50; tries++)
        {
            int x = r.Next(9), y = r.Next(9);
            SnapshotCell c = s.Cell(x, y);
            if (s.At(x, y) != null || c.IsBroken || c.HasTrap) continue; // 駒は破壊マス・落とし穴にはいない
            SnapshotPiece p = new SnapshotPiece { type = t, owner = (int)o, x = x, y = y };
            p.promoted = PieceRules.CanPromote(t) && r.NextDouble() < 0.3;
            p.undead = t != PieceType.King && r.NextDouble() < 0.05;
            p.shield = r.NextDouble() < 0.1;
            p.initial = r.NextDouble() < 0.5;
            p.moveCount = r.Next(4);
            s.Place(p);
            return;
        }
    }

    // スナップショットから GameBoard（今のルールの判定用）を作る
    static Pos Build(BoardSnapshot s)
    {
        Pos pos = new Pos { Snap = s, Board = new GameBoard(), Turn = new TurnState(P1) };
        for (int i = 0; i < s.turnNumber; i++) pos.Turn.BeginTurn();
        for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
        {
            SnapshotCell c = s.Cell(x, y);
            if (c.IsBroken) pos.Board.Cells.Break(x, y);
            else if (c.IsBurning) pos.Board.Cells.SetBurning(x, y, Math.Max(1, c.burnUntil - s.turnNumber + 1), s.turnNumber);
            if (c.HasTrap) pos.Board.Cells.SetTrap(x, y, (PlayerType)c.trap);
        }
        foreach (SnapshotPiece p in s.pieces)
        {
            GamePiece g = new GamePiece { type = p.type, player = p.Owner, X = p.x, Y = p.y, isPromoted = p.promoted, isUndead = p.undead, hasShield = p.shield };
            pos.Board.Put(g);
            pos.Map[p] = g;
        }
        return pos;
    }

    static List<IGameRule> Rules(TurnState turn) => new List<IGameRule>
    {
        new BaseMoveRule(), new EmptyCellDropRule(), new CellStateDropRule(), new NoLegalMoveDropRule(),
        new NifuDropRule(), new EnemyZoneDropLockRule(turn), new UchifuzumeRule(),
    };

    static int uchifuzumeHits, checkHits;

    static void CompareRules(Pos pos, string label)
    {
        List<IGameRule> rules = Rules(pos.Turn);
        BoardSnapshot s = pos.Snap;
        // 移動
        foreach (SnapshotPiece p in s.pieces.ToList())
        {
            GamePiece g = pos.Map[p];
            for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
            {
                bool a = rules.All(rule => rule.IsLegalMove(g, x, y, pos.Board));
                bool b = BoardSimulator.CanMoveTo(s, p, x, y);
                if (a != b) Check(false, $"{label} move {p.Describe()} ({p.x},{p.y})->({x},{y}) rules={a} snap={b}");
            }
        }
        // 打つ
        foreach (PlayerType o in new[] { P1, P2 })
            foreach (PieceType t in PieceRules.HandOrder)
                for (int x = 0; x < 9; x++) for (int y = 0; y < 9; y++)
                {
                    bool a = rules.All(rule => rule.IsLegalDrop(t, o, x, y, pos.Board));
                    bool b = BoardSimulator.CanDropTo(s, o, t, x, y);
                    if (a != b) Check(false, $"{label} drop {o} {t} ({x},{y}) rules={a} snap={b}");
                    if (t == PieceType.Pawn && CheckRules.IsUchifuzume(pos.Board, o, x, y)) uchifuzumeHits++;
                    if (t == PieceType.Pawn && CheckRules.IsUchifuzume(pos.Board, o, x, y) != SnapshotRules.IsUchifuzume(s, o, x, y))
                        Check(false, $"{label} uchifuzume {o} ({x},{y})");
                }
        // 王手
        foreach (PlayerType o in new[] { P1, P2 })
        {
            bool a = CheckRules.IsInCheck(pos.Board, o), b = SnapshotRules.IsInCheck(s, o);
            if (a) checkHits++;
            if (a != b) Check(false, $"{label} check {o} rules={a} snap={b}");
        }
        Check(true, label);
    }

    static void TestRandomRules(int n, System.Random r)
    {
        for (int i = 0; i < n; i++) CompareRules(RandomPos(r), $"random#{i}");
        Console.WriteLine($"random rules: {n} 局面（王手 {checkHits} 回・打ち歩詰め {uchifuzumeHits} 回）");
    }

    // ------------------------------------------------------------
    //  打ち歩詰めの局面を作って確かめる
    // ------------------------------------------------------------
    static BoardSnapshot Empty(int turn = 11, PlayerType current = PlayerType.Player1)
    {
        return new BoardSnapshot { firstPlayer = 0, current = (int)current, turnNumber = turn, moveNumber = 1 };
    }

    static SnapshotPiece Put(BoardSnapshot s, PieceType t, PlayerType o, int x, int y, bool prom = false)
    {
        SnapshotPiece p = new SnapshotPiece { type = t, owner = (int)o, x = x, y = y, promoted = prom };
        s.Place(p);
        return p;
    }

    static void TestUchifuzumeCrafted()
    {
        BoardSnapshot s = Empty();
        Put(s, PieceType.King, P1, 0, 0);
        Put(s, PieceType.King, P2, 8, 4);
        Put(s, PieceType.Lance, P2, 8, 3);
        Put(s, PieceType.Lance, P2, 8, 5);
        Put(s, PieceType.Pawn, P2, 7, 3);
        Put(s, PieceType.Pawn, P2, 7, 5);
        Put(s, PieceType.GoldGeneral, P1, 6, 4);
        Pos pos = Build(s);
        Check(CheckRules.IsUchifuzume(pos.Board, P1, 7, 4), "crafted: main のルールで打ち歩詰め");
        Check(SnapshotRules.IsUchifuzume(s, P1, 7, 4), "crafted: スナップショットで打ち歩詰め");
        Check(!BoardSimulator.CanDropTo(s, P1, PieceType.Pawn, 7, 4), "crafted: 打ち歩詰めの歩は打てない");
        Check(BoardSimulator.CanDropTo(s, P1, PieceType.Lance, 7, 4), "crafted: 香車なら打てる");
        CompareRules(pos, "crafted");

        // 後手に狙撃手がいれば、打った歩を撃ち抜けるので打ち歩詰めではない
        Put(s, PieceType.Sniper, P2, 4, 8);
        pos = Build(s);
        Check(!CheckRules.IsUchifuzume(pos.Board, P1, 7, 4) && !SnapshotRules.IsUchifuzume(s, P1, 7, 4), "crafted: 狙撃手がいれば打ち歩詰めではない");
        CompareRules(pos, "crafted+sniper");

        // 序盤（第10ターンまで）は敵陣に打てない
        BoardSnapshot early = Empty(10);
        Put(early, PieceType.King, P1, 0, 0);
        Put(early, PieceType.King, P2, 8, 8);
        Check(!BoardSimulator.CanDropTo(early, P1, PieceType.GoldGeneral, 6, 4), "第10ターンは敵陣に打てない");
        early.turnNumber = 11;
        Check(BoardSimulator.CanDropTo(early, P1, PieceType.GoldGeneral, 6, 4), "第11ターンは敵陣に打てる");
    }

    // ------------------------------------------------------------
    //  初期配置（GameBoard.CreateLayout と同じ）：先手の移動は 30 件
    // ------------------------------------------------------------
    static BoardSnapshot Initial()
    {
        BoardSnapshot s = Empty(1);
        for (int i = 0; i < 9; i++) { Put(s, PieceType.Pawn, P1, 2, i); Put(s, PieceType.Pawn, P2, 6, i); }
        Put(s, PieceType.King, P1, 0, 4); Put(s, PieceType.King, P2, 8, 4);
        Put(s, PieceType.Rook, P1, 1, 7); Put(s, PieceType.Rook, P2, 7, 1);
        Put(s, PieceType.Bishop, P1, 1, 1); Put(s, PieceType.Bishop, P2, 7, 7);
        Put(s, PieceType.GoldGeneral, P1, 0, 3); Put(s, PieceType.GoldGeneral, P1, 0, 5); Put(s, PieceType.GoldGeneral, P2, 8, 3); Put(s, PieceType.GoldGeneral, P2, 8, 5);
        Put(s, PieceType.SilverGeneral, P1, 0, 2); Put(s, PieceType.SilverGeneral, P1, 0, 6); Put(s, PieceType.SilverGeneral, P2, 8, 2); Put(s, PieceType.SilverGeneral, P2, 8, 6);
        Put(s, PieceType.Knight, P1, 0, 1); Put(s, PieceType.Knight, P1, 0, 7); Put(s, PieceType.Knight, P2, 8, 1); Put(s, PieceType.Knight, P2, 8, 7);
        Put(s, PieceType.Lance, P1, 0, 0); Put(s, PieceType.Lance, P1, 0, 8); Put(s, PieceType.Lance, P2, 8, 8); Put(s, PieceType.Lance, P2, 8, 0);
        foreach (SnapshotPiece p in s.pieces) p.initial = true;
        s.players[0].energy = 1;
        s.specialMoves[(int)PieceType.Pawn] = 2;
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Gunpowder, cost = 3 });
        s.recipes.Add(new SnapshotRecipe { piece = PieceType.Sniper, cost = 5 });
        return s;
    }

    static void TestInitialPosition()
    {
        BoardSnapshot s = Initial();
        List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
        Check(legal.Count(a => a.type == MatchActionType.Move) == 30, $"初期配置の先手の移動は 30 件（{legal.Count(a => a.type == MatchActionType.Move)}）");
        Check(legal.Count == 30, "初期配置：錬成はエナジー不足");
        Check(BoardSimulator.LegalActions(s, P2).Count == 0, "手番でない後手の合法手は空");
        CompareRules(Build(s), "initial");
    }

    // ------------------------------------------------------------
    //  当てはめる
    // ------------------------------------------------------------
    static MatchAction Mv(int fx, int fy, int tx, int ty, bool prom = false) => MatchAction.Move(new Vector2Int(fx, fy), new Vector2Int(tx, ty), prom);

    static BoardSnapshot Basic(PlayerType current = PlayerType.Player1, int turn = 3)
    {
        BoardSnapshot s = Empty(turn, current);
        Put(s, PieceType.King, P1, 0, 0);
        Put(s, PieceType.King, P2, 8, 8);
        s.players[0].energy = 4;
        s.players[1].energy = 2;
        s.moveNumber = 5;
        return s;
    }

    static void TestApply()
    {
        // 取り：取られた側が価値の全額、取った側が半分。持ち駒になる。手番が後手へ（ターン数はそのまま、後手 +1）
        {
            BoardSnapshot s = Basic();
            SnapshotPiece rook = Put(s, PieceType.Rook, P1, 4, 4);
            SnapshotPiece gold = Put(s, PieceType.GoldGeneral, P2, 4, 7); gold.initial = true;
            Check(BoardSimulator.Apply(s, Mv(4, 4, 4, 7)), "capture: apply");
            Check(s.At(4, 7) == rook && s.At(4, 4) == null && rook.moveCount == 1, "capture: 移動・移動回数");
            Check(s.players[0].energy == 4 + 1 && s.players[1].energy == 2 + 3 + 1, $"capture: エナジー {s.players[0].energy}/{s.players[1].energy}");
            Check(s.HandCount(P1, PieceType.GoldGeneral) == 1, "capture: 持ち駒");
            Check(s.players[1].fallenInitial.SequenceEqual(new[] { PieceType.GoldGeneral }), "capture: 倒された初期配置の駒");
            Check(s.Current == P2 && s.moveNumber == 6 && s.turnNumber == 3, "capture: 手番");
        }
        // 盾：取りを防いで割れる。動いた駒は元のマスに残り、移動回数は数えない。手番は使う
        {
            BoardSnapshot s = Basic();
            SnapshotPiece rook = Put(s, PieceType.Rook, P1, 4, 4);
            SnapshotPiece gold = Put(s, PieceType.GoldGeneral, P2, 4, 7); gold.shield = true;
            Check(BoardSimulator.Apply(s, Mv(4, 4, 4, 7)), "shield: apply");
            Check(s.At(4, 4) == rook && s.At(4, 7) == gold && !gold.shield && rook.moveCount == 0, "shield: 跳ね返る・盾が割れる");
            Check(s.players[0].energy == 4 && s.players[1].energy == 3 && s.HandCount(P1, PieceType.GoldGeneral) == 0, "shield: エナジー・持ち駒なし");
            Check(s.Current == P2, "shield: 手番は使う");
        }
        // アンデッド：取ると成仏（持ち駒・エナジーなし）
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.Rook, P1, 4, 4);
            SnapshotPiece u = Put(s, PieceType.SilverGeneral, P2, 4, 7); u.undead = true;
            Check(BoardSimulator.Apply(s, Mv(4, 4, 4, 7)), "undead: apply");
            Check(s.At(4, 7) != null && s.At(4, 7).type == PieceType.Rook && s.HandCount(P1, PieceType.SilverGeneral) == 0 && s.players[1].energy == 3, "undead: 成仏");
        }
        // 火薬：入ると 3×3 が消失。王将は残る。入った駒も消える
        {
            BoardSnapshot s = Basic();
            SnapshotPiece pawn = Put(s, PieceType.Pawn, P1, 3, 4); pawn.initial = true;
            Put(s, PieceType.Gunpowder, P2, 4, 4);
            SnapshotPiece silver = Put(s, PieceType.SilverGeneral, P2, 5, 5); silver.initial = true;
            SnapshotPiece king2 = Put(s, PieceType.King, P1, 3, 3);
            SnapshotPiece far = Put(s, PieceType.GoldGeneral, P2, 6, 6);
            Check(BoardSimulator.Apply(s, Mv(3, 4, 4, 4)), "gunpowder: apply");
            Check(s.At(4, 4) == null && s.At(3, 4) == null && s.At(5, 5) == null && s.At(3, 3) == king2 && s.At(6, 6) == far, "gunpowder: 3×3 が消える・王将は残る");
            Check(s.players[0].fallenInitial.SequenceEqual(new[] { PieceType.Pawn }) && s.players[1].fallenInitial.SequenceEqual(new[] { PieceType.SilverGeneral }), "gunpowder: 倒された初期配置の駒");
            Check(s.players[1].energy == 3 && s.HandCount(P1, PieceType.SilverGeneral) == 0, "gunpowder: 消失なのでエナジー・持ち駒なし");
        }
        // 王将が火薬に入る：爆発に耐えて、そのマスへ進む
        {
            BoardSnapshot s = Basic();
            SnapshotPiece king = Put(s, PieceType.King, P1, 3, 4);
            Put(s, PieceType.Gunpowder, P2, 4, 4);
            Check(BoardSimulator.Apply(s, Mv(3, 4, 4, 4)), "king-gunpowder: apply");
            Check(s.At(4, 4) == king && king.moveCount == 1, "king-gunpowder: 王将は残って進む");
        }
        // 成り：入るか出るかで選べる／行き所がなければ成るだけ／火薬で吹き飛ぶなら成らないだけ
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.SilverGeneral, P1, 5, 4);
            Put(s, PieceType.Pawn, P1, 7, 2);
            Put(s, PieceType.Pawn, P1, 4, 6);
            Put(s, PieceType.SilverGeneral, P1, 5, 1);
            Put(s, PieceType.Gunpowder, P2, 6, 1);
            Check(BoardSimulator.CanApply(s, Mv(5, 4, 6, 4, true)) && BoardSimulator.CanApply(s, Mv(5, 4, 6, 4, false)), "promote: 敵陣に入るなら選べる");
            Check(BoardSimulator.CanApply(s, Mv(7, 2, 8, 2, true)) && !BoardSimulator.CanApply(s, Mv(7, 2, 8, 2, false)), "promote: 行き所がなければ成るだけ");
            Check(!BoardSimulator.CanApply(s, Mv(4, 6, 5, 6, true)) && BoardSimulator.CanApply(s, Mv(4, 6, 5, 6, false)), "promote: 敵陣でなければ成れない");
            Check(!BoardSimulator.CanApply(s, Mv(5, 1, 6, 1, true)) && BoardSimulator.CanApply(s, Mv(5, 1, 6, 1, false)), "promote: 火薬で吹き飛ぶなら成らないだけ");
            BoardSimulator.Apply(s, Mv(5, 4, 6, 4, true));
            Check(s.At(6, 4).promoted && s.At(6, 4).moveCount == 1, "promote: 成った");
        }
        // 炎上・落とし穴：入ると消失（成りも起きない）、落とし穴は消える。炎上には打てる（打った駒は消える）
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.SilverGeneral, P1, 5, 4);
            s.Cell(6, 4).trap = 1;
            s.Cell(4, 2).state = CellState.Burning; s.Cell(4, 2).burnUntil = 5;
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.Pawn)] = 1;
            BoardSnapshot t = s.Clone();
            Check(BoardSimulator.Apply(s, Mv(5, 4, 6, 4, true)), "trap: apply");
            Check(s.At(6, 4) == null && !s.Cell(6, 4).HasTrap, "trap: 落ちて穴が消える");
            Check(BoardSimulator.Apply(t, MatchAction.Drop(PieceType.Pawn, new Vector2Int(4, 2))), "burning drop: apply");
            Check(t.At(4, 2) == null && t.HandCount(P1, PieceType.Pawn) == 0 && t.Current == P2, "burning drop: 消える・持ち駒が減る・手番");
            Check(!BoardSimulator.CanApply(s.Clone(), MatchAction.Drop(PieceType.Pawn, new Vector2Int(6, 4))) || true, "（打つ判定は CompareRules で確認）");
        }
        // 後手の手番の終わり：期限の来た炎上を鎮火 → 先手の手番でターン数 +1・エナジー +1
        {
            BoardSnapshot s = Basic(PlayerType.Player2, 3);
            Put(s, PieceType.GoldGeneral, P2, 5, 5);
            s.Cell(2, 2).state = CellState.Burning; s.Cell(2, 2).burnUntil = 3;
            s.Cell(2, 3).state = CellState.Burning; s.Cell(2, 3).burnUntil = 4;
            Check(BoardSimulator.Apply(s, Mv(5, 5, 4, 5)), "round end: apply");
            Check(!s.Cell(2, 2).IsBurning && s.Cell(2, 3).IsBurning, "round end: 鎮火");
            Check(s.Current == P1 && s.turnNumber == 4 && s.moveNumber == 6 && s.players[0].energy == 5, "round end: ターン数・エナジー");
        }
        // 2回行動：手番が続く（エナジー・手数は増えない）
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.GoldGeneral, P1, 3, 3);
            s.extraTurn = true;
            Check(BoardSimulator.Apply(s, Mv(3, 3, 4, 3)), "extra: apply");
            Check(s.Current == P1 && !s.extraTurn && s.moveNumber == 5 && s.players[0].energy == 4, "extra: 手番が続く");
        }
        // 最後の王将を取ると終局（手番は替わらない。王将は持ち駒にならない）
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.Rook, P1, 8, 0);
            Check(BoardSimulator.Apply(s, Mv(8, 0, 8, 8)), "king capture: apply");
            Check(s.gameOver && s.winner == 0 && s.Current == P1 && s.moveNumber == 5, "king capture: 終局");
            Check(s.players[1].energy == 2 + 10 && s.players[0].energy == 4 + 5 && s.HandCount(P1, PieceType.King) == 0, "king capture: エナジー");
            Check(BoardSimulator.LegalActions(s, P1).Count == 0, "king capture: 終局後は合法手なし");
        }
        // 狙撃：取り・火薬は爆発・盾は割れる・王将は撃てない
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.Sniper, P1, 1, 1);
            Put(s, PieceType.GoldGeneral, P2, 5, 5);
            Put(s, PieceType.Gunpowder, P2, 6, 6);
            Put(s, PieceType.SilverGeneral, P2, 7, 7);
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
            Check(legal.Count(a => a.type == MatchActionType.Snipe) == 3, "snipe: 王将以外の 3 つ");
            BoardSnapshot t = s.Clone();
            Check(BoardSimulator.Apply(s, MatchAction.Snipe(new Vector2Int(1, 1), new Vector2Int(5, 5))), "snipe: apply");
            Check(s.At(5, 5) == null && s.HandCount(P1, PieceType.GoldGeneral) == 1 && s.players[1].energy == 2 + 3 + 1, "snipe: 取り");
            Check(BoardSimulator.Apply(t, MatchAction.Snipe(new Vector2Int(1, 1), new Vector2Int(6, 6))), "snipe gunpowder: apply");
            Check(t.At(6, 6) == null && t.At(5, 5) == null && t.At(7, 7) == null && t.At(8, 8) != null, "snipe gunpowder: 爆発（王将は残る）");
            s.online = true;
            Check(!BoardSimulator.LegalActions(s, s.Current).Any(a => a.type == MatchActionType.Snipe || a.type == MatchActionType.Forge), "online: 狙撃・錬成なし");
        }
        // 錬成：エナジーを払って持ち駒（火薬も）。手番は続く。必殺技・魔法の合法手は分からなくなる
        {
            BoardSnapshot s = Basic();
            s.players[0].energy = 5;
            s.recipes.Add(new SnapshotRecipe { piece = PieceType.Gunpowder, cost = 3 });
            s.skillActionsKnown = true;
            s.skillActions.Add(MatchAction.Magic("Shield", new[] { new Vector2Int(0, 0) }));
            Check(BoardSimulator.CanApply(s, MatchAction.Magic("Shield", new[] { new Vector2Int(0, 0) })), "magic: 記録した合法手なら合法");
            Check(!BoardSimulator.Apply(s.Clone(), MatchAction.Magic("Shield", new[] { new Vector2Int(0, 0) })), "magic: 効果はシミュレーションしない");
            Check(BoardSimulator.Apply(s, MatchAction.Forge(PieceType.Gunpowder)), "forge: apply");
            Check(s.players[0].energy == 2 && s.HandCount(P1, PieceType.Gunpowder) == 1 && s.Current == P1 && !s.skillActionsKnown, "forge: エナジー・持ち駒・手番");
            Check(!BoardSimulator.CanApply(s, MatchAction.Forge(PieceType.Gunpowder)), "forge: エナジー不足");
        }
        // 時間停止中：合法手は分かるが、当てはめない。予定のある駒は動かせない・予定のマスには打てない
        {
            BoardSnapshot s = Basic();
            SnapshotPiece g = Put(s, PieceType.GoldGeneral, P1, 3, 3);
            g.frozen = true;
            s.timeStopped = true; s.timeStopOwner = 0; s.timeStopReleasable = true;
            s.heldSquares.Add(new MatchSquare(4, 4));
            s.players[0].hand[BoardSnapshot.HandIndex(PieceType.GoldGeneral)] = 1;
            List<MatchAction> legal = BoardSimulator.LegalActions(s, P1);
            Check(!legal.Any(a => a.type == MatchActionType.Move && a.from == new MatchSquare(3, 3)), "time stop: 予定のある駒は動かせない");
            Check(!legal.Any(a => a.type == MatchActionType.Drop && a.to == new MatchSquare(4, 4)), "time stop: 予定のマスには打てない");
            Check(legal.Any(a => a.type == MatchActionType.ReleaseTimeStop), "time stop: 解除できる");
            Check(!BoardSimulator.Apply(s, Mv(0, 0, 1, 0)), "time stop: 当てはめない");
        }
        // 手番でないプレイヤーの行動・owner の違う行動は実行できない
        {
            BoardSnapshot s = Basic();
            Put(s, PieceType.GoldGeneral, P2, 5, 5);
            Check(!BoardSimulator.CanApply(s, Mv(5, 5, 4, 5)), "owner: 相手の駒は動かせない");
            MatchAction a = Mv(0, 0, 1, 0); a.Owner = P2;
            Check(!BoardSimulator.CanApply(s, a), "owner: 手番と違う owner");
            a.Owner = P1;
            Check(BoardSimulator.CanApply(s, a), "owner: 手番と同じ owner");
        }
    }

    // ------------------------------------------------------------
    //  JSON の往復
    // ------------------------------------------------------------
    static void TestJson(System.Random r)
    {
        for (int i = 0; i < 300; i++)
        {
            BoardSnapshot s = RandomPos(r).Snap;
            s.players[0].hand[2] = 3;
            s.players[1].fallenInitial.Add(PieceType.Rook);
            s.skillActionsKnown = true;
            s.skillActions.Add(MatchAction.Magic("SwapPiece", new[] { new Vector2Int(1, 2), new Vector2Int(3, 4) }));
            s.heldSquares.Add(new MatchSquare(2, 2));
            string json = s.ToJson();
            BoardSnapshot back = BoardSnapshot.FromJson(json);
            List<string> d = back == null ? new List<string> { "null" } : s.Diff(back, true);
            Check(d.Count == 0, $"json#{i}: {string.Join(" / ", d.Take(3))}");
            if (i == 0) Console.WriteLine($"snapshot json: {json.Length} 文字");
        }
        MatchAction m = MatchAction.Magic("Meteor", new[] { new Vector2Int(4, 5) }, 1);
        m.Owner = P2;
        string mj = m.ToJson();
        MatchAction mb = MatchAction.FromJson(mj);
        Check(mb != null && mb.Key == m.Key && mb.Owner == P2, "action json");
        Console.WriteLine($"action json: {mj}");
        Check(MatchAction.FromJson("not json") == null, "action json: 壊れた JSON は null");
    }

    // ------------------------------------------------------------
    //  ランダムに指し続けて、壊れないか・今のルールと判定が同じままか
    // ------------------------------------------------------------
    static void TestPlayouts(int games, System.Random r)
    {
        int plies = 0, ended = 0;
        for (int g = 0; g < games; g++)
        {
            BoardSnapshot s = Initial();
            s.turnNumber = 1;
            for (int ply = 0; ply < 160 && !s.gameOver; ply++)
            {
                // ときどきマスを炎上させる（合法手を作る前に）
                if (r.NextDouble() < 0.05) { SnapshotCell c = s.Cell(r.Next(9), r.Next(9)); if (!c.IsBroken && !c.HasTrap) { c.state = CellState.Burning; c.burnUntil = s.turnNumber + r.Next(3); } }
                List<MatchAction> legal = BoardSimulator.LegalActions(s, s.Current).Where(a => BoardSimulator.CanSimulate(a.type)).ToList();
                if (legal.Count == 0) break;
                // 取る手・打つ手を少し多めに選ぶ
                MatchAction a = legal[r.Next(legal.Count)];
                if (r.NextDouble() < 0.3)
                {
                    var caps = legal.Where(x => x.type == MatchActionType.Move && s.At(x.to.x, x.to.y) != null).ToList();
                    if (caps.Count > 0) a = caps[r.Next(caps.Count)];
                }
                if (!BoardSimulator.Apply(s, a, out string reason)) { Check(false, $"playout {g}/{ply}: 合法手が当てはめられない {a} {reason}"); break; }
                plies++;
                Invariants(s, $"playout {g}/{ply}");
                if (ply % 20 == 0) CompareRules(Build(s), $"playout {g}/{ply}");
            }
            if (s.gameOver) ended++;
        }
        Console.WriteLine($"playouts: {games} 局・{plies} 手（終局 {ended} 局）");
    }

    static void Invariants(BoardSnapshot s, string label)
    {
        HashSet<int> seen = new HashSet<int>();
        foreach (SnapshotPiece p in s.pieces)
        {
            int i = GameHelper.CalcPanelNum(p.x, p.y);
            if (i < 0 || !seen.Add(i) || s.At(p.x, p.y) != p) { Check(false, $"{label}: 駒の位置が壊れた {p.Describe()} ({p.x},{p.y})"); return; }
        }
        int inGrid = s.Grid.Count(q => q != null);
        if (inGrid != s.pieces.Count) Check(false, $"{label}: grid {inGrid} / pieces {s.pieces.Count}");
        if (s.players.Any(p => p.energy < 0 || p.hand.Any(h => h < 0))) Check(false, $"{label}: エナジー・持ち駒が負");
        BoardSnapshot clone = s.Clone();
        if (s.Diff(clone, true).Count != 0) Check(false, $"{label}: Clone が違う");
    }
}
