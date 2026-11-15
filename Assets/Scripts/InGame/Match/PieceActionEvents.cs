using UnityEngine;

// 新しい駒の行動（爆発・狙撃）の通知。GameSystem が作り、演出（NewPieceFx）に渡す。
// 盤面のデータはすぐに変わるので、演出は Delay などを見て見た目の順番を合わせる

// 爆発 1 回分（火薬に駒が入った・火薬を狙撃した・爆発スキル）
public class ExplosionInfo
{
    public int X { get; }
    public int Y { get; }
    public float Delay { get; }        // 爆発の見た目を始めるまでの秒数（駒が火薬のマスに着く・狙撃の弾が届くまで）
    public GamePiece Entering { get; } // 火薬のマスに入った駒（狙撃・スキルなら null）。演出では火薬のマスまで進んでから吹き飛ぶ

    public ExplosionInfo(int x, int y, float delay, GamePiece entering)
    {
        X = x;
        Y = y;
        Delay = delay;
        Entering = entering;
    }
}

// 狙撃 1 回分
public class SnipeShot
{
    public PlayerType Owner { get; }
    public GamePiece Sniper { get; }
    public Vector2Int From { get; }
    public Vector2Int To { get; }
    public PieceType TargetType { get; }
    public bool Explosive { get; }   // 撃った駒が火薬（その場で爆発する）
    public float ImpactDelay { get; } // 照準 → ビームが着弾するまでの秒数
    public bool Guarded { get; }      // 撃った駒が盾・カウンター状態（弾は防がれ・無効にされ、「SNIPE!」は出さない）

    public SnipeShot(PlayerType owner, GamePiece sniper, Vector2Int from, Vector2Int to, PieceType targetType, bool explosive, float impactDelay, bool guarded = false)
    {
        Guarded = guarded;
        Owner = owner;
        Sniper = sniper;
        From = from;
        To = to;
        TargetType = targetType;
        Explosive = explosive;
        ImpactDelay = impactDelay;
    }
}

// 盾が「取り」を防いで割れた 1 回分（魔法「盾」）
public class ShieldBreak
{
    public GamePiece Piece { get; }     // 盾が割れた駒（盤に残る）
    public GamePiece Attacker { get; }  // 取ろうとした駒（わからなければ null）
    public PlayerType By { get; }       // 取ろうとした側
    public float Delay { get; }         // 割れる見た目を始めるまでの秒数（攻撃が届くまで）

    public ShieldBreak(GamePiece piece, GamePiece attacker, PlayerType by, float delay)
    {
        Piece = piece;
        Attacker = attacker;
        By = by;
        Delay = delay;
    }
}

// カウンター状態の駒が「取り」を無効にした 1 回分（通常魔法「カウンター」。Prototype の effects.js の counterGuard）
public class CounterGuard
{
    public GamePiece Piece { get; }     // カウンター状態だった駒（盤に残る。カウンター状態は解ける）
    public GamePiece Attacker { get; }  // 取ろうとした駒（わからなければ null。元のマスへ戻る）
    public PlayerType By { get; }       // 取ろうとした側
    public float Delay { get; }         // 無効にする見た目を始めるまでの秒数（攻撃が届くまで）

    public CounterGuard(GamePiece piece, GamePiece attacker, PlayerType by, float delay)
    {
        Piece = piece;
        Attacker = attacker;
        By = by;
        Delay = delay;
    }
}

// カウンターの反撃 1 回分（Prototype の play.js の resolveCounters の counterStrike）。
// 続く移動・取りは SpecialContext.Step の演出（MovePiece・取りの通知）で出る
public class CounterStrike
{
    public GamePiece Piece { get; }     // 反撃する駒（カウンター状態だった駒）
    public GamePiece Target { get; }    // 攻撃した駒（取り返す相手）
    public Vector2Int At { get; }       // 攻撃した駒のマス（反撃して入るマス）
    public float Time { get; }          // 反撃の見た目を始める時刻（行動の時間軸の秒数）

    public CounterStrike(GamePiece piece, GamePiece target, Vector2Int at, float time)
    {
        Piece = piece;
        Target = target;
        At = at;
        Time = time;
    }
}

// 巨人が攻撃を 1 回受けた（Prototype の effects.js の hitGiant の giantHit）。HP が 0 なら、続けて取り・消失の通知が来る
public class GiantHitInfo
{
    public GamePiece Piece { get; }     // 巨人
    public int Hp { get; }              // 攻撃を受けたあとの HP
    public string By { get; }           // 攻撃の名前（駒の名前・「爆発」「隕石」など。わからなければ null）
    public float Delay { get; }         // 見た目を始めるまでの秒数（攻撃が届くまで）

    public GiantHitInfo(GamePiece piece, int hp, string by, float delay)
    {
        Piece = piece;
        Hp = hp;
        By = by;
        Delay = delay;
    }
}
