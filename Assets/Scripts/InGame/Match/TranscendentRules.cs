// 超常魔法の解放条件（Prototype の src/core/rules.js の transcendentProgress）。
//
// 次の 2 つが同時に成り立つと解放される（一度解放されたら、条件が崩れても解放のまま。MatchState.UnlockTranscendent）：
//  ① 相手の駒が 1 つ以上、自陣（自分側の 3 段）にいる（侵入）
//  ② 盤上の駒の数（火薬を除く）が、自分より相手のほうが多い
// 判定は手番の始まりと、手番を終えない行動（2回行動・手番を使わない魔法）のあと（GameSystem.CheckTranscendentUnlock）。
// Prototype と同じく、侵入には火薬も数える（駒数からだけ除く）
public struct TranscendentProgress
{
    public bool Invaded; // 相手の駒が自陣にいる
    public int Mine;     // 盤上の自分の駒数（火薬を除く）
    public int Theirs;   // 盤上の相手の駒数（火薬を除く）

    public bool MoreEnemies => Theirs > Mine;
    public bool Met => Invaded && MoreEnemies;
}

public static class TranscendentRules
{
    // owner から見た解放条件の達成状況（盤上の今の状態）
    public static TranscendentProgress Progress(GameBoard board, PlayerType owner)
    {
        TranscendentProgress p = new TranscendentProgress();
        if (board == null) return p;
        foreach (GamePiece piece in board.GetBoardData())
        {
            if (piece == null) continue;
            if (piece.player != owner && BoardZones.IsOwnZone(owner, piece.X)) p.Invaded = true;
            if (PieceRules.IsExplosive(piece.type)) continue;
            if (piece.player == owner) p.Mine++;
            else p.Theirs++;
        }
        return p;
    }
}
