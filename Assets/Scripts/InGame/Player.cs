using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    [Header("Player Configurations")]
    [SerializeField] private PlayerType playerType;
    [SerializeField] private SkillDeck skillDeck;
    [SerializeField] private PointManager pointManager; // エナジーの表示

    // エナジーの値（GameSystem が BindEnergy で渡す。値は MatchState 側に持つ）
    private EnergyState energy;

    // 持ち駒のデータ
    private Dictionary<PieceType, int> hand = new Dictionary<PieceType, int>()
    {
        { PieceType.Pawn, 0 },
        { PieceType.Lance, 0 },
        { PieceType.Knight, 0 },
        { PieceType.SilverGeneral, 0 },
        { PieceType.GoldGeneral, 0 },
        { PieceType.Bishop, 0 },
        { PieceType.Rook, 0 }
    };

    public PlayerType PlayerType => playerType;
    public SkillDeck SkillDeck => skillDeck;
    public PointManager PointManager => pointManager;
    public Dictionary<PieceType, int> Hand => hand;

    // エナジーの値と表示を繋ぐ
    public void BindEnergy(EnergyState state)
    {
        if (energy != null) energy.Changed -= OnEnergyChanged;
        energy = state;
        if (energy == null) return;
        energy.Changed += OnEnergyChanged;
        if (pointManager != null) pointManager.CurrentPoint = energy.Get(playerType);
    }

    void OnDestroy()
    {
        if (energy != null) energy.Changed -= OnEnergyChanged;
    }

    void OnEnergyChanged(PlayerType owner, int value, int delta)
    {
        if (owner != playerType || pointManager == null) return;
        pointManager.CurrentPoint = value;
    }

    // エナジー（旧ポイント）の確認・消費・加算
    public int Energy => energy != null ? energy.Get(playerType) : (pointManager != null ? pointManager.CurrentPoint : 0);

    public bool HasEnoughPoint(int amount)
    {
        return Energy >= amount;
    }

    public void ConsumePoint(int amount)
    {
        if (energy != null)
        {
            energy.Spend(playerType, amount);
        }
        else if (pointManager != null)
        {
            pointManager.CurrentPoint -= amount;
        }
    }

    public void AddPoint(int amount)
    {
        if (energy != null)
        {
            energy.Gain(playerType, amount);
        }
        else if (pointManager != null)
        {
            pointManager.AddPoint(amount);
        }
    }

    // 持ち駒の追加・削減処理
    public void AddPieceToHand(PieceType type)
    {
        if (type == PieceType.King) return;
        if (hand.ContainsKey(type))
        {
            hand[type]++;
        }
    }

    public void RemovePieceFromHand(PieceType type)
    {
        if (hand.ContainsKey(type) && hand[type] > 0)
        {
            hand[type]--;
        }
    }

    public void SetSkillDeck(SkillDeck deck)
    {
        skillDeck = deck;
    }
}
