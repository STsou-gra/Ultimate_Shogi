using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using TMPro;

// 結果画面（GameOver シーン）の棋譜の部品（Prototype の GameOverScene の「棋譜を保存」）。
//  ・「棋譜を保存（N 手）」… 終わった対局の棋譜（KifuSession.Current）を JSON ファイルに保存する
//  ・「棋譜を読み込んで再生」… 棋譜を選んで、Board シーンで最初から再生する
//  ・直前に棋譜を再生していたら、その結果（全手が一致したか・何手目でずれたか）も出す
// GameResultDisplay（結果画面の勝者テキスト）が自動で作る。画面の右下に置く
public class KifuGameOverPanel : MonoBehaviour
{
    const string MatchSceneName = "Board";

    private TMP_FontAsset font;
    private TextMeshProUGUI messageText;

    public static void Ensure()
    {
        if (FindAnyObjectByType<KifuGameOverPanel>() != null) return;
        new GameObject("KifuGameOverPanel").AddComponent<KifuGameOverPanel>();
    }

    void Start()
    {
        font = RuntimeUi.LoadFont();
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.CreateCanvas("KifuGameOverCanvas", transform, 100);

        const float w = 520f, pad = 24f;
        Image box = RuntimeUi.CreatePanel("Box", canvas.transform, RuntimeUi.WithAlpha(RuntimeUi.BoxColor, 0.9f), true);
        RuntimeUi.Place(box.rectTransform, new Vector2(1f, 0f), new Vector2(24f, 24f), new Vector2(w, 330f));

        KifuFile k = KifuSession.Current;
        string head = k != null ? $"棋譜：{k.Count} 手" : (KifuSession.NoKifuReason ?? "この対局は棋譜を記録していません");
        TextMeshProUGUI title = RuntimeUi.CreateText("Title", box.transform, head, 26f, RuntimeUi.GoldColor, TextAlignmentOptions.Center, font);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, pad), new Vector2(w - pad * 2f, 40f));

        Button save = RuntimeUi.CreateButton("SaveKifu", box.transform, RuntimeUi.Hex(0x2e7d32), "棋譜を保存（.json）", 26f, font, out _);
        RuntimeUi.Place((RectTransform)save.transform, new Vector2(0.5f, 1f), new Vector2(0f, pad + 52f), new Vector2(w - pad * 2f, 60f));
        save.interactable = k != null && k.Count > 0;
        save.onClick.AddListener(() =>
        {
            SetMessage(KifuSession.SaveCurrent());
            RuntimeUi.ClearUiSelection();
        });

        Button replay = RuntimeUi.CreateButton("ReplayKifu", box.transform, RuntimeUi.Hex(0x1565c0), "棋譜を読み込んで再生", 26f, font, out _);
        RuntimeUi.Place((RectTransform)replay.transform, new Vector2(0.5f, 1f), new Vector2(0f, pad + 124f), new Vector2(w - pad * 2f, 60f));
        replay.onClick.AddListener(() =>
        {
            SetMessage(ReplayFromFile());
            RuntimeUi.ClearUiSelection();
        });

        messageText = RuntimeUi.CreateText("Message", box.transform, KifuSession.LastReplayResult ?? "", 20f, RuntimeUi.Hex(0xffe082), TextAlignmentOptions.Center, font);
        RuntimeUi.Place(messageText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, pad + 196f), new Vector2(w - pad * 2f, 90f));
    }

    void SetMessage(string message)
    {
        if (messageText != null && message != null) messageText.text = message;
    }

    // 棋譜を選んで、対局のシーンで最初から再生する。戻り値：始められなかった理由（キャンセルなら null）
    static string ReplayFromFile()
    {
        string error;
        string text = KifuFileDialog.OpenJson("棋譜を読み込んで再生", out error);
        if (text == null) return error;
        KifuFile kifu;
        try
        {
            kifu = KifuFile.Parse(text);
        }
        catch (KifuFormatException e)
        {
            return $"棋譜を読み込めません：{e.Message}";
        }
        SkillDeck[] decks = KifuSession.ResolveDecks(kifu.loadouts,
            new[] { DeckTransferManager.Player1SelectedDeck, DeckTransferManager.Player2SelectedDeck }, out error);
        if (decks == null) return $"棋譜を再生できません：{error}";
        KifuSession.RequestReplay(kifu, decks, 1f);
        SceneManager.LoadScene(MatchSceneName);
        return null;
    }
}
