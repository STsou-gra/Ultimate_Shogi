using System.Text;

// 盤面のハッシュ（Prototype の src/core/hash.js の hashState と同じ考え方）。
// 行動（GameSystem.ActionHistory）ごとに「その行動の処理が終わった時点」のハッシュを MatchAction.hash に残し、
// 棋譜の再生・オンライン対戦で盤面がずれていないかを確かめるのに使う。
//
// 局面（MatchSnapshot）から、次の項目を JSON の配列の形の文字列にして、FNV-1a 32bit の 16 進 8 桁にする：
//   手番・ターン数・手数・勝敗が決まったか・エナジー・持ち駒（PieceRules.HandOrder の順）・
//   盤の駒（[種類, 持ち主, 成り, 移動回数, 盾, ファランクス, アンデッド]。時間停止の予定の駒は入れない）・
//   マス（[状態, 炎上が終わるターン, 落とし穴の持ち主]）・超常魔法の使用・解放・世界の終わり・倒された初期配置の駒の種類・
//   時間停止（[発動したプレイヤー, 残り回数, 予定の [種類, x, y, 処理済み]]）
// 盤は Prototype と同じく「段ごとに筋を並べた」9×9 の配列にする。本編の座標は x = 段・y = 筋で、Prototype（x = 筋・y = 段）とは
// x と y が逆なので、外側を x（段）、内側を y（筋）で回す。
// 乱数・魔法の編成・千日手の記録・2回行動の続きは Prototype と同じく入れない。
// 今までに残したハッシュと比べられるよう、入れる項目と並びは変えないこと（変えるなら MatchSnapshot.CurrentVersion も上げる）
public static class MatchHash
{
    public static string Compute(MatchSnapshot s)
    {
        if (s == null) return "";
        return Fnv1a(BuildText(s));
    }

    // FNV-1a 32bit の 16 進 8 桁（Prototype と同じく、文字列の UTF-16 の 1 文字ずつ）
    public static string Fnv1a(string text)
    {
        unchecked
        {
            uint h = 0x811c9dc5u;
            foreach (char c in text)
            {
                h ^= c;
                h *= 0x01000193u;
            }
            return h.ToString("x8");
        }
    }

    // ハッシュにする文字列（テスト・ずれの調査用に公開）
    public static string BuildText(MatchSnapshot s)
    {
        PlayerSnapshot p1 = s.Player(PlayerType.Player1) ?? new PlayerSnapshot { owner = 0 };
        PlayerSnapshot p2 = s.Player(PlayerType.Player2) ?? new PlayerSnapshot { owner = 1 };
        PlayerSnapshot[] players = { p1, p2 };

        PieceSnapshot[] board = new PieceSnapshot[BoardCells.Size * BoardCells.Size];
        if (s.pieces != null)
        {
            foreach (PieceSnapshot p in s.pieces)
            {
                if (p == null || p.held || !BoardCells.InBounds(p.x, p.y)) continue;
                board[GameHelper.CalcPanelNum(p.x, p.y)] = p;
            }
        }
        CellSnapshot?[] cells = new CellSnapshot?[BoardCells.Size * BoardCells.Size];
        if (s.cells != null)
        {
            foreach (CellSnapshot c in s.cells)
            {
                if (BoardCells.InBounds(c.x, c.y)) cells[GameHelper.CalcPanelNum(c.x, c.y)] = c;
            }
        }

        StringBuilder sb = new StringBuilder(2048);
        sb.Append('[');
        sb.Append(s.current).Append(',').Append(s.turnNumber).Append(',').Append(s.moveNumber).Append(',').Append(Bool(s.gameOver)).Append(',');

        // エナジー
        sb.Append('[').Append(p1.energy).Append(',').Append(p2.energy).Append("],");

        // 持ち駒
        sb.Append('[');
        for (int i = 0; i < players.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('{');
            for (int k = 0; k < PieceRules.HandOrder.Length; k++)
            {
                PieceType type = PieceRules.HandOrder[k];
                if (k > 0) sb.Append(',');
                sb.Append('"').Append(type).Append("\":").Append(players[i].HandCountOf(type));
            }
            sb.Append('}');
        }
        sb.Append("],");

        // 盤の駒（外側 = 段 x、内側 = 筋 y）
        sb.Append('[');
        for (int x = 0; x < BoardCells.Size; x++)
        {
            if (x > 0) sb.Append(',');
            sb.Append('[');
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (y > 0) sb.Append(',');
                PieceSnapshot p = board[GameHelper.CalcPanelNum(x, y)];
                if (p == null)
                {
                    sb.Append("null");
                    continue;
                }
                sb.Append("[\"").Append(p.type).Append("\",").Append(p.owner).Append(',').Append(Flag(p.promoted)).Append(',')
                  .Append(p.moveCount).Append(',').Append(Flag(p.shield)).Append(',').Append(Flag(p.phalanx)).Append(',').Append(Flag(p.undead)).Append(']');
            }
            sb.Append(']');
        }
        sb.Append("],");

        // マス
        sb.Append('[');
        for (int x = 0; x < BoardCells.Size; x++)
        {
            if (x > 0) sb.Append(',');
            sb.Append('[');
            for (int y = 0; y < BoardCells.Size; y++)
            {
                if (y > 0) sb.Append(',');
                CellSnapshot? c = cells[GameHelper.CalcPanelNum(x, y)];
                CellState state = c.HasValue ? c.Value.state : CellState.Normal;
                int burnUntil = c.HasValue && state == CellState.Burning ? c.Value.burnUntil : -1;
                int trap = c.HasValue && state != CellState.Broken ? c.Value.trap : -1;
                sb.Append("[\"").Append(state).Append("\",").Append(Int(burnUntil)).Append(',').Append(Player(trap)).Append(']');
            }
            sb.Append(']');
        }
        sb.Append("],");

        // 超常魔法の使用・解放・世界の終わり・倒された初期配置の駒の種類
        sb.Append('[').Append(Bool(p1.transcendentUsed)).Append(',').Append(Bool(p2.transcendentUsed)).Append("],");
        sb.Append('[').Append(Bool(p1.transcendentUnlocked)).Append(',').Append(Bool(p2.transcendentUnlocked)).Append("],");
        sb.Append('[').Append(Int(p1.worldEndSince)).Append(',').Append(Int(p2.worldEndSince)).Append("],");
        sb.Append('[');
        for (int i = 0; i < players.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append('[');
            if (players[i].fallenInitial != null)
            {
                for (int k = 0; k < players[i].fallenInitial.Count; k++)
                {
                    if (k > 0) sb.Append(',');
                    sb.Append('"').Append(players[i].fallenInitial[k].type).Append('"');
                }
            }
            sb.Append(']');
        }
        sb.Append("],");

        // 時間停止
        TimeStopSnapshot ts = s.timeStop;
        if (ts == null || !ts.active)
        {
            sb.Append("null");
        }
        else
        {
            sb.Append('[').Append(ts.owner).Append(',').Append(ts.remaining).Append(",[");
            if (ts.pending != null)
            {
                for (int i = 0; i < ts.pending.Count; i++)
                {
                    TimeStopPendingSnapshot e = ts.pending[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("[\"").Append(e.kind).Append("\",").Append(e.at.x).Append(',').Append(e.at.y).Append(',').Append(Flag(e.done)).Append(']');
                }
            }
            sb.Append("]]");
        }
        sb.Append(']');
        return sb.ToString();
    }

    static string Bool(bool b) => b ? "true" : "false";
    static char Flag(bool b) => b ? '1' : '0';
    static string Int(int v) => v >= 0 ? v.ToString() : "null";
    static string Player(int v) => v == (int)PlayerType.Player1 || v == (int)PlayerType.Player2 ? v.ToString() : "null";
}
