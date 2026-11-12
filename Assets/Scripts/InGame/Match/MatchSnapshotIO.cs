using System.Collections.Generic;
using UnityEngine;

// 局面（MatchSnapshot）を詰める・戻す処理（Prototype の Game.exportState / importState のうち、データの分）。
// GameSystem.ExportState / ImportState が使う。GameSystem（シーン）に依存しないので、EditMode のテストからも呼べる。
//
//  ・Capture … 対局の状態（MatchState・BoardCells・盤の駒・時間停止・持ち駒・魔法の編成）から MatchSnapshot を作る。
//              駒の id は書き出すたびに 1 から振り直す（盤の添字 GameHelper.CalcPanelNum の順 → 時間停止の予定の駒）。
//              同じ局面からは必ず同じ JSON になる（書き出し → 読み込み → 書き出しで同じ JSON）
//  ・Restore … MatchSnapshot を MatchState・BoardCells に戻し、駒を spawn で作り直し、時間停止の状態を組み立てる。
//              駒の GameObject を作るのは spawn（GameSystem では GameBoard.SpawnPiece）。持ち駒は RestoreHand、編成は BuildDeck
//  ・Validate … 読み込める局面か（読めないなら理由）
// GameSystem だけが持つ状態（2回行動の続き・勝敗・千日手の記録）は GameSystem.ExportState / ImportState が詰める
public static class MatchSnapshotIO
{
    // ------------------------------------------------------------
    //  書き出し
    // ------------------------------------------------------------

    // hands・decks は添字 = (int)PlayerType（null の要素は空の持ち駒・編成なし）
    public static MatchSnapshot Capture(MatchState match, BoardCells cells, GamePiece[] board, TimeStopState timeStop,
        IReadOnlyList<Dictionary<PieceType, int>> hands, IReadOnlyList<SkillDeck> decks)
    {
        MatchSnapshot s = new MatchSnapshot();
        if (match != null)
        {
            s.firstPlayer = (int)match.Turn.FirstPlayer;
            s.current = (int)match.Turn.Current;
            s.turnNumber = match.Turn.TurnNumber;
            s.moveNumber = match.Turn.MoveNumber;
            s.rng.seed = match.Rng.Seed;
            s.rng.state = match.Rng.State;
        }

        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            int i = (int)owner;
            PlayerSnapshot p = new PlayerSnapshot { owner = i };
            Dictionary<PieceType, int> hand = hands != null && i < hands.Count ? hands[i] : null;
            foreach (PieceType type in PieceRules.HandOrder)
            {
                int count = 0;
                if (hand != null) hand.TryGetValue(type, out count);
                p.hand.Add(new HandCount { piece = type, count = count });
            }
            p.loadout = LoadoutOf(decks != null && i < decks.Count ? decks[i] : null);
            if (match != null)
            {
                p.energy = match.Energy.Get(owner);
                p.transcendentUnlocked = match.IsTranscendentUnlocked(owner);
                p.transcendentUsed = match.IsTranscendentUsed(owner);
                p.worldEndSince = match.WorldEndSince(owner) ?? -1;
                foreach (FallenPieceRecord f in match.FallenInitial(owner))
                {
                    p.fallenInitial.Add(new FallenSnapshot { type = f.Type, kind = f.Kind, turn = f.TurnNumber });
                }
            }
            s.players.Add(p);
        }

        // 駒（盤の添字の順 → 時間停止の予定の駒）
        Dictionary<GamePiece, int> ids = new Dictionary<GamePiece, int>();
        if (board != null)
        {
            foreach (GamePiece piece in board) AddPiece(s, ids, piece, false);
        }
        if (timeStop != null)
        {
            foreach (TimeStopPending e in timeStop.Pending) AddPiece(s, ids, timeStop.HeldPiece(e), true);
            // 予定から指されていない予定の駒（念のため。場所の順にそろえる）
            List<GamePiece> rest = new List<GamePiece>();
            foreach (GamePiece q in timeStop.Held)
            {
                if (q != null && !ids.ContainsKey(q)) rest.Add(q);
            }
            rest.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.type.CompareTo(b.type));
            foreach (GamePiece q in rest) AddPiece(s, ids, q, true);
        }

        if (cells != null) s.cells = cells.Capture();
        s.timeStop = CaptureTimeStop(timeStop, ids);
        return s;
    }

    // 駒を書き出しに足す（id は足した順に 1 から。同じ駒は 1 回だけ）
    static void AddPiece(MatchSnapshot s, Dictionary<GamePiece, int> ids, GamePiece piece, bool held)
    {
        if (piece == null || ids.ContainsKey(piece)) return;
        int id = s.pieces.Count + 1;
        ids[piece] = id;
        s.pieces.Add(CapturePiece(piece, id, held));
    }

    static int IdOf(Dictionary<GamePiece, int> ids, GamePiece piece)
    {
        int id;
        return piece != null && ids.TryGetValue(piece, out id) ? id : -1;
    }

    static GamePiece PieceOf(Dictionary<int, GamePiece> pieces, int id)
    {
        GamePiece piece;
        return id >= 0 && pieces.TryGetValue(id, out piece) ? piece : null;
    }

    public static PieceSnapshot CapturePiece(GamePiece piece, int id, bool held)
    {
        return new PieceSnapshot
        {
            id = id,
            type = piece.type,
            owner = (int)piece.player,
            x = piece.X,
            y = piece.Y,
            promoted = piece.isPromoted,
            moveCount = piece.moveCount,
            initial = piece.IsInitial,
            shield = piece.hasShield,
            phalanx = piece.hasPhalanx,
            undead = piece.isUndead,
            wing = piece.hasWing,
            held = held,
        };
    }

    static TimeStopSnapshot CaptureTimeStop(TimeStopState ts, Dictionary<GamePiece, int> ids)
    {
        TimeStopSnapshot t = new TimeStopSnapshot();
        if (ts == null) return t;

        t.active = true;
        t.owner = (int)ts.Owner;
        t.total = ts.Total;
        t.remaining = ts.Remaining;
        t.ending = ts.Ending;
        t.justStarted = ts.JustStarted;
        foreach (TimeStopMove m in ts.Moves)
        {
            t.moves.Add(new TimeStopMoveSnapshot
            {
                piece = IdOf(ids, m.Piece),
                from = m.From,
                to = m.To,
                captured = m.Captured,
                bounced = m.Bounced,
                undone = m.Undone,
                counted = m.Counted,
            });
        }
        foreach (TimeStopPending e in ts.Pending)
        {
            t.pending.Add(new TimeStopPendingSnapshot
            {
                kind = e.Kind,
                piece = IdOf(ids, e.Piece),
                move = e.Move != null ? ts.Moves.IndexOf(e.Move) : -1,
                at = e.At,
                done = e.Done,
                target = IdOf(ids, e.Target),
                targetType = e.TargetType,
                byOwner = (int)e.ByOwner,
                reason = e.Reason ?? "",
                hitDirection = e.HitDirection,
                cellEffect = e.CellEffect,
                cellOwner = (int)e.CellOwner,
                turns = e.Turns,
            });
        }
        return t;
    }

    // 魔法の編成（枠の順に SkillDefinition.Id。空の枠は ""）
    public static List<string> LoadoutOf(SkillDeck deck)
    {
        List<string> ids = new List<string>();
        if (deck == null || deck.skills == null) return ids;
        foreach (SkillDefinition skill in deck.skills) ids.Add(skill != null ? skill.Id : "");
        return ids;
    }

    // ------------------------------------------------------------
    //  読み込み
    // ------------------------------------------------------------

    // s を match・cells に戻し、駒を spawn で作り直す（spawn は駒を作って盤に置き、ApplyPiece で状態を写して返す。
    // held の駒は盤のデータに入れずにその場に置くこと）。時間停止中なら timeStop に組み立てた状態が入る（それ以外は null）。
    // pieces は id → 作った駒。s は Validate を通っていること
    public static void Restore(MatchSnapshot s, MatchState match, BoardCells cells, System.Func<PieceSnapshot, GamePiece> spawn,
        out TimeStopState timeStop, out Dictionary<int, GamePiece> pieces)
    {
        match.Turn.Restore((PlayerType)s.firstPlayer, (PlayerType)s.current, s.turnNumber, s.moveNumber);
        match.Rng.Restore((uint)s.rng.seed, (uint)s.rng.state);
        foreach (PlayerType owner in new[] { PlayerType.Player1, PlayerType.Player2 })
        {
            PlayerSnapshot p = s.Player(owner);
            if (p == null) continue;
            match.Energy.Restore(owner, p.energy);
            match.RestoreTranscendent(owner, p.transcendentUnlocked, p.transcendentUsed);
            match.RestoreWorldEnd(owner, p.worldEndSince >= 0 ? p.worldEndSince : (int?)null);
            List<FallenPieceRecord> fallen = new List<FallenPieceRecord>();
            foreach (FallenSnapshot f in p.fallenInitial) fallen.Add(new FallenPieceRecord { Type = f.type, Kind = f.kind, TurnNumber = f.turn });
            match.RestoreFallenInitial(owner, fallen);
        }

        if (cells != null) cells.Restore(s.cells);

        pieces = new Dictionary<int, GamePiece>();
        foreach (PieceSnapshot ps in s.pieces)
        {
            GamePiece piece = spawn != null ? spawn(ps) : null;
            if (piece != null) pieces[ps.id] = piece;
        }

        timeStop = RestoreTimeStop(s, pieces);
    }

    // 駒の状態を写す（種類・持ち主・位置は駒を作るときに決める）
    public static void ApplyPiece(PieceSnapshot ps, GamePiece piece)
    {
        if (ps == null || piece == null) return;
        piece.type = ps.type;
        piece.player = (PlayerType)ps.owner;
        piece.X = ps.x;
        piece.Y = ps.y;
        if (ps.undead) piece.MakeUndead();
        piece.IsInitial = ps.initial;
        piece.isPromoted = ps.promoted;
        piece.moveCount = ps.moveCount;
        piece.hasShield = ps.shield;
        piece.hasPhalanx = ps.phalanx;
        piece.hasWing = ps.wing;
        piece.UpdateVisual();
    }

    static TimeStopState RestoreTimeStop(MatchSnapshot s, Dictionary<int, GamePiece> pieces)
    {
        TimeStopSnapshot t = s.timeStop;
        if (t == null || !t.active) return null;

        TimeStopState ts = new TimeStopState((PlayerType)t.owner, t.total)
        {
            Remaining = t.remaining,
            Ending = t.ending,
            JustStarted = t.justStarted,
        };
        foreach (TimeStopMoveSnapshot m in t.moves)
        {
            ts.Moves.Add(new TimeStopMove
            {
                Piece = PieceOf(pieces, m.piece),
                From = m.from,
                To = m.to,
                Captured = m.captured,
                Bounced = m.bounced,
                Undone = m.undone,
                Counted = m.counted,
            });
        }
        foreach (TimeStopPendingSnapshot e in t.pending)
        {
            ts.Pending.Add(new TimeStopPending
            {
                Kind = e.kind,
                Piece = PieceOf(pieces, e.piece),
                Move = e.move >= 0 && e.move < ts.Moves.Count ? ts.Moves[e.move] : null,
                At = e.at,
                Done = e.done,
                Target = PieceOf(pieces, e.target),
                TargetType = e.targetType,
                ByOwner = (PlayerType)e.byOwner,
                Reason = e.reason ?? "",
                HitDirection = e.hitDirection,
                CellEffect = e.cellEffect,
                CellOwner = (PlayerType)e.cellOwner,
                Turns = e.turns,
            });
        }
        foreach (PieceSnapshot ps in s.pieces)
        {
            GamePiece q = ps.held ? PieceOf(pieces, ps.id) : null;
            if (q != null) ts.Held.Add(q);
        }
        return ts;
    }

    // 予定の駒 q を灰色で残すか（取られる予定・消える予定の駒。爆発する予定の火薬は色のまま。GameSystem.TimeStop.cs の HoldPiece と同じ）
    public static bool IsGrayHeld(TimeStopState ts, GamePiece q)
    {
        if (ts == null || q == null) return true;
        foreach (TimeStopPending e in ts.Pending)
        {
            if (e.Target == q && !e.Done && !TimeStopState.IsUndone(e)) return e.Kind != TimeStopKind.Explode;
        }
        return true;
    }

    // 持ち駒を書き出した数にする（PieceRules.HandOrder の駒。書き出しにない駒は 0 枚）
    public static void RestoreHand(PlayerSnapshot p, Dictionary<PieceType, int> hand)
    {
        if (hand == null) return;
        foreach (PieceType type in PieceRules.HandOrder) hand[type] = p != null ? Mathf.Max(0, p.HandCountOf(type)) : 0;
    }

    // 魔法の編成を作る（Id から find で SkillDefinition を探す。見つからない魔法の枠は空にする）
    public static SkillDeck BuildDeck(IReadOnlyList<string> ids, System.Func<string, SkillDefinition> find)
    {
        SkillDeck deck = ScriptableObject.CreateInstance<SkillDeck>();
        deck.name = "LoadedDeck";
        deck.skills = new List<SkillDefinition>();
        if (ids == null) return deck;
        foreach (string id in ids)
        {
            SkillDefinition skill = !string.IsNullOrEmpty(id) && find != null ? find(id) : null;
            if (skill == null && !string.IsNullOrEmpty(id)) Debug.LogWarning($"局面の読み込み：魔法「{id}」が見つからないので、その枠は空にします");
            deck.skills.Add(skill);
        }
        return deck;
    }

    // ------------------------------------------------------------
    //  読み込める局面か
    // ------------------------------------------------------------

    // 読み込めるなら null、読み込めなければ理由
    public static string Validate(MatchSnapshot s)
    {
        if (s == null) return "局面のデータがありません";
        if (s.format != MatchSnapshot.Format) return "この対局の局面の書き出しではありません";
        if (s.rng == null || s.pieces == null || s.cells == null || s.positions == null) return "局面のデータが壊れています";
        if (s.version < 1 || s.version > MatchSnapshot.CurrentVersion) return $"対応していない局面の版です（version {s.version}）";
        if (!IsPlayer(s.firstPlayer) || !IsPlayer(s.current)) return "先手・手番のプレイヤーが正しくありません";
        if (s.turnNumber < 0 || s.moveNumber < 1) return "ターン数・手数が正しくありません";
        if (s.gameOver && !s.draw && !IsPlayer(s.winner)) return "勝者が正しくありません";
        if (s.players == null || s.players.Count != 2 || s.Player(PlayerType.Player1) == null || s.Player(PlayerType.Player2) == null)
        {
            return "プレイヤーの情報が 2 人分ありません";
        }

        bool timeStopped = s.timeStop != null && s.timeStop.active;
        HashSet<int> ids = new HashSet<int>();
        HashSet<int> squares = new HashSet<int>();
        foreach (PieceSnapshot p in s.pieces)
        {
            if (p == null) return "駒の情報が壊れています";
            if (p.id <= 0 || !ids.Add(p.id)) return $"駒の id が正しくありません（{p.id}）";
            if (!System.Enum.IsDefined(typeof(PieceType), p.type)) return $"知らない駒です（{(int)p.type}）";
            if (!IsPlayer(p.owner)) return "駒の持ち主が正しくありません";
            if (!BoardCells.InBounds(p.x, p.y)) return $"盤の外に駒があります（{p.x}, {p.y}）";
            if (p.held && !timeStopped) return "時間停止中でないのに、予定の駒があります";
            if (!p.held && !squares.Add(GameHelper.CalcPanelNum(p.x, p.y))) return $"同じマスに駒が 2 つあります（{p.x}, {p.y}）";
        }

        HashSet<int> cellSquares = new HashSet<int>();
        foreach (CellSnapshot c in s.cells)
        {
            if (!BoardCells.InBounds(c.x, c.y) || !cellSquares.Add(GameHelper.CalcPanelNum(c.x, c.y))) return $"マスの状態が正しくありません（{c.x}, {c.y}）";
            if (!System.Enum.IsDefined(typeof(CellState), c.state)) return $"知らないマスの状態です（{(int)c.state}）";
        }

        if (timeStopped)
        {
            TimeStopSnapshot t = s.timeStop;
            if (!IsPlayer(t.owner)) return "時間停止を発動したプレイヤーが正しくありません";
            foreach (TimeStopMoveSnapshot m in t.moves)
            {
                if (m == null || (m.piece >= 0 && !ids.Contains(m.piece))) return "時間停止の移動の記録が正しくありません";
            }
            foreach (TimeStopPendingSnapshot e in t.pending)
            {
                if (e == null) return "時間停止の予定が壊れています";
                if (!System.Enum.IsDefined(typeof(TimeStopKind), e.kind)) return "知らない時間停止の予定です";
                if ((e.piece >= 0 && !ids.Contains(e.piece)) || (e.target >= 0 && !ids.Contains(e.target))) return "時間停止の予定の駒が見つかりません";
                if (e.move >= t.moves.Count) return "時間停止の予定の移動が見つかりません";
            }
        }
        return null;
    }

    static bool IsPlayer(int v) => v == (int)PlayerType.Player1 || v == (int)PlayerType.Player2;
}
