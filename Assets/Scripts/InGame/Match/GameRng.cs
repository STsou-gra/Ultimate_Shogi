using System.Collections.Generic;

// 対局の結果に関わる乱数（シード付き）。Prototype の src/core/rng.js の createRng（mulberry32）。
//
// 天変地異・世界の終わり・アンデッドなど、乱数で盤面が変わるものは必ずこれを使う。
// 同じシードから同じ順に引けば同じ結果になるので、シードを棋譜に残せば棋譜の再生で同じ盤面になり、
// オンライン対戦でも両者が同じシードを持てば同じ結果になる（どちらも今後対応）。
// 演出（粒の飛び方・ひびの形など、結果に関わらないもの）には UnityEngine.Random を使い、これは使わない
// （演出で引くと、演出の有無で対局の結果が変わってしまうため）。
//
// MonoBehaviour に依存しない（棋譜の再生・CPU・サーバーからも使える）。対局の状態 MatchState.Rng が持つ
public class GameRng
{
    // 対局の開始時に決めたシード（棋譜に残す値）
    public uint Seed { get; }

    // 内部状態（Prototype の state.rng.s）。棋譜の途中から再開するときに使う
    public uint State { get; private set; }

    public GameRng(uint seed)
    {
        Seed = seed;
        State = seed;
    }

    // 途中の状態から作る（保存した State から再開する）
    public GameRng(uint seed, uint state)
    {
        Seed = seed;
        State = state;
    }

    // 内部状態を戻す（局面の読み込み：MatchSnapshot.rngState）
    public void Restore(uint state)
    {
        State = state;
    }

    // 新しい対局のシード（32 ビット。暗号用の乱数で作る）
    public static uint NewSeed()
    {
        byte[] bytes = new byte[4];
        using (System.Security.Cryptography.RandomNumberGenerator rng = System.Security.Cryptography.RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return System.BitConverter.ToUInt32(bytes, 0);
    }

    // 0 以上 1 未満の数（mulberry32。Prototype の next と同じ値の列になる）
    public double Next()
    {
        unchecked
        {
            State += 0x6d2b79f5u;
            uint t = State;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return (t ^ (t >> 14)) / 4294967296.0;
        }
    }

    // 0 以上 max 未満の整数（max が 0 以下なら 0）
    public int Range(int max)
    {
        if (max <= 0) return 0;
        int i = (int)(Next() * max);
        return i < max ? i : max - 1;
    }

    // 配列・リストから 1 つ選ぶ（空なら既定値）
    public T Pick<T>(IReadOnlyList<T> list)
    {
        if (list == null || list.Count == 0) return default(T);
        return list[Range(list.Count)];
    }

    // シャッフルした新しいリストを返す（Fisher–Yates。Prototype の shuffle と同じ順に引く）。元のリストは変えない
    public List<T> Shuffle<T>(IEnumerable<T> source)
    {
        List<T> a = new List<T>(source);
        for (int i = a.Count - 1; i > 0; i--)
        {
            int j = (int)(Next() * (i + 1));
            T tmp = a[i];
            a[i] = a[j];
            a[j] = tmp;
        }
        return a;
    }
}
