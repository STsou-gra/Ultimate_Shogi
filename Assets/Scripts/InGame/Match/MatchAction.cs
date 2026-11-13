using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 行動（Action）：プレイヤーの意思決定を表すデータ（Prototype の src/core/actions.js）。
// 盤のクリック・キー・ボタンは MatchAction を組み立てて GameSystem.Dispatch に渡す（実行は Dispatch に集める）。
// CPU・棋譜・合戦将棋型・討伐任務の土台。MonoBehaviour に依存しないプレーンなクラスで、JsonUtility でそのまま JSON にできる。
//
//  Move            from の駒を to へ動かす（promote: 成るか）
//  Drop            持ち駒 piece を to に打つ
//  Special         from の駒の必殺技（対象を選ぶ技だけ targets[0]）
//  Snipe           from の狙撃手で targets[0] の駒を撃つ
//  Magic           魔法 skill（SkillDefinition.Id）。対象を選ぶ魔法だけ targets（選んだ順）。範囲を選ぶ魔法は向き orient も
//  Forge           駒 piece を錬成する
//  ReleaseTimeStop 停止世界の「解除」
//
// hash は棋譜（ActionHistory）に残した行動にだけ入る、処理が終わった時点の盤面のハッシュ（MatchHash）。
//
// 座標は GamePiece の X・Y と同じ（x = 段：先手の前進方向が +x、y = 筋）。Prototype（x = 筋・y = 段）とは x と y が逆なので注意。
// owner は行動するプレイヤー（-1 = 手番のプレイヤー）。本将棋型では、手番と違うプレイヤーの行動は実行できない
// （合戦将棋型のように手番がない対局で使う。Prototype の policies.js の actorOf）。
// 使わない項目はそのまま（既定値）でよい。比べるときは Key を使う（使わない項目の違いは無視する）
public enum MatchActionType
{
    // 末尾に足していく（JSON・棋譜では数値で保存するため、途中に入れると既存の値がずれる）
    Move,
    Drop,
    Special,
    Snipe,
    Magic,
    Forge,
    ReleaseTimeStop,
}

// 盤のマス（JSON にしたときに {"x":0,"y":0} になるように、Vector2Int ではなく int 2 つで持つ）。(-1, -1) は「なし」
[System.Serializable]
public struct MatchSquare : System.IEquatable<MatchSquare>
{
    public int x;
    public int y;

    public MatchSquare(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public static readonly MatchSquare None = new MatchSquare(-1, -1);

    public bool IsOnBoard => BoardCells.InBounds(x, y);

    public static implicit operator Vector2Int(MatchSquare s) => new Vector2Int(s.x, s.y);
    public static implicit operator MatchSquare(Vector2Int v) => new MatchSquare(v.x, v.y);

    public bool Equals(MatchSquare other) => x == other.x && y == other.y;
    public override bool Equals(object obj) => obj is MatchSquare other && Equals(other);
    public override int GetHashCode() => x * 31 + y;
    public static bool operator ==(MatchSquare a, MatchSquare b) => a.Equals(b);
    public static bool operator !=(MatchSquare a, MatchSquare b) => !a.Equals(b);

    public override string ToString() => IsOnBoard ? $"({x}, {y})" : "(なし)";
}

[System.Serializable]
public class MatchAction
{
    public MatchActionType type;

    // 行動するプレイヤー（(int)PlayerType。-1 = 手番のプレイヤー）
    public int owner = -1;

    // Move・Special・Snipe：行動する盤上の駒のマス
    public MatchSquare from = MatchSquare.None;
    // Move：動かす先／Drop：打つマス
    public MatchSquare to = MatchSquare.None;
    // Move：成るか（成れないときは false、行き所がなくなるときは true でなければ実行できない）
    public bool promote;

    // Drop：打つ持ち駒／Forge：錬成する駒
    public PieceType piece;

    // Magic：魔法の Id（SkillDefinition.Id）
    public string skill = "";
    // Special・Snipe・Magic：選んだ対象のマス（選んだ順。対象を選ばない技・魔法は空）
    public List<MatchSquare> targets = new List<MatchSquare>();
    // Magic：範囲を選ぶ魔法の向き（0 〜 SkillEffect.OrientCount - 1）
    public int orient;

    // 棋譜（GameSystem.ActionHistory）にだけ入る：この行動の処理が終わった時点の盤面のハッシュ（MatchHash。Prototype の棋譜の hash）。
    // 処理が終わるまでは ""。行動の中身ではないので、Key・判定・実行では見ない
    public string hash = "";

    // ------------------------------------------------------------
    //  作る
    // ------------------------------------------------------------

    public static MatchAction Move(Vector2Int from, Vector2Int to, bool promote)
    {
        return new MatchAction { type = MatchActionType.Move, from = from, to = to, promote = promote };
    }

    public static MatchAction Drop(PieceType piece, Vector2Int to)
    {
        return new MatchAction { type = MatchActionType.Drop, piece = piece, to = to };
    }

    // 対象を選ばない技は target を省く
    public static MatchAction Special(Vector2Int from, Vector2Int? target = null)
    {
        MatchAction a = new MatchAction { type = MatchActionType.Special, from = from };
        if (target.HasValue) a.targets.Add(target.Value);
        return a;
    }

    public static MatchAction Snipe(Vector2Int from, Vector2Int target)
    {
        MatchAction a = new MatchAction { type = MatchActionType.Snipe, from = from };
        a.targets.Add(target);
        return a;
    }

    // 対象を選ばない魔法は targets を省く
    public static MatchAction Magic(string skillId, IEnumerable<Vector2Int> targets = null, int orient = 0)
    {
        MatchAction a = new MatchAction { type = MatchActionType.Magic, skill = skillId ?? "", orient = orient };
        if (targets != null)
        {
            foreach (Vector2Int t in targets) a.targets.Add(t);
        }
        return a;
    }

    public static MatchAction Forge(PieceType piece)
    {
        return new MatchAction { type = MatchActionType.Forge, piece = piece };
    }

    public static MatchAction ReleaseTimeStop()
    {
        return new MatchAction { type = MatchActionType.ReleaseTimeStop };
    }

    // ------------------------------------------------------------
    //  読む
    // ------------------------------------------------------------

    // 1 つ目の対象（なければ MatchSquare.None）
    public MatchSquare Target => targets != null && targets.Count > 0 ? targets[0] : MatchSquare.None;
    public int TargetCount => targets != null ? targets.Count : 0;

    // owner を PlayerType で（手番のプレイヤーの行動なら null）
    public PlayerType? Owner
    {
        get { return owner == (int)PlayerType.Player1 || owner == (int)PlayerType.Player2 ? (PlayerType)owner : (PlayerType?)null; }
        set { owner = value.HasValue ? (int)value.Value : -1; }
    }

    // この行動の写し（targets も別のリストにする）
    public MatchAction Clone()
    {
        MatchAction a = (MatchAction)MemberwiseClone();
        a.targets = targets != null ? new List<MatchSquare>(targets) : new List<MatchSquare>();
        return a;
    }

    // 行動を比べるための文字列（Prototype の actionKey）。同じ文字列なら同じ行動になる。
    // その種類の行動で使わない項目の違いは無視する（例：Move の piece、Forge の from）
    public string Key
    {
        get
        {
            StringBuilder sb = new StringBuilder();
            sb.Append(type);
            switch (type)
            {
                case MatchActionType.Move:
                    sb.Append('|').Append(Sq(from)).Append('|').Append(Sq(to)).Append('|').Append(promote ? '+' : '-');
                    break;
                case MatchActionType.Drop:
                    sb.Append('|').Append(piece).Append('|').Append(Sq(to));
                    break;
                case MatchActionType.Special:
                case MatchActionType.Snipe:
                    sb.Append('|').Append(Sq(from)).Append('|').Append(TargetsKey());
                    break;
                case MatchActionType.Magic:
                    sb.Append('|').Append(skill).Append('|').Append(TargetsKey()).Append('|').Append(orient);
                    break;
                case MatchActionType.Forge:
                    sb.Append('|').Append(piece);
                    break;
            }
            if (owner >= 0) sb.Append("|p").Append(owner);
            return sb.ToString();
        }
    }

    static string Sq(MatchSquare s) => $"{s.x},{s.y}";

    string TargetsKey()
    {
        if (targets == null || targets.Count == 0) return "";
        StringBuilder sb = new StringBuilder();
        for (int i = 0; i < targets.Count; i++)
        {
            if (i > 0) sb.Append(';');
            sb.Append(Sq(targets[i]));
        }
        return sb.ToString();
    }

    // ログ用の短い説明（例：「移動 (2, 6) → (3, 6)」）
    public override string ToString()
    {
        string who = Owner.HasValue ? $"[{(Owner.Value == PlayerType.Player1 ? "先手" : "後手")}] " : "";
        switch (type)
        {
            case MatchActionType.Move: return $"{who}移動 {from} → {to}{(promote ? " 成" : "")}";
            case MatchActionType.Drop: return $"{who}打つ {PieceNames.Full(piece)} {to}";
            case MatchActionType.Special: return $"{who}必殺技 {from}" + (TargetCount > 0 ? $" → {Target}" : "");
            case MatchActionType.Snipe: return $"{who}狙撃 {from} → {Target}";
            case MatchActionType.Magic: return $"{who}魔法 {skill}" + (TargetCount > 0 ? $" → {TargetsText()}" : "") + (orient != 0 ? $"（向き {orient}）" : "");
            case MatchActionType.Forge: return $"{who}錬成 {PieceNames.Full(piece)}";
            case MatchActionType.ReleaseTimeStop: return $"{who}停止世界の解除";
            default: return $"{who}{type}";
        }
    }

    string TargetsText()
    {
        List<string> parts = new List<string>();
        foreach (MatchSquare t in targets) parts.Add(t.ToString());
        return string.Join(" ", parts);
    }

    // ------------------------------------------------------------
    //  JSON（JsonUtility。棋譜・通信・CPU のログ用）
    // ------------------------------------------------------------

    public string ToJson()
    {
        return JsonUtility.ToJson(this);
    }

    // 読めなければ null
    public static MatchAction FromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            MatchAction a = JsonUtility.FromJson<MatchAction>(json);
            if (a != null && a.targets == null) a.targets = new List<MatchSquare>();
            if (a != null && a.skill == null) a.skill = "";
            if (a != null && a.hash == null) a.hash = "";
            return a;
        }
        catch (System.ArgumentException)
        {
            return null;
        }
    }
}
