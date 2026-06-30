using UnityEngine;
using TMPro;
using System.Collections.Generic;
using DG.Tweening;

public class HandUIManager : MonoBehaviour
{
    [Header("UI References")]
    [SerializeField] private RectTransform containerPanel; // 上から降りてくる背景パネル（持ち駒選択時に使用）
    [SerializeField] private TextMeshProUGUI activePlayerText; // 現在の手番プレイヤーを示すテキスト
    [SerializeField] private TextMeshProUGUI playerHandPieceText; // 操作している側の持ち駒をUIで表示するテキスト
    [SerializeField] private TextMeshProUGUI statusText;       // 操作ステータス表示テキスト

    [Header("Animation Settings")]
    [SerializeField] private float hiddenY = 350f;   
    [SerializeField] private float visibleY = 100f;  
    [SerializeField] private float animDuration = 0.4f;

    private static HandUIManager instance;
    public static HandUIManager Instance => instance;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else
        {
            Destroy(gameObject);
        }

        // 初期状態ではパネルを画面外（隠し位置）にセット
        if (containerPanel != null)
        {
            Vector2 pos = containerPanel.anchoredPosition;
            pos.y = hiddenY;
            containerPanel.anchoredPosition = pos;
        }
    }

    public void ShowPanel()
    {
        if (containerPanel != null)
        {
            containerPanel.DOKill();
            containerPanel.DOAnchorPosY(visibleY, animDuration).SetEase(Ease.OutBack);
        }
    }

    public void HidePanel()
    {
        if (containerPanel != null)
        {
            containerPanel.DOKill();
            containerPanel.DOAnchorPosY(hiddenY, animDuration).SetEase(Ease.InQuad);
        }
    }

    // 現在操作しているプレイヤーの持ち駒情報だけを更新して表示する
    public void UpdateActiveHandDisplay(PlayerType activePlayer, Dictionary<PieceType, int> activeHand)
    {
        if (activePlayerText != null)
        {
            activePlayerText.text = activePlayer == PlayerType.Player1 ? "【先手（プレイヤー1）の手番】" : "【後手（プレイヤー2）の手番】";
        }

        if (playerHandPieceText != null)
        {
            playerHandPieceText.text = "持ち駒： " + FormatHand(activeHand);
        }
    }

    public void SetStatusText(string text)
    {
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    private string FormatHand(Dictionary<PieceType, int> hand)
    {
        List<string> items = new List<string>();
        foreach (var kvp in hand)
        {
            if (kvp.Value > 0)
            {
                items.Add($"{GetJapaneseName(kvp.Key)} x{kvp.Value}");
            }
        }
        if (items.Count == 0) return "なし";
        return string.Join("  ", items);
    }

    public static string GetJapaneseName(PieceType type)
    {
        switch (type)
        {
            case PieceType.Pawn: return "歩";
            case PieceType.Lance: return "香";
            case PieceType.Knight: return "桂";
            case PieceType.SilverGeneral: return "銀";
            case PieceType.GoldGeneral: return "金";
            case PieceType.Bishop: return "角";
            case PieceType.Rook: return "飛";
            case PieceType.King: return "玉";
            default: return type.ToString();
        }
    }
}
