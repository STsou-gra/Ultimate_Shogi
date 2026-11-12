// ターン数・手数（Prototype の src/core/turn.js の startTurn / passTurn / roundEnd）
// 先手と後手が1回ずつ指して1ターン。手番が始まるたびに BeginTurn、手番を相手に渡すときに EndTurn を呼ぶ。
// 2回行動のように手番を渡さずに続けて指すときは、どちらも呼ばない（ターン数・手数は進まない）
public class TurnState
{
    public PlayerType FirstPlayer { get; private set; }   // 先手
    public PlayerType Current { get; private set; }
    public int TurnNumber { get; private set; }     // 第Nターン（対局開始前は 0）
    public int MoveNumber { get; private set; } = 1; // 第M手

    // 先手の手番が始まってターン数が増えた（引数は新しいターン数）
    public event System.Action<int> TurnAdvanced;
    // 後手の手番が終わった（引数は終わったターン数）。手番交代の前に呼ばれる
    public event System.Action<int> RoundEnded;

    public TurnState(PlayerType firstPlayer)
    {
        FirstPlayer = firstPlayer;
        Current = firstPlayer;
    }

    public PlayerType SecondPlayer => BoardZones.Opponent(FirstPlayer);

    // 序盤（EnemyZoneDropLockTurns ターン以下）は敵陣に持ち駒を打てない
    public bool IsEnemyZoneDropLocked => TurnNumber <= MatchConfig.EnemyZoneDropLockTurns;

    // 今のターンが、敵陣への打ち込みが解禁された最初のターンか
    public bool IsDropUnlockTurn => TurnNumber == MatchConfig.EnemyZoneDropLockTurns + 1;

    // 手番の始まり：先手の番ならターン数を進める
    public void BeginTurn()
    {
        if (Current != FirstPlayer) return;
        TurnNumber++;
        TurnAdvanced?.Invoke(TurnNumber);
    }

    // 手番の終わり：後手の番ならターンの終わり（RoundEnded）→ 手番交代・手数を進める
    public void EndTurn()
    {
        if (Current == SecondPlayer)
        {
            RoundEnded?.Invoke(TurnNumber);
        }
        Current = BoardZones.Opponent(Current);
        MoveNumber++;
    }

    // 書き出した局面から戻す（GameSystem.ImportState。通知は出さない）
    public void Restore(PlayerType firstPlayer, PlayerType current, int turnNumber, int moveNumber)
    {
        FirstPlayer = firstPlayer;
        Current = current;
        TurnNumber = turnNumber;
        MoveNumber = moveNumber;
    }

    // 合戦将棋型（手番がない）：行動 1 回ごとに手数だけ進める（Prototype の afterRealtimeAction の moveNumber++）
    public void AdvanceMove()
    {
        MoveNumber++;
    }

    // 合戦将棋型：ターンはない（第1ターンのまま。Prototype の setupGame と同じ）
    public void BeginRealtime()
    {
        if (TurnNumber == 0) TurnNumber = 1;
    }

    // 画面表示用：「第Nターン（第M手）」
    public string Label => $"第{TurnNumber}ターン（第{MoveNumber}手）";
}
