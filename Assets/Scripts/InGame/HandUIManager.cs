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
    [SerializeField] private GameObject darkOverlay; // 画面を暗くする半透明パネル
    [SerializeField] private TextMeshProUGUI turnText; // 「第Nターン（第M手）」（未設定なら手番テキストの先頭に付ける）

    [Header("Animation Settings")]
    [SerializeField] private float hiddenY = 350f;
    [SerializeField] private float visibleY = 100f;
    [SerializeField] private float animDuration = 0.4f;

    private static HandUIManager instance;
    public static HandUIManager Instance => instance;
    public TextMeshProUGUI ActivePlayerText => activePlayerText;

    private string turnLabel = "";        // 「第Nターン（第M手）」
    private string activePlayerLabel = ""; // 「【先手（プレイヤー1）の手番】」

    // 合戦将棋型（手番がない）：手番の代わりに「操作中の席」と出す
    public bool SeatMode { get; set; }

    // 案内文を出す前に通す関数（合戦将棋型：キーの名前を席のキーに読み替え、席の印を付ける。null を返したら出さない）。
    // GameSystem が対局の準備で設定する
    public static System.Func<string, string> StatusFilter;

    // 出した案内文（フィルターを通した後）。HUD などが読む
    public string StatusText => statusText != null ? statusText.text : "";

    void OnDestroy()
    {
        if (instance == this) StatusFilter = null;
    }

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
        if (darkOverlay != null)
        {
            darkOverlay.SetActive(false);
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
        if (SeatMode) activePlayerLabel = activePlayer == PlayerType.Player1 ? "【操作中：先手（プレイヤー1）】" : "【操作中：後手（プレイヤー2）】";
        else activePlayerLabel = activePlayer == PlayerType.Player1 ? "【先手（プレイヤー1）の手番】" : "【後手（プレイヤー2）の手番】";
        RefreshTurnTexts();

        if (playerHandPieceText != null)
        {
            playerHandPieceText.text = "持ち駒： " + FormatHand(activeHand);
        }
    }

    // ターン数・手数の表示を更新する
    public void SetTurnInfo(string label)
    {
        turnLabel = label ?? "";
        RefreshTurnTexts();
    }

    private void RefreshTurnTexts()
    {
        if (turnText != null)
        {
            turnText.text = turnLabel;
            if (activePlayerText != null) activePlayerText.text = activePlayerLabel;
        }
        else if (activePlayerText != null)
        {
            activePlayerText.text = string.IsNullOrEmpty(turnLabel) ? activePlayerLabel : $"{turnLabel}　{activePlayerLabel}";
        }
    }

    public void SetStatusText(string text)
    {
        if (StatusFilter != null)
        {
            text = StatusFilter(text);
            if (text == null) return;
        }
        if (statusText != null)
        {
            statusText.text = text;
        }
    }

    private string FormatHand(Dictionary<PieceType, int> hand)
    {
        List<string> items = new List<string>();
        foreach (PieceType type in PieceRules.HandOrder)
        {
            int count;
            if (hand.TryGetValue(type, out count) && count > 0)
            {
                items.Add($"{GetJapaneseName(type)} x{count}");
            }
        }
        if (items.Count == 0) return "なし";
        return string.Join("  ", items);
    }

    // 駒の短い名前（Match/PieceNames.cs。ルール説明などからも同じ名前を使う）
    public static string GetJapaneseName(PieceType type)
    {
        return PieceNames.Short(type);
    }
}
