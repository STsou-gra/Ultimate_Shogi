# パフォーマンス改善・設計整理の計画（REFACTORING_PLAN）

フレームレートの安定化（GC スパイク・処理落ちの解消）と、保守しやすい設計への整理の計画です。
調査は 2026-11-18 時点の `claude/eager-sagan-7kurvz`（`e8b4cac` の直後）で行いました。**コードはまだ変更していません。**

別のチャットで作業を続けるときは、末尾の「[新しいチャットへの指示](#新しいチャットへの指示)」をそのまま貼ってください。

---

## 0. 厳守ルール

1. **YAML・メタファイルは編集しない。** `.unity`（シーン）・`.prefab`・`.asset`・`.meta` は直接編集しない（GUID の不整合・シーン破損を防ぐ）。
2. **インスペクターの参照を守る。** 既存の `[SerializeField]` や `public` フィールドの**名前・型を無断で変更・削除しない**。再アタッチが必要になる変更は、実装前に必ず明示して承認をもらう。
3. **1 項目ずつ進める。** 計画の 1 項目を実装 → コミット → 「Unity エディタで確かめること」を伝える → 次の項目の承認を待つ。
4. **ルールの中心部分の挙動を変えない。** `Assets/Scripts/InGame/Match/`・`Cpu/`・`Rules/GameRules.cs` は `Tests/SnapshotTests`（.NET 8）で Prototype の棋譜とハッシュを照合している。数値・処理の順序・乱数の使い方を変えない。
5. **グループ A では新しい `.cs` ファイルを作らない。** 新しいファイルには Unity が `.meta` を生成するため。グループ B で新しいファイルが必要なときは、Unity で開いて生成された `.meta` をユーザーにコミットしてもらう、と事前に伝える。
6. **使用ライブラリ。** DOTween の設定は `Assets/Resources/DOTweenSettings.asset` を触らず、必要ならコードで設定する。TMP は uGUI 2.0 同梱版。Input System 1.19。

## 1. 環境

- Unity 6000.3.17f1、URP、Input System 1.19、uGUI 2.0（TMP 同梱）、DOTween（`Assets/Plugins/Demigiant`）
- 対象プラットフォームに **WebGL** がある（`Ultimate-Shogi_WebBuild/`）。WebGL ではインクリメンタル GC が効かず GC がまとめて走るため、GC の削減は WebGL 版で特に効く。
- Play Mode はドメインリロードあり（`EnterPlayModeOptions: 0`）。static は毎回リセットされる。
- 作業用のクラウド環境には Unity も dotnet も入っていない。動作確認はユーザーが Unity エディタで行う。
  - `Tests/SnapshotTests` は `cd Tests/SnapshotTests && dotnet run` で動く（.NET 8 SDK が必要）。対象は `Match/`・`Cpu/`・`GameRules.cs`・`GameHelper.cs` だけ。
- 自前のスクリプトは約 3 万行。物理（`Raycast` / `Overlap`）は未使用。

## 2. すでに良くできていて、触らない部分

- パーティクルはプール済み（`Effect/PieceFx.cs`・`Effect/GlowParticles.cs`）。
- 手続き生成のテクスチャは static にキャッシュされている。`CrackCanvas` やカットインの `Material` も破棄されている。
- `async` の処理は `destroyCancellationToken` を渡し、`OperationCanceledException` を処理している。
- イベントの購読解除（`+=` / `-=`）はほぼ揃っている。
- `Find` 系はほぼ初期化時だけ。`Resources.Load` も起動時だけ。
- 実行時に作る UI は Canvas が分かれていて、`raycastTarget = false` も設定済み（`RuntimeUi.CreateText`）。
- シミュレーションの照合・デバッグツールはエディタと Development Build に限定済み。
- CPU の探索スレッドは制限時間付きで自然に終わる。

---

## 3. 現状分析

### 3-1. フレーム落ちの要因

| # | 場所 | 内容 | 頻度 | 影響 |
|---|---|---|---|---|
| P1 | `Effect/GameTickEffects.cs:614-643`（`UpdateUI`。623・635・641 行） | 手番中ずっと、シーンに元からある Canvas 上で `Outline.effectColor`（スキルボタン・パネル）と TMP の `alpha`（手番表示）を変えている。TMP は `alpha` を変えると文字メッシュを作り直す。Board シーンの Canvas（TMP 23・Image 23）が毎フレーム再バッチされる | 毎フレーム | **高（CPU）** |
| P2 | `ForgePanelView.cs:96`（`Update`） | レシピ数ぶん毎フレーム：`CanForge` → `ForgeBlockReason`（`GameSystem.Actions.cs:342-349`）が `$"エナジー不足：あと {…}"` を生成／`FocusForgeKeyLabel` → `SeatKeys.KeyName` の既定分岐 `k.ToString()`（`InputSeat.cs:205`）／狙撃・必殺技ボタンの `$"..."`／`CanUseSelectedSpecial` が `new SpecialContext(...)`（`GameSystem.cs:1581`） | 毎フレーム | **高（GC）** |
| P3 | `GameBoard.cs:219`（`Pieces()`） | 呼ぶたびに新しい `List<GamePiece>` を作る。呼び出し元：`GameTickEffects.cs:207,440`（毎フレーム 2 回）、`BossView.cs:435,489`（討伐任務で毎フレーム 2 回）、`Skill/PhalanxAdvance.cs:29`・`Skill/UndeadMarch.cs:41-` の `HasWork`（合戦将棋型で tick ごと） | 毎フレーム | 中〜高（GC） |
| P4 | `TranscendentStatusView.cs:142-170`（`LateUpdate`） | 162 行の `new Vector3[4]` と、`StatusText` の文字列約 5 本 | 毎フレーム（超常魔法の編成時） | 中（GC） |
| P5 | `Effect/GameTickEffects.cs:397` | 翼の `w.GetComponentsInChildren<SpriteRenderer>()` を左右で毎フレーム | 翼のある駒ごと | 中（GC） |
| P6 | `GameSystem.cs:745`（`PressedIndex`）・`GameSystem.cs:1805`（`HandleForgeKeys`）の `new[] { key }`／`InputSeat.cs:142-157` の `AnyPressed` が `yield` の `All()`／`GameSystem.Realtime.cs:642`・`GameSystem.cs:3021` の `new[] { PlayerType.Player1, PlayerType.Player2 }` | 毎フレーム／tick（50ms＝毎秒 20 回） | 小〜中（GC） |
| P7 | `BossView.cs:382`（`UpdateLabel`・毎フレーム補間）・`BossView.cs:626`（`RefreshHud`・0.1 秒ごと、StringBuilder 2 本）・`RealtimeHudView.cs:54-140`（0.1 秒ごと、LINQ（115 行）・StringBuilder・`LocalizeKeys` の `Replace`） | 毎秒 10 回〜毎フレーム | 中（GC） |
| P8 | `Effect/MagicFx.cs:1016-1241` ほか | static スプライト 31 個が初回アクセス時に生成される。魔法陣は 256×256 を 1 ピクセルずつ計算（`MagicFx.cs:1099`） | 各演出の初回 | 中（単発の処理落ち） |
| P9 | DOTween の初期化 | `SetTweensCapacity` が無く既定（Tween 200・Sequence 50）。演出が重なると対局中に自動拡張が起きる | 演出の重なり時 | 小〜中（単発） |
| P10 | `Debug.Log` 187 箇所（`GameSystem.cs` 52 など） | `ProjectSettings` の Stack Trace が全種類 ScriptOnly。製品ビルドでも行動ごとにスタックを取得（例：`GameSystem.State.cs` の `FinishActionHash`） | 行動ごと | 小〜中 |
| P11 | `Effect/CutInEffect.cs`（1 回で UI 約 20 個＋粒を作って `Destroy`）・`NewPieceFx.cs:413`・`SpecialMoveFx.cs:383`・`BoardCellView.cs:654`（駒を `Instantiate` で複製） | 演出ごと | 小 |
| P12 | `Assets/Scenes/Board.unity` | Raycast Target が有効な Graphic が 46 個（うち TMP 23） | 入力時 | 小 |
| P13 | 同期の `SceneManager.LoadScene`（14 箇所）＋Board 開始時の手続きテクスチャ生成 | シーン遷移時 | 小〜中 |

### 3-2. 設計の課題

- **D1：`GameSystem` の肥大化。** partial 9 ファイル・計 7,850 行（`GameSystem.cs` だけで 3,766 行）。入力（`HandleTurnUpdate` のキー読み取り）・ルール判定・UI 更新（`HandUIManager` の参照 105 回、成りの確認画面、スキル枠の `UpdateActivePlayerSkillUI`）・演出待ち・通信・棋譜・CPU・合戦将棋型・討伐任務が、同じクラスの状態を共有している。partial はファイル分割だけ。
- **D2：シングルトン依存。** `HandUIManager.Instance` 134 回（ほぼ毎回 null チェック付き）、`NetworkManager.Instance` 14 回（`DontDestroyOnLoad`）、`CutInEffect.Instance` 7 回。シーン間の static 状態（`MatchSetup`・`DeckTransferManager`・`GameResult`・`KifuSession`）、入力を止める static フラグ（`RulesOverlayView.IsBlockingInput`・`KifuMenuView.IsBlockingInput`）。
- **D3：データのハードコード。** スキル・錬成・必殺技はすでに ScriptableObject（`SkillDatabase`・`ForgeRecipeBook`・`SpecialMoveBook`）。ルール定数（`Match/MatchConfig.cs`・`Match/BossDefs.cs`・`Match/PieceValues.cs`・CPU の重み）はコードだが、.NET テストと Prototype の棋譜ハッシュ照合の対象。**ScriptableObject 化は決定性とテストを壊すので推奨しない。**
- **D4：ライフサイクル。** 大きな問題なし。気になる点：
  - `GameController.cs:38-62` で `Keyboard.current` の null チェックが無い（キーボードの無い環境で毎フレーム例外）。
  - `HandUIManager.Awake` は重複時に `Destroy(gameObject)` で GameObject ごと消す。

---

## 4. 改善計画

進行状況はチェックボックスで管理する（完了したら `[x]` にしてコミットに含める）。

### グループ A：コードだけで完結（インスペクターの参照は変えない・新しいファイルを作らない）

効果が大きくリスクが低い順。

#### - [x] A-1 HUD の毎フレームの GC（P2・P4） 効果：高／リスク：低

- `InputSeat.cs`：`SeatKeys.KeyName` の既定分岐（`k.ToString()`）の結果を `static Dictionary<Key, string>` にキャッシュする。返す文字列は今と同じ。
- `GameSystem.Actions.cs`：`ForgeBlockReason` と同じ条件を文字列なしで判定する `bool` 版（例：`IsForgeBlocked`）を追加し、`GameSystem.cs:1740` の `CanForge` から使う。理由の文字列が要る箇所は今の `ForgeBlockReason` のまま。
- `ForgePanelView.cs`：狙撃・必殺技ボタンの文字列は、元になる値（`aiming`・キー名・技名・`usable`・`choosing`）が前回と変わったときだけ作る。
- `TranscendentStatusView.cs`：`corners` をフィールドの配列で使い回す。`StatusText` は元になる値（使用済み・解放済み・`TranscendentProgress` の各値）が変わったときだけ作る。位置（`anchoredPosition`）は値が変わったときだけ書く。
- 今回は `CanUseSelectedSpecial` の `new SpecialContext` は触らない（盤面の変化を拾う仕組みが要るため。B-4 の後に検討）。
- 実装メモ：`TranscendentStatusView` は文字列を作らなくなったが、`GetTranscendentProgress` → `TranscendentRules.Progress`（`Match/`）の `board.Pieces()` の List はまだ毎フレーム作られる（A-2 で `Pieces(List)` を足したあと、`Match/` の外の呼び出し側で解決できるか検討）。必殺技を使える駒を選んでいる間は `new SpecialContext` も残る。

Unity エディタで確かめること：
- コンソールにエラー・警告が増えていない。
- 錬成ボタン：エナジーが足りる／足りないで押せる状態が切り替わる。キー表示（Z・X…、合戦将棋型の後手は N・M）が正しい。
- 狙撃手・必殺技が使える駒を選んだときにボタンが出て、文言とキー表示が正しい。対象選択中は「やめる」に変わる。
- 超常魔法を編成した対局で、解放条件の表示（侵入 ○×・駒数）が駒の増減に合わせて変わる。
- Profiler：手番の待機中に `ForgePanelView.Update`・`TranscendentStatusView.LateUpdate` の GC Alloc が 0 B 付近になる。

#### - [x] A-2 駒一覧の List 生成（P3・P5） 効果：中〜高／リスク：低

- `GameBoard.cs`：既存の `List<GamePiece> Pieces()` は**残し**、渡された List に詰める `void Pieces(List<GamePiece> into)`（`Clear` してから追加。順序は同じ）を追加する。
- 毎フレームの呼び出し元だけ、使い回しの List に切り替える：`GameTickEffects.cs:207,440`、`BossView.cs:435,489`。
- `GameTickEffects.cs:397`：翼の SpriteRenderer を `CreateWings` の時点で `PieceFx`（同ファイル内のクラス）に配列でキャッシュし、毎フレームの `GetComponentsInChildren` をやめる。
- 盤を書き換えながら回す呼び出し元（`UndeadMarch` など）は今のまま。
- 実装メモ：`TranscendentRules.Progress`（`Match/`）の `board.Pieces()` は今回は触っていない（`Match/` の中なので。毎フレームの List が 1 本残る）。必要なら `TranscendentStatusView` 側で値の変化を待ってから呼ぶ形を B-4 の後に検討する。

Unity エディタで確かめること：
- 駒のオーラ・盾・カウンター・ファランクスの槍・アンデッド・翼・鳥の羽ばたきが今までどおり出る。
- 駒が取られたあとにオーラなどが残らない。
- 討伐任務：手下の行き先の矢印・軍銃兵の照準と線が出る。
- Profiler：`GameTickEffects.LateUpdate` の GC Alloc が 0 B 付近。

#### - [x] A-3 入力・tick の GC と安全対策（P6・D4） 効果：中／リスク：低

- `InputSeat.cs`：`SeatKeys.Pressed(Key key)`（1 キー版）を追加する。`AnyPressed` は `All()` の `yield` をやめ、各配列を順に `Pressed` で調べる（結果は同じ）。
- `GameSystem.cs:745`（`PressedIndex`）・`1805`（`HandleForgeKeys`）：`new[] { key }` を 1 キー版に置き換える。
- `GameSystem.Realtime.cs:642`・`GameSystem.cs:3021`：`static readonly PlayerType[] BothSides = { PlayerType.Player1, PlayerType.Player2 }`（**順序は同じ**）に置き換える。tick ごとでない箇所（`GameSystem.Snapshot.cs:99,136`、`GameSystem.State.cs:124,213`、`GameSystem.cs:2889`）は任意。
- `Skill/PhalanxAdvance.cs`・`Skill/UndeadMarch.cs` の `HasWork`：「1 体でもいるか」をアロケーションなしで判定する（戻り値の true / false は今と同じになること）。
- `GameController.cs`：`Keyboard.current` が null なら return。
- 実装メモ：`BothSides` は `GameSystem.cs` に置き、任意の箇所（`Snapshot`・`State`・千日手の判定）も含めて `GameSystem` の partial の中はすべて置き換えた。`Match/`・`Cpu/` の `new[] { Player1, Player2 }` は触っていない。`UndeadMarch.HasWork` は `RaidGoals`・`Kings`・`Pieces`・`Moves` の `Count > 0` をそれぞれ List を作らない `HasRaidGoal`・`HasPiece`・`HasAnyMove` に置き換えた（判定の順序は同じで、元の関数は `Run` で使うので残した）。動けるマスの探索は 1 つ見つかった時点でやめる。

Unity エディタで確かめること：
- 本将棋型：WASD・矢印・F・Space・H・E・G・Q・R・J・Z〜V が今までどおり動く。
- 合戦将棋型：先手と後手のキーがそれぞれ効き、押した側の席に操作が切り替わる。魔法キー（先手 1〜5・後手 6〜0）・錬成キーが効く。
- 合戦将棋型でファランクス・アンデッドの自動行動が今までどおりの間隔で起きる。
- 棋譜の再生（合戦将棋型・討伐任務）で、ハッシュ不一致の警告が出ない。

#### - [x] A-4 手番表示の明滅（P1 の一部） 効果：中／リスク：低〜中

- `GameTickEffects.cs:641`：`turnBadgeText.alpha` の代わりに、実行時に追加した `CanvasGroup` の `alpha` で明滅させる（文字メッシュを作り直さない）。
- 手番表示の GameObject の子が TMP のサブメッシュ（`TMP_SubMeshUI`）以外にもあるときは、子まで薄くなってしまうので、今の方法のまま残す（安全のため判定を入れる）。
- 開始時に TMP の `alpha` は 1 に戻しておく。
- 実装メモ：`GameTickEffects.SetupTurnBadge`（`SetupUI` の最後）で `CanvasGroup` を足す。次のときは足さずに今の方法（TMP の `alpha`）のまま：uGUI の TMP でない／手番表示の GameObject に元から `CanvasGroup` がある（別の用途かもしれないので触らない）／`TMP_SubMeshUI` 以外の子がある。Board シーンの `ActivePlayerText` は子も `CanvasGroup` も無く、文字色の alpha は 1 なので `CanvasGroup` 側になる。手番以外は `alpha` が 1 のままなので、値が変わったときだけ書く。足した `CanvasGroup` は既定値（`interactable`・`blocksRaycasts` は true、`ignoreParentGroups` は false）なので入力は変わらない。

Unity エディタで確かめること：
- 手番中、「【先手（プレイヤー1）の手番】」などの表示が今までどおり明滅し、手番以外は不透明。
- 明滅しても文字の位置・縁取りが変わらない。
- Profiler：`Canvas.SendWillRenderCanvases` の中で手番表示の TMP の再生成（`TextMeshProUGUI.Rebuild` 等）が毎フレーム出なくなる。

#### - [x] A-5 起動時の事前準備（P8・P9） 効果：中（初回の処理落ちを解消）／リスク：低

- DOTween：`[RuntimeInitializeOnLoadMethod]` か `GameSystem` の開始時に、`DOTween.SetTweensCapacity(500, 125)` 程度をコードで設定する（`DOTweenSettings.asset` は触らない）。容量は演出の重なりから決める。
- `MagicFx` などの static スプライト（`MagicFx.cs:1016-1241` の `Glow`・`CircleColored`・`CircleWhite`・`BarrierGlow` など）を、対局の開始時に一度読んで先に作っておく。作るものと中身は今と同じ。
- 実装メモ：DOTween は `GameSystem.ConfigureTweenCapacity`（`[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`）で `DOTween.Init()`（最初の Tween で自動で行うものと同じ）→ `SetTweensCapacity(500, 125)`。容量の目安はマスの崩落 1 マスで Sequence 1・Tween 6〜8（`BoardCellView.PlayFallingTile`）で、砲撃の列や駒の爆散・カットインが重なると既定の 200 を超えうるため。画像は `MagicSprites.Prewarm`（`MagicFx.cs`）を `GameSystem.Start` の `DecideStartMode` の直後から呼ぶ。対象は `MagicFx.cs` の 16 個＋`Heart`・`Bomb`・`CounterGlow`・`CounterFace`、討伐任務のときだけボス（`BossSprite`・`BossWeapon`）と手下・照準・盾・矢印の 9 個。タイトルの `PieceShape` は対象外。`Rock` は `UnityEngine.Random` で形を決めるので、その乱数を引く時点が早まる（演出だけの乱数で、対局の乱数・棋譜には影響しない）。エディタと Development Build では所要時間をログに出す。`MakeSpear`・`MakeWing`・`MakeBirdWing` は 1 ピクセルごとに配列やラムダを作っていて事前準備の時間の大半を占めるはずなので、遅ければ中身を変えずに作り方だけ直す余地がある（今回は触っていない）。

Unity エディタで確かめること：
- コンソールに DOTween の「Max Tweens reached」の警告が出ない（たくさん演出が重なる場面で）。
- 対局の開始が目に見えて遅くなっていない。
- 魔法を初めて使ったときのカクつきが減る（Profiler で初回の `MagicFx` の処理時間を比べる）。

#### - [ ] A-6 毎秒 10 回の HUD 文字列（P7） 効果：中／リスク：中

- `BossView.cs:626`（`RefreshHud`）：HP・最大 HP・フェーズ・攻撃・残り秒・時間停止中か・王将の被弾数・残りの兵などの値が前回と同じなら作り直さない。
- `BossView.cs:382`（`UpdateLabel`）：技名と残り秒が変わったときだけ文字列を作る（色と位置は毎フレームのまま）。
- `RealtimeHudView.cs`：キー案内の固定部分（移動・決定・錬成のキー）は席ごとに一度だけ作ってキャッシュし、115 行の LINQ をやめる。エナジー・使える魔法・状態は今までどおり更新する。
- 作り直す条件に漏れがあると表示が古くなるので、条件に使う値を実装時にコメントで列挙する。

Unity エディタで確かめること：
- 討伐任務（赤鬼王・大将軍）：HP バー・予告の残り秒・フェーズ（予告・攻撃中・休憩）・王将の盾・残りの兵の表示が変わるタイミングどおりに更新される。
- 合戦将棋型：両席のエナジー・魔法の使える／使えない・「（操作中）」「（CPU）」・CPU の考え中が正しく変わる。一時停止の表示も正しい。

### グループ B：構造の変更・エディタでの作業・判断が必要なもの

- [ ] **B-1 スキル枠の明滅を Canvas から切り離す（P1 の残り）**：スキルパネルに Canvas＋GraphicRaycaster を追加する。**エディタ作業あり**（シーン。コードで実行時に足す方法も可）。リスク：中（描画順・クリックの確認が必要）。
- [ ] **B-2 Raycast Target の整理（P12）**：押す必要のない Text / Image のチェックを外す。対象の一覧を作り、操作はエディタでユーザーが行う。リスク：低。
- [ ] **B-3 ログのコスト（P10）**：Player Settings の Stack Trace で Log を None にするか、製品ビルドで Log を抑えるか。**ユーザーの判断が必要**（棋譜のデバッグにログを使っているか）。
- [ ] **B-4 HUD 更新を `GameSystem` から切り出す（D1・D2）**：`UpdateActivePlayerSkillUI` と案内文の更新を Pure C# の `MatchHudPresenter` に移し、`HandUIManager.Instance` の参照を 1 か所にまとめる。`[SerializeField] private SkillButtonUI[] skillSlotUIs`（`GameSystem.cs:297`）は `GameSystem` に残して渡すので再アタッチ不要。新しいファイルの `.meta` は Unity に生成してもらう。リスク：中。
- [ ] **B-5 千日手の記録を Pure C# に（D1）**：`PositionRecord`・`positionHistory`・`positionCounts`（`GameSystem.cs`・`GameSystem.State.cs` のみで使用）を `RepetitionTracker` に移し、`Tests/SnapshotTests` にテストを追加できるようにする。リスク：低〜中。
- [ ] **B-6 演出オブジェクトのプール（P11）**：カットインの粒と駒の複製を使い回す。エディタ作業なし。リスク：中・効果：小。
- [ ] **B-7 シーン遷移の非同期化（P13）**：`LoadSceneAsync` と読み込み中の表示。リスク：中。
- [ ] **B-8 入力処理の分離（D1）**：`HandleTurnUpdate` を「キー → 操作の指示」と「指示の実行」に分ける。効果は最大だが影響範囲も最大なので B-4・B-5 の後。リスク：高。

### 見送りを推奨するもの

- ルール定数の ScriptableObject 化（D3）。決定性と Prototype のハッシュ照合を壊す。
- DOTween の Recyclable の有効化。Tween をフィールドに持っている箇所（例：`PieceFx` の `shakeTween`）があり、使い回しによる不具合の元になる。
- シーンの TMP をコードから一括で設定し直すこと（B-2 はエディタで行う）。

---

## 5. 進め方

- 1 項目＝1 コミット。ブランチの指定が無ければ、作業中のチャットで指定されたブランチに push する。
- コミットメッセージは既存の履歴に合わせて日本語で、何をなぜ変えたかを書く。
- 変更前の数値を残すなら、A-1 の前に Profiler（CPU Usage → Hierarchy）で、手番の待機中の **GC Alloc 列**と **`Canvas.SendWillRenderCanvases`** を記録しておく。
- 実装後は毎回、その項目の「Unity エディタで確かめること」をユーザーに伝える。

---

## 新しいチャットへの指示

以下をそのまま新しいチャットに貼ってください。

```text
このリポジトリは Unity (C#) のゲームプロジェクトです。
リポジトリ直下の REFACTORING_PLAN.md に、フレームレート安定化と設計整理の調査結果・計画・厳守ルールがまとまっています。
まずそれを全部読んでから作業してください。調査はやり直さなくて構いません（必要な箇所だけコードを読んで確認してください）。

- 「0. 厳守ルール」を必ず守ってください（.unity / .prefab / .asset / .meta を編集しない、[SerializeField] と public フィールドの名前・型を変えない、など）。
- 「4. 改善計画」のチェックが付いていない最初の項目（未着手なら A-1）から、1 項目ずつ実装してください。
- 1 項目終わるごとに、REFACTORING_PLAN.md のチェックボックスを [x] にして一緒にコミット・push し、
  その項目の「Unity エディタで確かめること」を私に伝えてから止まってください。次の項目は私の承認を待ってください。
- 計画から外れる変更や、再アタッチが必要になる変更をしたくなったら、実装する前に相談してください。
```
