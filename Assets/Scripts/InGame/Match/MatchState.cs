using System.Collections.Generic;

// 駒が倒された方法（Prototype の effects.js の capture / vanish）
public enum DefeatKind
{
    Capture, // 取り：エナジー配分あり・取った側の持ち駒になる
    Vanish,  // 消失：エナジー・持ち駒化なし（爆発など）
}

// 倒された初期配置の駒の記録（超常魔法「アンデッド・パラダイス」で使う予定）
public struct FallenPieceRecord
{
    public PieceType Type;
    public DefeatKind Kind;
    public int TurnNumber; // 倒されたターン
}

// 対局の状態のうち、新しいルール（ターン・エナジー・倒された初期配置の駒）の分。
// MonoBehaviour に依存しないので、後で棋譜・CPU・オンラインの同期に使える。
// 盤面と持ち駒は今のところ GameBoard・Player が持っている
public class MatchState
{
    public TurnState Turn { get; }
    public EnergyState Energy { get; }

    private readonly List<FallenPieceRecord>[] fallenInitial = { new List<FallenPieceRecord>(), new List<FallenPieceRecord>() };

    public MatchState(PlayerType firstPlayer)
    {
        Turn = new TurnState(firstPlayer);
        Energy = new EnergyState();
    }

    // 倒された自分の初期配置の駒（持ち主ごと。取り・消失どちらも）
    public IReadOnlyList<FallenPieceRecord> FallenInitial(PlayerType owner)
    {
        return fallenInitial[(int)owner];
    }

    public void RecordFallenInitial(PlayerType owner, PieceType type, DefeatKind kind)
    {
        fallenInitial[(int)owner].Add(new FallenPieceRecord { Type = type, Kind = kind, TurnNumber = Turn.TurnNumber });
    }
}
