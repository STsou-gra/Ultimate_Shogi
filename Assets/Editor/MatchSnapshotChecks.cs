using System.Collections.Generic;
using UnityEngine;

// 局面の書き出し・読み込み（MatchSnapshot / MatchSnapshotIO）と盤面のハッシュ（MatchHash）の確認。
//
// ゲームのスクリプトはアセンブリ定義（asmdef）を持たない（Assembly-CSharp）ので、テスト用の asmdef からは直接参照できない。
// そこで確認の中身はこの Editor フォルダ（Assembly-CSharp-Editor。Assembly-CSharp を参照でき、ビルドには入らない）に書き、
// EditMode テスト（Assets/Tests/EditMode/MatchSnapshotTests.cs）がリフレクションで Check〜 のメソッドを 1 つずつ呼ぶ。
// 失敗したら CheckFailedException を投げる。メニュー「UltimateShogi/局面の書き出しを確かめる」からも全部を実行できる。
//
// Check〜 を足すと、テストにも自動で並ぶ（public static・引数なし・名前が Check で始まるメソッド）
public static class MatchSnapshotChecks
{
    public class CheckFailedException : System.Exception
    {
        public CheckFailedException(string message) : base(message) { }
    }

    // ------------------------------------------------------------
    //  確認
    // ------------------------------------------------------------

    // 書き出し → JSON → 読み込み → 書き出し で同じ JSON になる（駒・時間停止の予定・マス・持ち駒・編成・乱数・超常魔法を含む局面）
    public static void CheckExportImportExportGivesSameJson()
    {
        using (Fixture f = new Fixture())
        {
            MatchSnapshot first = f.BuildRichSnapshot();
            string json1 = first.ToJson();
            string pretty1 = first.ToJson(true);

            MatchSnapshot read = MatchSnapshot.FromJson(json1);
            True(read != null, "書き出した JSON を読めない");
            Equal(null, MatchSnapshotIO.Validate(read), "書き出した局面が Validate を通らない");

            using (Fixture g = new Fixture())
            {
                MatchSnapshot second = g.Load(read);
                Equal(json1, second.ToJson(), "書き出し → 読み込み → 書き出しで JSON が変わった");
                Equal(pretty1, second.ToJson(true), "書き出し → 読み込み → 書き出しで JSON（整形あり）が変わった");
                Equal(MatchHash.Compute(first), MatchHash.Compute(second), "読み込み直すとハッシュが変わった");
            }
        }
    }

    // 読み込んだ駒・時間停止の状態が、書き出した局面と同じ中身になっている（id の付け替えで参照がずれない）
    public static void CheckImportRebuildsPiecesAndTimeStop()
    {
        using (Fixture f = new Fixture())
        {
            MatchSnapshot snapshot = f.BuildRichSnapshot();
            using (Fixture g = new Fixture())
            {
                g.Load(MatchSnapshot.FromJson(snapshot.ToJson()));

                GamePiece rook = g.board[GameHelper.CalcPanelNum(4, 4)];
                True(rook != null && rook.type == PieceType.Rook && rook.player == PlayerType.Player1, "(4, 4) の先手の飛車がない");
                True(rook.isPromoted && rook.hasShield && rook.hasPhalanx && rook.moveCount == 3, "飛車の成り・盾・ファランクス・移動回数が戻らない");
                GamePiece undead = g.board[GameHelper.CalcPanelNum(5, 2)];
                True(undead != null && undead.isUndead && !undead.IsInitial, "アンデッドが戻らない");

                TimeStopState ts = g.timeStop;
                True(ts != null, "時間停止の状態が戻らない");
                Equal(PlayerType.Player1, ts.Owner, "時間停止の持ち主");
                Equal(2, ts.Remaining, "時間停止の残り回数");
                Equal(1, ts.Held.Count, "予定の駒の数");
                GamePiece held = null;
                foreach (GamePiece q in ts.Held) held = q;
                True(held.type == PieceType.SilverGeneral && held.X == 4 && held.Y == 4, "予定の駒（取られる予定の銀）の位置・種類");
                True(g.board[GameHelper.CalcPanelNum(4, 4)] != held, "予定の駒が盤のデータに入っている");
                Equal(2, ts.Pending.Count, "予定の数");
                TimeStopPending capture = ts.Pending[0];
                True(capture.Target == held && capture.Piece == rook, "取りの予定の駒の参照");
                True(capture.Move != null && capture.Move == ts.Moves[0] && capture.Move.Piece == rook, "取りの予定の移動の参照");
                Equal(TimeStopKind.Cell, ts.Pending[1].Kind, "2 つ目の予定");
                True(MatchSnapshotIO.IsGrayHeld(ts, held), "取られる予定の駒は灰色で残す");
            }
        }
    }

    // 乱数は読み込んだあとも、書き出したときの続きから同じ値の列になる
    public static void CheckRngContinuesAfterImport()
    {
        using (Fixture f = new Fixture())
        {
            MatchSnapshot snapshot = f.BuildRichSnapshot();
            double[] expected = { f.match.Rng.Next(), f.match.Rng.Next(), f.match.Rng.Next() };
            using (Fixture g = new Fixture())
            {
                g.Load(MatchSnapshot.FromJson(snapshot.ToJson()));
                Equal(f.match.Rng.Seed, g.match.Rng.Seed, "乱数のシード");
                for (int i = 0; i < expected.Length; i++) Equal(expected[i], g.match.Rng.Next(), $"読み込んだあと {i + 1} 回目の乱数");
            }
        }
    }

    // 盤面のハッシュ：8 桁の 16 進。駒・エナジー・マスが変われば変わる。予定の駒・乱数・千日手の記録では変わらない
    public static void CheckHashFollowsBoard()
    {
        Equal("811c9dc5", MatchHash.Fnv1a(""), "FNV-1a（空）");
        Equal("e40c292c", MatchHash.Fnv1a("a"), "FNV-1a（\"a\"）");

        using (Fixture f = new Fixture())
        {
            MatchSnapshot s = f.BuildRichSnapshot();
            string h = MatchHash.Compute(s);
            True(System.Text.RegularExpressions.Regex.IsMatch(h, "^[0-9a-f]{8}$"), $"ハッシュの形（{h}）");
            Equal(h, MatchHash.Compute(s.Clone()), "同じ局面なのにハッシュが違う");

            MatchSnapshot moved = s.Clone();
            moved.pieces.Find(p => p.type == PieceType.King && p.owner == 0).y += 1;
            True(MatchHash.Compute(moved) != h, "駒を動かしてもハッシュが変わらない");

            MatchSnapshot energy = s.Clone();
            energy.players[1].energy += 1;
            True(MatchHash.Compute(energy) != h, "エナジーが変わってもハッシュが変わらない");

            MatchSnapshot cell = s.Clone();
            cell.cells.Add(new CellSnapshot { x = 8, y = 8, state = CellState.Broken, burnUntil = -1, trap = -1 });
            True(MatchHash.Compute(cell) != h, "マスが壊れてもハッシュが変わらない");

            MatchSnapshot other = s.Clone();
            other.rng.state += 1;
            other.positions.Clear();
            other.extraTurn = !other.extraTurn;
            PieceSnapshot held = other.pieces.Find(p => p.held);
            held.moveCount += 5; // 予定の駒は盤の駒として数えない
            Equal(h, MatchHash.Compute(other), "乱数・千日手の記録・2回行動・予定の駒でハッシュが変わった");
        }
    }

    // ハッシュの盤は「段（x）ごとに筋（y）を並べる」（本編は x = 段・y = 筋で、Prototype とは x と y が逆）
    public static void CheckHashBoardOrientation()
    {
        MatchSnapshot s = new MatchSnapshot();
        s.players.Add(new PlayerSnapshot { owner = 0 });
        s.players.Add(new PlayerSnapshot { owner = 1 });
        s.pieces.Add(new PieceSnapshot { id = 1, type = PieceType.Pawn, owner = 0, x = 0, y = 1 });
        string text = MatchHash.BuildText(s);
        string firstRow = "[[null,[\"Pawn\",0,0,0,0,0,0],null,null,null,null,null,null,null],";
        True(text.Contains(firstRow), $"(x = 0, y = 1) の駒が 1 段目の 2 筋目に並ばない：{text}");
    }

    // 読み込めない局面を Validate が見分ける
    public static void CheckValidateRejectsBrokenSnapshots()
    {
        using (Fixture f = new Fixture())
        {
            MatchSnapshot s = f.BuildRichSnapshot();
            Equal(null, MatchSnapshotIO.Validate(s), "正しい局面");

            MatchSnapshot format = s.Clone();
            format.format = "something-else";
            True(MatchSnapshotIO.Validate(format) != null, "形式の名前が違うのに読める");

            MatchSnapshot newer = s.Clone();
            newer.version = MatchSnapshot.CurrentVersion + 1;
            True(MatchSnapshotIO.Validate(newer) != null, "新しい版なのに読める");

            MatchSnapshot overlap = s.Clone();
            PieceSnapshot a = overlap.pieces.Find(p => !p.held);
            PieceSnapshot b = overlap.pieces.FindLast(p => !p.held);
            b.x = a.x;
            b.y = a.y;
            True(MatchSnapshotIO.Validate(overlap) != null, "同じマスに駒が 2 つあるのに読める");

            MatchSnapshot heldWithoutStop = s.Clone();
            heldWithoutStop.timeStop = new TimeStopSnapshot();
            True(MatchSnapshotIO.Validate(heldWithoutStop) != null, "時間停止中でないのに予定の駒がある局面を読める");

            True(MatchSnapshotIO.Validate(MatchSnapshot.FromJson("{}")) != null, "空の JSON を読める");
            True(MatchSnapshot.FromJson("not json") == null, "JSON でない文字列が読めた");
        }
    }

    // 行動の hash は棋譜の記録用で、行動の比較（Key）には入らない。hash のない古い JSON も読める
    public static void CheckActionHashField()
    {
        MatchAction a = MatchAction.Move(new Vector2Int(2, 6), new Vector2Int(3, 6), false);
        MatchAction b = a.Clone();
        b.hash = "1a2b3c4d";
        Equal(a.Key, b.Key, "hash で行動の Key が変わった");
        Equal("1a2b3c4d", MatchAction.FromJson(b.ToJson()).hash, "hash が JSON を通らない");
        Equal("", MatchAction.FromJson("{\"type\":0,\"from\":{\"x\":2,\"y\":6},\"to\":{\"x\":3,\"y\":6}}").hash, "hash のない JSON");
    }

    // ------------------------------------------------------------
    //  局面を作る（GameSystem の代わりに、MatchState・BoardCells・駒の配列を直接持つ）
    // ------------------------------------------------------------
    sealed class Fixture : System.IDisposable
    {
        public readonly MatchState match = new MatchState(PlayerType.Player1, 12345u);
        public readonly BoardCells cells = new BoardCells();
        public readonly GamePiece[] board = new GamePiece[BoardCells.Size * BoardCells.Size];
        public readonly Dictionary<PieceType, int>[] hands = { NewHand(), NewHand() };
        public readonly SkillDeck[] decks = new SkillDeck[2];
        public TimeStopState timeStop;
        public bool extraTurn;
        public readonly List<PositionSnapshot> positions = new List<PositionSnapshot>();

        private readonly List<Object> created = new List<Object>();
        private readonly Dictionary<string, SkillDefinition> skills = new Dictionary<string, SkillDefinition>();

        public Fixture()
        {
            foreach (string id in new[] { "Shield", "Sword", "Pitfall", "Meteor", "TimeStop" })
            {
                SkillDefinition skill = ScriptableObject.CreateInstance<SkillDefinition>();
                skill.name = id + SkillDefinition.AssetSuffix;
                skill.skillName = id;
                skills[id] = skill;
                created.Add(skill);
            }
        }

        static Dictionary<PieceType, int> NewHand()
        {
            Dictionary<PieceType, int> hand = new Dictionary<PieceType, int>();
            foreach (PieceType type in PieceRules.HandOrder) hand[type] = 0;
            return hand;
        }

        // 駒の GameObject を作る（プレハブの代わり。見た目はない）
        public GamePiece NewPiece(PieceType type, PlayerType owner, int x, int y, bool attach = true)
        {
            GameObject obj = new GameObject($"{owner}_{type}");
            created.Add(obj);
            GamePiece piece;
            switch (type)
            {
                case PieceType.King: piece = obj.AddComponent<PieceKing>(); break;
                case PieceType.Rook: piece = obj.AddComponent<PieceRook>(); break;
                case PieceType.Bishop: piece = obj.AddComponent<PieceBishop>(); break;
                case PieceType.GoldGeneral: piece = obj.AddComponent<PieceGoldGeneral>(); break;
                case PieceType.SilverGeneral: piece = obj.AddComponent<PieceSilverGeneral>(); break;
                case PieceType.Knight: piece = obj.AddComponent<PieceKnight>(); break;
                case PieceType.Lance: piece = obj.AddComponent<PieceLance>(); break;
                case PieceType.Gunpowder: piece = obj.AddComponent<PieceGunpowder>(); break;
                case PieceType.Sniper: piece = obj.AddComponent<PieceSniper>(); break;
                default: piece = obj.AddComponent<PiecePawn>(); break;
            }
            piece.type = type;
            piece.player = owner;
            piece.X = x;
            piece.Y = y;
            piece.Cells = cells;
            if (attach) board[GameHelper.CalcPanelNum(x, y)] = piece;
            return piece;
        }

        SkillDeck NewDeck(params string[] ids)
        {
            SkillDeck deck = MatchSnapshotIO.BuildDeck(ids, Find);
            created.Add(deck);
            return deck;
        }

        SkillDefinition Find(string id) => skills.TryGetValue(id, out SkillDefinition s) ? s : null;

        // GameSystem.ExportState と同じ（GameSystem だけが持つ状態は、ここでは自分のフィールドから詰める）
        public MatchSnapshot Export()
        {
            MatchSnapshot s = MatchSnapshotIO.Capture(match, cells, board, timeStop, hands, decks);
            s.extraTurn = extraTurn;
            s.positions.AddRange(positions);
            return s;
        }

        // GameSystem.ImportState と同じ順に戻して、すぐ書き出し直す
        public MatchSnapshot Load(MatchSnapshot s)
        {
            MatchSnapshotIO.Restore(s, match, cells, ps =>
            {
                GamePiece piece = NewPiece(ps.type, (PlayerType)ps.owner, ps.x, ps.y, !ps.held);
                MatchSnapshotIO.ApplyPiece(ps, piece);
                return piece;
            }, out timeStop, out _);
            for (int i = 0; i < 2; i++)
            {
                PlayerSnapshot p = s.Player((PlayerType)i);
                MatchSnapshotIO.RestoreHand(p, hands[i]);
                decks[i] = MatchSnapshotIO.BuildDeck(p.loadout, Find);
                created.Add(decks[i]);
            }
            extraTurn = s.extraTurn;
            positions.Clear();
            positions.AddRange(s.positions);
            return Export();
        }

        // いろいろな状態を含む局面
        public MatchSnapshot BuildRichSnapshot()
        {
            // ターン・手番（第3ターンの先手の手番。第5手）
            for (int i = 0; i < 5; i++)
            {
                match.Turn.BeginTurn();
                if (i < 4) match.Turn.EndTurn();
            }
            match.Energy.Gain(PlayerType.Player1, 7);
            match.Energy.Gain(PlayerType.Player2, 4);
            for (int i = 0; i < 3; i++) match.Rng.Next();
            match.RecordFallenInitial(PlayerType.Player1, PieceType.Pawn, DefeatKind.Capture);
            match.RecordFallenInitial(PlayerType.Player2, PieceType.Knight, DefeatKind.Vanish);
            match.UnlockTranscendent(PlayerType.Player2);
            match.MarkTranscendentUsed(PlayerType.Player2);
            match.StartWorldEnd(PlayerType.Player2);

            cells.Break(3, 3);
            cells.SetBurning(5, 5, 3, match.Turn.TurnNumber);
            cells.SetTrap(6, 1, PlayerType.Player1);

            GamePiece k1 = NewPiece(PieceType.King, PlayerType.Player1, 0, 4);
            k1.IsInitial = true;
            NewPiece(PieceType.King, PlayerType.Player2, 8, 4).IsInitial = true;
            GamePiece rook = NewPiece(PieceType.Rook, PlayerType.Player1, 4, 4);
            rook.isPromoted = true;
            rook.hasShield = true;
            rook.hasPhalanx = true;
            rook.moveCount = 3;
            GamePiece undead = NewPiece(PieceType.GoldGeneral, PlayerType.Player2, 5, 2);
            undead.MakeUndead();
            NewPiece(PieceType.Gunpowder, PlayerType.Player2, 7, 7);
            NewPiece(PieceType.Sniper, PlayerType.Player1, 1, 8).moveCount = 1;

            // 時間停止中：先手の飛車が (3, 4) → (4, 4) で銀を取る予定（銀は盤から外してその場に残す）＋ 落とし穴の予定
            GamePiece silver = NewPiece(PieceType.SilverGeneral, PlayerType.Player2, 4, 4, false);
            timeStop = new TimeStopState(PlayerType.Player1, 3) { Remaining = 2, JustStarted = false };
            TimeStopMove move = new TimeStopMove { Piece = rook, From = new Vector2Int(3, 4), To = new Vector2Int(4, 4), Captured = true, Counted = true };
            timeStop.Moves.Add(move);
            timeStop.Held.Add(silver);
            timeStop.Pending.Add(new TimeStopPending
            {
                Kind = TimeStopKind.Capture,
                Target = silver,
                TargetType = PieceType.SilverGeneral,
                ByOwner = PlayerType.Player1,
                Piece = rook,
                Move = move,
                At = new Vector2Int(4, 4),
                HitDirection = new Vector2Int(1, 0),
            });
            timeStop.Pending.Add(new TimeStopPending
            {
                Kind = TimeStopKind.Cell,
                CellEffect = TimeStopCellEffect.Trap,
                CellOwner = PlayerType.Player1,
                At = new Vector2Int(2, 2),
            });

            hands[0][PieceType.Pawn] = 2;
            hands[0][PieceType.Gunpowder] = 1;
            hands[1][PieceType.Bishop] = 1;
            decks[0] = NewDeck("Shield", "Sword", "Pitfall", "Meteor", "TimeStop");
            decks[1] = NewDeck("Sword", "", "Pitfall", "Meteor", "TimeStop");

            extraTurn = true;
            positions.Add(new PositionSnapshot { key = "abc|1", side = 0, inCheck = false });
            positions.Add(new PositionSnapshot { key = "abd|2", side = 1, inCheck = true });
            return Export();
        }

        public void Dispose()
        {
            foreach (Object o in created)
            {
                if (o != null) Object.DestroyImmediate(o);
            }
        }
    }

    // ------------------------------------------------------------
    //  確かめる
    // ------------------------------------------------------------

    static void True(bool condition, string message)
    {
        if (!condition) throw new CheckFailedException(message);
    }

    static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new CheckFailedException($"{message}\n  期待: {expected}\n  実際: {actual}");
        }
    }

    // Check〜 のメソッドをすべて実行する（メニューから。テストは 1 つずつ呼ぶ）
    [UnityEditor.MenuItem("UltimateShogi/局面の書き出しを確かめる")]
    static void RunAll()
    {
        int failed = 0, total = 0;
        foreach (System.Reflection.MethodInfo m in typeof(MatchSnapshotChecks).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (!m.Name.StartsWith("Check") || m.GetParameters().Length != 0) continue;
            total++;
            try
            {
                m.Invoke(null, null);
            }
            catch (System.Reflection.TargetInvocationException e)
            {
                failed++;
                Debug.LogError($"[局面の書き出し] {m.Name}：{e.InnerException?.Message}");
            }
        }
        if (failed == 0) Debug.Log($"[局面の書き出し] {total} 件の確認がすべて通りました");
        else Debug.LogError($"[局面の書き出し] {total} 件中 {failed} 件が失敗しました");
    }
}
