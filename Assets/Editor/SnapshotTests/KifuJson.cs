// テストで Prototype の棋譜（Tests/SnapshotTests/PrototypeKifus/*.json）を読むための、小さな JSON の読み取り。
// Unity（.NET Standard 2.1）には System.Text.Json がないので、Unity の Test Runner と dotnet run の両方で使えるように自前で読む。
// 使い方は System.Text.Json の JsonElement に合わせた（GetProperty・TryGetProperty・EnumerateArray・GetString・GetInt32 など）
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

public enum KifuJsonKind { Object, Array, String, Number, True, False, Null }

public sealed class KifuJson
{
    public KifuJsonKind ValueKind { get; private set; }

    string text;                                  // 文字列の値・数の字面
    List<KifuJson> items;                         // 配列
    List<KeyValuePair<string, KifuJson>> props;   // オブジェクト（書かれた順）

    public static KifuJson Parse(string json)
    {
        int i = 0;
        KifuJson root = ReadValue(json, ref i);
        SkipSpace(json, ref i);
        if (i != json.Length) throw Error(json, i, "値のあとに余計な文字がある");
        return root;
    }

    // ------------------------------------------------------------
    //  値を読む
    // ------------------------------------------------------------

    public KifuJson GetProperty(string name)
    {
        if (TryGetProperty(name, out KifuJson v)) return v;
        throw new KeyNotFoundException($"JSON のオブジェクトに \"{name}\" がない");
    }

    public bool TryGetProperty(string name, out KifuJson value)
    {
        Expect(KifuJsonKind.Object);
        foreach (KeyValuePair<string, KifuJson> p in props)
        {
            if (p.Key == name)
            {
                value = p.Value;
                return true;
            }
        }
        value = null;
        return false;
    }

    public KifuJson this[int index]
    {
        get
        {
            Expect(KifuJsonKind.Array);
            return items[index];
        }
    }

    public int GetArrayLength()
    {
        Expect(KifuJsonKind.Array);
        return items.Count;
    }

    public IEnumerable<KifuJson> EnumerateArray()
    {
        Expect(KifuJsonKind.Array);
        return items;
    }

    public string GetString()
    {
        if (ValueKind == KifuJsonKind.Null) return null;
        Expect(KifuJsonKind.String);
        return text;
    }

    public int GetInt32()
    {
        Expect(KifuJsonKind.Number);
        return int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public uint GetUInt32()
    {
        Expect(KifuJsonKind.Number);
        return uint.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public long GetInt64()
    {
        Expect(KifuJsonKind.Number);
        return long.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public double GetDouble()
    {
        Expect(KifuJsonKind.Number);
        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    public bool GetBoolean()
    {
        if (ValueKind == KifuJsonKind.True) return true;
        if (ValueKind == KifuJsonKind.False) return false;
        throw new InvalidOperationException($"JSON の値が真偽値ではない（{ValueKind}）");
    }

    void Expect(KifuJsonKind kind)
    {
        if (ValueKind != kind) throw new InvalidOperationException($"JSON の値が {kind} ではない（{ValueKind}）");
    }

    // ------------------------------------------------------------
    //  文字列から読む
    // ------------------------------------------------------------

    static KifuJson ReadValue(string s, ref int i)
    {
        SkipSpace(s, ref i);
        if (i >= s.Length) throw Error(s, i, "値がない");
        char c = s[i];
        switch (c)
        {
            case '{': return ReadObject(s, ref i);
            case '[': return ReadArray(s, ref i);
            case '"': return new KifuJson { ValueKind = KifuJsonKind.String, text = ReadString(s, ref i) };
            case 't': ReadWord(s, ref i, "true"); return new KifuJson { ValueKind = KifuJsonKind.True };
            case 'f': ReadWord(s, ref i, "false"); return new KifuJson { ValueKind = KifuJsonKind.False };
            case 'n': ReadWord(s, ref i, "null"); return new KifuJson { ValueKind = KifuJsonKind.Null };
        }
        if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber(s, ref i);
        throw Error(s, i, $"読めない文字 '{c}'");
    }

    static KifuJson ReadObject(string s, ref int i)
    {
        KifuJson node = new KifuJson { ValueKind = KifuJsonKind.Object, props = new List<KeyValuePair<string, KifuJson>>() };
        i++; // {
        SkipSpace(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return node; }
        while (true)
        {
            SkipSpace(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "オブジェクトの名前がない");
            string key = ReadString(s, ref i);
            SkipSpace(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "':' がない");
            i++;
            node.props.Add(new KeyValuePair<string, KifuJson>(key, ReadValue(s, ref i)));
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return node; }
            throw Error(s, i, "',' か '}' がない");
        }
    }

    static KifuJson ReadArray(string s, ref int i)
    {
        KifuJson node = new KifuJson { ValueKind = KifuJsonKind.Array, items = new List<KifuJson>() };
        i++; // [
        SkipSpace(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return node; }
        while (true)
        {
            node.items.Add(ReadValue(s, ref i));
            SkipSpace(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return node; }
            throw Error(s, i, "',' か ']' がない");
        }
    }

    static string ReadString(string s, ref int i)
    {
        i++; // "
        StringBuilder sb = new StringBuilder();
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\')
            {
                sb.Append(c);
                continue;
            }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case '"': sb.Append('"'); break;
                case '\\': sb.Append('\\'); break;
                case '/': sb.Append('/'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'u':
                    if (i + 4 > s.Length) throw Error(s, i, "\\u のあとの 16 進数が足りない");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: throw Error(s, i - 1, $"知らないエスケープ \\{e}");
            }
        }
        throw Error(s, i, "文字列が閉じていない");
    }

    static KifuJson ReadNumber(string s, ref int i)
    {
        int start = i;
        if (s[i] == '-') i++;
        while (i < s.Length && "0123456789+-.eE".IndexOf(s[i]) >= 0) i++;
        string t = s.Substring(start, i - start);
        if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) throw Error(s, start, $"読めない数 {t}");
        return new KifuJson { ValueKind = KifuJsonKind.Number, text = t };
    }

    static void ReadWord(string s, ref int i, string word)
    {
        if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Error(s, i, $"{word} のはずの値が読めない");
        i += word.Length;
    }

    static void SkipSpace(string s, ref int i)
    {
        while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r' || s[i] == '﻿')) i++;
    }

    static FormatException Error(string s, int i, string what) => new FormatException($"JSON の {i} 文字目：{what}");
}
