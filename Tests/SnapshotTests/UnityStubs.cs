// 実行できるテスト用の、UnityEngine の最小限の代わり（Unity の外で動かすため）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace UnityEngine
{
    public struct Vector2Int : IEquatable<Vector2Int>
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public bool Equals(Vector2Int o) => x == o.x && y == o.y;
        public override bool Equals(object o) => o is Vector2Int v && Equals(v);
        public override int GetHashCode() => x * 31 + y;
        public static bool operator ==(Vector2Int a, Vector2Int b) => a.Equals(b);
        public static bool operator !=(Vector2Int a, Vector2Int b) => !a.Equals(b);
        public override string ToString() => $"({x}, {y})";
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public static class Mathf
    {
        public static int Abs(int v) => Math.Abs(v);
        public static float Abs(float v) => Math.Abs(v);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public static class Debug
    {
        public static void Log(object m) { }
        public static void LogWarning(object m) => Console.WriteLine("WARN " + m);
    }
    // JsonUtility と同じく public なフィールドだけを書き出す（プロパティは書き出さない）
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            IncludeFields = true,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { FieldsOnly } },
        };
        static void FieldsOnly(JsonTypeInfo info)
        {
            if (info.Kind != JsonTypeInfoKind.Object) return;
            for (int i = info.Properties.Count - 1; i >= 0; i--)
            {
                if (!(info.Properties[i].AttributeProvider is FieldInfo f) || f.IsNotSerialized) info.Properties.RemoveAt(i);
            }
        }
        public static string ToJson(object o, bool pretty = false) => JsonSerializer.Serialize(o, o.GetType(), Options);
        public static T FromJson<T>(string json) { try { return JsonSerializer.Deserialize<T>(json, Options); } catch (JsonException e) { throw new ArgumentException("JSON parse error: " + e.Message); } } // Unity も ArgumentException
    }
}

public enum PieceType { King, Rook, Bishop, GoldGeneral, SilverGeneral, Knight, Lance, Pawn, Gunpowder, Sniper }
public enum PlayerType { Player1, Player2 }

// GameObject の駒の代わり：動きの形は main の各駒のクラス（oracle）で判定する
public class GamePiece
{
    public PieceType type;
    public PlayerType player;
    public int X { get; set; }
    public int Y { get; set; }
    public bool isPromoted, isUndead, hasShield, hasPhalanx, IsInitial;
    public int moveCount;
    public BoardCells Cells { get; set; }
    public bool CanMove(int toX, int toY)
    {
        if (Cells != null && Cells.IsBroken(toX, toY)) return false;
        OracleBase o = Oracles.Make(type);
        o.X = X; o.Y = Y; o.player = player; o.isPromoted = isPromoted;
        return o.CanMoveByPattern(toX, toY);
    }
}

public static class Oracles
{
    public static OracleBase Make(PieceType t)
    {
        switch (t)
        {
            case PieceType.Pawn: return new OraclePawn();
            case PieceType.Lance: return new OracleLance();
            case PieceType.Knight: return new OracleKnight();
            case PieceType.SilverGeneral: return new OracleSilverGeneral();
            case PieceType.GoldGeneral: return new OracleGoldGeneral();
            case PieceType.Bishop: return new OracleBishop();
            case PieceType.Rook: return new OracleRook();
            case PieceType.King: return new OracleKing();
            case PieceType.Gunpowder: return new OracleGunpowder();
            default: return new OracleSniper();
        }
    }
}

// GameBoard の代わり（ルールの判定が読む分だけ）
public class GameBoard
{
    readonly GamePiece[] boardData = new GamePiece[81];
    public BoardCells Cells { get; } = new BoardCells();
    public GamePiece[] GetBoardData() => boardData;
    public GamePiece GetPieceAt(int x, int y) { int i = GameHelper.CalcPanelNum(x, y); return i < 0 ? null : boardData[i]; }
    public bool HasPieceAt(int x, int y) => GetPieceAt(x, y) != null;
    public void Put(GamePiece p) { boardData[GameHelper.CalcPanelNum(p.X, p.Y)] = p; p.Cells = Cells; }
}
