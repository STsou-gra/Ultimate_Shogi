using System.Collections.Generic;
using UnityEngine;

public class Player : MonoBehaviour
{
    [Header("Player Configurations")]
    [SerializeField] private PlayerType playerType;
    [SerializeField] private SkillDeck skillDeck;
    [SerializeField] private PointManager pointManager;

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

    // コストの確認・消費処理
    public bool HasEnoughPoint(int amount)
    {
        return pointManager != null && pointManager.CurrentPoint >= amount;
    }

    public void ConsumePoint(int amount)
    {
        if (pointManager != null)
        {
            pointManager.CurrentPoint -= amount;
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
