using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using DG.Tweening;
using TMPro;

// 棋譜メニュー（Prototype の src/ui/kifuMenu.js）と、棋譜の再生中の表示（Game.js の refreshReplayHud）。
//
//  ・画面の左下（「ルール」ボタンの上）に「棋譜 [K]」ボタン。押す（K キー）と、対局の上にメニューを重ねて開く
//      この対局の棋譜を保存（.json）／棋譜を読み込んで再生／棋譜を読み込んで早送り再生（4倍速）／
//      今の局面を書き出す（.json）／局面を読み込んで続きから指す／閉じる
//  ・閉じる：K・Esc・Space・「閉じる」ボタン・枠の外をクリック。開いている間は対局の操作を受け付けない（IsBlockingInput）。
//    棋譜の再生も、開いている間は止まる
//  ・再生中は画面の上に「棋譜の再生中 N / M 手」と速さ・「再生を止める」ボタン。終わったら結果を数秒出す
// 中身の処理は GameSystem.Kifu.cs。シーンに置かなくても GameSystem が自動で追加する
public class KifuMenuView : MonoBehaviour
{
    [SerializeField] private GameSystem gameSystem;
    [SerializeField] private Key toggleKey = Key.K;

    [Header("Layout (基準解像度 1920×1080 でのピクセル)")]
    [SerializeField] private Vector2 buttonOffset = new Vector2(20f, 96f); // 左下からの距離（「ルール」ボタンの上）
    [SerializeField] private Vector2 buttonSize = new Vector2(230f, 64f);

    [Header("Sorting")]
    [SerializeField] private int buttonSortingOrder = 60;    // カットイン（500）より奥
    [SerializeField] private int hudSortingOrder = 450;
    [SerializeField] private int overlaySortingOrder = 1000; // ほかの UI・カットインより手前

    private static KifuMenuView active;

    // 開いている間（と閉じた直後の 1 フレーム）は対局の操作を止める（RulesOverlayView と同じ）
    public static bool IsBlockingInput
    {
        get
        {
            if (active == null) return false;
            return active.isOpen || Time.frameCount <= active.closedFrame + 1;
        }
    }

    public static bool IsOpenAny => active != null && active.isOpen;

    private TMP_FontAsset font;
    private Canvas overlayCanvas;
    private CanvasGroup overlayGroup;
    private TextMeshProUGUI infoText;
    private TextMeshProUGUI messageText;
    private Button saveButton, replayButton, fastButton, exportButton, importButton;

    private Canvas hudCanvas;
    private RectTransform replayBar;
    private TextMeshProUGUI replayText;
    private RectTransform toast;
    private TextMeshProUGUI toastText;
    private float toastUntil;

    private bool isOpen;
    private int closedFrame = -10;

    void Awake()
    {
        active = this;
    }

    void Start()
    {
        if (gameSystem == null) gameSystem = FindAnyObjectByType<GameSystem>();
        font = RuntimeUi.LoadFont();
        RuntimeUi.EnsureEventSystem();
        BuildButton();
        BuildOverlay();
        BuildHud();
        overlayCanvas.gameObject.SetActive(false);
        if (gameSystem != null)
        {
            gameSystem.ReplayEnded += OnReplayEnded;
            gameSystem.StateImported += OnStateImported;
        }
    }

    void OnDestroy()
    {
        if (active == this) active = null;
        if (gameSystem != null)
        {
            gameSystem.ReplayEnded -= OnReplayEnded;
            gameSystem.StateImported -= OnStateImported;
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && overlayCanvas != null)
        {
            if (!isOpen)
            {
                if (keyboard[toggleKey].wasPressedThisFrame && !RulesOverlayView.IsBlockingInput) Open();
            }
            else if (keyboard[toggleKey].wasPressedThisFrame || keyboard.escapeKey.wasPressedThisFrame || keyboard.spaceKey.wasPressedThisFrame)
            {
                Close();
            }
        }
        UpdateHud();
    }

    // ------------------------------------------------------------
    //  開く・閉じる
    // ------------------------------------------------------------
    public void Toggle()
    {
        if (isOpen) Close();
        else Open();
    }

    public void Open()
    {
        if (isOpen || overlayCanvas == null || gameSystem == null) return;
        isOpen = true;
        overlayCanvas.gameObject.SetActive(true);
        messageText.text = "";
        Refresh();
        overlayGroup.DOKill();
        overlayGroup.alpha = 0f;
        overlayGroup.DOFade(1f, 0.2f).SetUpdate(true).SetLink(overlayCanvas.gameObject);
        RuntimeUi.ClearUiSelection();
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        closedFrame = Time.frameCount;
        overlayGroup.DOKill();
        overlayGroup.DOFade(0f, 0.15f).SetUpdate(true).SetLink(overlayCanvas.gameObject)
            .OnComplete(() => { if (!isOpen) overlayCanvas.gameObject.SetActive(false); });
        RuntimeUi.ClearUiSelection();
    }

    // ボタンの押せる／押せないと説明を、今の対局に合わせる
    void Refresh()
    {
        string saveReason;
        bool canSave = gameSystem.CanSaveKifu(out saveReason);
        KifuFile k = gameSystem.Kifu;
        string head = k != null ? $"この対局：{k.Count} 手" : (KifuSession.NoKifuReason ?? "この対局は棋譜を記録していません");
        if (k != null && !canSave && saveReason != null) head += $"（{saveReason}）";
        if (k != null && k.debugEdited) head += "\n<color=#ff8a80>デバッグキーでマスを変えたので、この棋譜は再生しても同じ盤面になりません</color>";

        infoText.text = head + "\n" +
            "<size=85%><color=#bdbdbd>棋譜には、両者の魔法スキルの編成・乱数のシード・すべての行動が入ります。\n" +
            "読み込んだ棋譜は最初から再生し、終わるとその局面から続けて指せます（続きも同じ棋譜に記録）。\n" +
            "局面の書き出しは今の盤面だけを保存し、読み込むとそこから続けて指せます（棋譜は記録しません）。</color></size>";

        bool idle = !gameSystem.IsReplaying;
        string exportReason;
        saveButton.interactable = canSave;
        replayButton.interactable = idle;
        fastButton.interactable = idle;
        exportButton.interactable = gameSystem.CanExportState(out exportReason);
        importButton.interactable = idle;
    }

    void ShowMessage(string message)
    {
        messageText.text = message ?? "";
        Refresh();
        RuntimeUi.ClearUiSelection();
    }

    // 読み込んで再生・局面の読み込みは、成功するとシーンを読み込み直す（このメニューも消える）
    void OnSave() => ShowMessage(gameSystem.SaveKifu());
    void OnReplay(float speed) => ShowMessage(gameSystem.LoadKifuAndReplay(speed));
    void OnExport() => ShowMessage(gameSystem.SaveState());
    void OnImport() => ShowMessage(gameSystem.LoadState());

    // ------------------------------------------------------------
    //  再生中の表示・結果
    // ------------------------------------------------------------
    void UpdateHud()
    {
        if (gameSystem == null || replayBar == null) return;
        GameSystem.ReplayProgress r = gameSystem.Replay;
        bool show = r != null;
        if (replayBar.gameObject.activeSelf != show) replayBar.gameObject.SetActive(show);
        if (show)
        {
            string speed = r.Speed > 1f ? $"（{r.Speed:0}倍速）" : "（等速）";
            string note = r.DebugEdited ? "<color=#ff8a80>デバッグキーで変えた棋譜（ずれることがある）</color>" : "<color=#bdbdbd>操作はできません</color>";
            replayText.text = $"棋譜の再生中　{r.Played} / {r.Total} 手{speed}　<size=75%>{note}</size>";
        }
        if (toast != null && toast.gameObject.activeSelf && Time.unscaledTime > toastUntil) toast.gameObject.SetActive(false);
    }

    void OnReplayEnded(GameSystem.ReplayResult result)
    {
        ShowToast(result.Message, result.Ok || result.Stopped ? RuntimeUi.Hex(0x1b5e20) : RuntimeUi.Hex(0xb71c1c));
    }

    void OnStateImported(bool same)
    {
        ShowToast(same ? "局面を読み込んだ。この局面から続けて指せる（棋譜は記録しない）"
                       : "局面を読み込んだが、盤面のハッシュが書き出したときと違う（Console を確認）",
                  same ? RuntimeUi.Hex(0x1b5e20) : RuntimeUi.Hex(0xb71c1c));
    }

    void ShowToast(string message, Color color)
    {
        if (toast == null) return;
        toast.GetComponent<Image>().color = RuntimeUi.WithAlpha(color, 0.95f);
        toastText.text = message;
        toast.gameObject.SetActive(true);
        toastUntil = Time.unscaledTime + 8f;
    }

    // ------------------------------------------------------------
    //  UI を作る
    // ------------------------------------------------------------
    void BuildButton()
    {
        Canvas canvas = RuntimeUi.CreateCanvas("KifuButtonCanvas", transform, buttonSortingOrder);
        Button button = RuntimeUi.CreateButton("KifuButton", canvas.transform, RuntimeUi.GrayButton, $"棋譜  [{toggleKey}]", 28f, font, out _);
        RuntimeUi.Place((RectTransform)button.transform, Vector2.zero, buttonOffset, buttonSize);
        Outline line = button.gameObject.AddComponent<Outline>();
        line.effectColor = RuntimeUi.WithAlpha(RuntimeUi.GoldColor, 0.8f);
        line.effectDistance = new Vector2(2f, -2f);
        button.onClick.AddListener(Toggle);
    }

    void BuildOverlay()
    {
        overlayCanvas = RuntimeUi.CreateCanvas("KifuOverlayCanvas", transform, overlaySortingOrder);
        RectTransform root = (RectTransform)overlayCanvas.transform;
        overlayGroup = overlayCanvas.gameObject.AddComponent<CanvasGroup>();

        // 暗幕（クリックで閉じる）
        Button dim = RuntimeUi.CreateButton("Dim", root, RuntimeUi.DimColor, "", 10f, font, out _);
        RuntimeUi.Stretch((RectTransform)dim.transform, new Vector2(-10f, -10f), new Vector2(10f, 10f));
        ColorBlock dc = dim.colors;
        dc.highlightedColor = Color.white;
        dc.pressedColor = Color.white;
        dim.colors = dc;
        dim.onClick.AddListener(Close);

        // 枠（クリックしても閉じない）
        const float boxW = 980f, boxH = 860f, pad = 34f, bw = 760f, bh = 70f, gapY = 16f;
        Image box = RuntimeUi.CreatePanel("Box", root, RuntimeUi.BoxColor, true);
        RuntimeUi.Place(box.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(boxW, boxH));

        TextMeshProUGUI title = RuntimeUi.CreateText("Title", box.transform, "棋譜", 44f, RuntimeUi.GoldColor, TextAlignmentOptions.Center, font);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, pad), new Vector2(boxW - pad * 2f, 56f));

        infoText = RuntimeUi.CreateText("Info", box.transform, "", 25f, Hex(0xf5f5f5), TextAlignmentOptions.Center, font);
        infoText.richText = true;
        infoText.lineSpacing = 8f;
        RuntimeUi.Place(infoText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, pad + 70f), new Vector2(boxW - pad * 2f, 170f));

        float y = pad + 70f + 180f;
        saveButton = AddMenuButton(box.transform, "この対局の棋譜を保存（.json）", Hex(0x2e7d32), ref y, bw, bh, gapY, OnSave);
        replayButton = AddMenuButton(box.transform, "棋譜を読み込んで再生", Hex(0x1565c0), ref y, bw, bh, gapY, () => OnReplay(1f));
        fastButton = AddMenuButton(box.transform, $"棋譜を読み込んで早送り再生（{GameSystem.ReplayFastSpeed:0}倍速）", Hex(0x1565c0), ref y, bw, bh, gapY, () => OnReplay(GameSystem.ReplayFastSpeed));
        exportButton = AddMenuButton(box.transform, "今の局面を書き出す（.json）", Hex(0x6a1b9a), ref y, bw, bh, gapY, OnExport);
        importButton = AddMenuButton(box.transform, "局面を読み込んで続きから指す", Hex(0x6a1b9a), ref y, bw, bh, gapY, OnImport);

        messageText = RuntimeUi.CreateText("Message", box.transform, "", 24f, Hex(0xffe082), TextAlignmentOptions.Center, font);
        RuntimeUi.Place(messageText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(boxW - pad * 2f, 60f));

        Button close = RuntimeUi.CreateButton("Close", box.transform, Hex(0x546e7a), $"閉じる  [{toggleKey}]", 26f, font, out _);
        RuntimeUi.Place((RectTransform)close.transform, new Vector2(0.5f, 0f), new Vector2(0f, pad), new Vector2(260f, 56f));
        close.onClick.AddListener(Close);
    }

    Button AddMenuButton(Transform parent, string label, Color color, ref float y, float w, float h, float gap, UnityEngine.Events.UnityAction onClick)
    {
        Button b = RuntimeUi.CreateButton(label, parent, color, label, 28f, font, out _);
        RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(w, h));
        b.onClick.AddListener(onClick);
        y += h + gap;
        return b;
    }

    void BuildHud()
    {
        hudCanvas = RuntimeUi.CreateCanvas("KifuHudCanvas", transform, hudSortingOrder);

        // 再生中：画面の上の中央
        Image bar = RuntimeUi.CreatePanel("ReplayBar", hudCanvas.transform, new Color(0.05f, 0.08f, 0.2f, 0.88f), true);
        replayBar = bar.rectTransform;
        RuntimeUi.Place(replayBar, new Vector2(0.5f, 1f), new Vector2(0f, 110f), new Vector2(900f, 64f));
        replayText = RuntimeUi.CreateText("Text", replayBar, "", 28f, Color.white, TextAlignmentOptions.Left, font);
        replayText.richText = true;
        RuntimeUi.Stretch(replayText.rectTransform, new Vector2(24f, 0f), new Vector2(-250f, 0f));
        Button stop = RuntimeUi.CreateButton("Stop", replayBar, Hex(0xc62828), "再生を止める", 24f, font, out _);
        RuntimeUi.Place((RectTransform)stop.transform, new Vector2(1f, 0.5f), new Vector2(10f, 0f), new Vector2(220f, 48f));
        stop.onClick.AddListener(() =>
        {
            if (gameSystem != null) gameSystem.StopReplay();
            RuntimeUi.ClearUiSelection();
        });
        replayBar.gameObject.SetActive(false);

        // 結果（数秒で消える。クリックでも消える）
        Button t = RuntimeUi.CreateButton("Toast", hudCanvas.transform, Hex(0x1b5e20), "", 26f, font, out toastText);
        toast = (RectTransform)t.transform;
        RuntimeUi.Place(toast, new Vector2(0.5f, 1f), new Vector2(0f, 184f), new Vector2(1300f, 80f));
        toastText.enableAutoSizing = true;
        toastText.fontSizeMin = 16f;
        t.onClick.AddListener(() => { toast.gameObject.SetActive(false); RuntimeUi.ClearUiSelection(); });
        toast.gameObject.SetActive(false);
    }

    static Color Hex(uint rgb) => RuntimeUi.Hex(rgb);
}
