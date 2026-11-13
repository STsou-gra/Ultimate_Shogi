// エナジー（Prototype の state.energy / effects.js の gainEnergy / spendEnergy）
// 値はここに持ち、PointManager は表示だけを受け持つ（Player.BindEnergy で繋ぐ）
public class EnergyState
{
    private readonly int[] values = new int[2];

    // 上限（0 = 上限なし）
    public int Max { get; }

    // エナジーが変わった（プレイヤー・変わった後の値・増減）
    public event System.Action<PlayerType, int, int> Changed;

    // Changed の通知中、その変化が「目立たせない」もの（合戦将棋型の毎秒のエナジー。「+1 エナジー」のポップアップを出さない）か
    public bool IsQuietChange { get; private set; }

    public EnergyState(int max = MatchConfig.EnergyMax)
    {
        Max = max;
    }

    public int Get(PlayerType player)
    {
        return values[(int)player];
    }

    public bool HasEnough(PlayerType player, int amount)
    {
        return Get(player) >= amount;
    }

    // エナジーを加える（0 以下なら何もしない）。上限があれば超えた分は捨てる。実際に増えた量を返す。
    // quiet なら Changed の通知中 IsQuietChange が true（表示は変えるが、ポップアップは出さない）
    public int Gain(PlayerType player, int amount, bool quiet = false)
    {
        if (amount <= 0) return 0;
        int before = Get(player);
        int after = before + amount;
        if (Max > 0 && after > Max) after = System.Math.Max(before, Max);
        IsQuietChange = quiet;
        try
        {
            return Set(player, after);
        }
        finally
        {
            IsQuietChange = false;
        }
    }

    // エナジーを使う（足りなければ使わずに false）
    public bool Spend(PlayerType player, int amount)
    {
        if (amount <= 0) return true;
        if (!HasEnough(player, amount)) return false;
        Set(player, Get(player) - amount);
        return true;
    }

    // 書き出した局面から戻す（GameSystem.ImportState。Changed は出さないので、表示は Player.BindEnergy で合わせ直す）
    public void Restore(PlayerType player, int value)
    {
        values[(int)player] = value;
    }

    int Set(PlayerType player, int value)
    {
        int delta = value - values[(int)player];
        if (delta == 0) return 0;
        values[(int)player] = value;
        Changed?.Invoke(player, value, delta);
        return delta;
    }
}
