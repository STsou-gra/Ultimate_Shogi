# アルティメット将棋（Ultimate Shogi）

盤面をひっくり返す、超自由な将棋ゲーム。<br>
駒を取ってポイントを貯め、ポイントを使ってスキルを発動する。新しい駒やギミックも追加予定。

- チーム：チーム28 ゲーム好き一般人
- 出展予定：ゲーム出展型イベント『大阪ゲームダンジョン2』（12/26）
- デモ動画：https://www.youtube.com/watch?v=fbzGp0XJGq8
- KC3Hack時のリポジトリ：https://github.com/kc3hack/2026_team28

<!-- TODO: ゲーム画面のスクリーンショットを1枚入れる -->

---

## 注力したポイント

### アイデア面
- 元の将棋のルールにひねりを加え、ソーシャルゲームのように遊べるゲーム性を目指した

### デザイン面
- 一目で操作方法が分かるようにした

### その他
- 手番のプレイヤー側にカメラが向くようにした

---

## 動作環境

| 項目 | バージョン |
|---|---|
| Unity | **6000.3.17f1**（全員このバージョンに揃えること） |
| レンダリング | URP（2D Renderer） |
| 入力 | Input System |
| サーバー（任意） | .NET 8 |

使用技術：Unity、Figma、DOTween

---

## セットアップ

```bash
# 1. Git LFS を有効にする（画像などは LFS で管理しています）
git lfs install

# 2. リポジトリを取得
git clone https://github.com/STsou-gra/Ultimate_Shogi.git
```

3. Unity Hub で「追加」→ クローンしたフォルダを選ぶ（Unity 6000.3.17f1 で開く）
4. `Assets/Scenes/GameTitle.unity` を開いて再生する

> 画像がピンク色や真っ白になる場合は、LFS の実体が取れていません。`git lfs pull` を実行してください。

---

## 遊び方

### 画面の流れ

```
GameTitle（タイトル）
  → OrganizationScene（スキル編成：各プレイヤーが5枠にスキルをセット）
  → Board（対局）
  → GameOver（結果）
```

### 操作方法（キーボード）

| キー | 動作 |
|---|---|
| W A S D / 矢印キー | カーソル移動・選択肢の切り替え |
| F | 決定（駒を選ぶ・動かす・打つ） |
| Space | キャンセル |
| H | 持ち駒を選ぶ |
| マウスクリック | スキルボタン・UI操作 |

現在は1台の端末を2人で交代して遊ぶ対戦のみです。手番が替わるとカメラが回転します。

---

## フォルダ構成

```
Assets/
├─ Scenes/                 シーン
├─ Scripts/InGame/
│  ├─ GameSystem.cs        ターン進行・入力・成り・持ち駒・スキル使用の中心
│  ├─ GameBoard.cs         盤面データ・駒の配置/削除/入れ替え
│  ├─ Player.cs            持ち駒・スキルデッキ・ポイント
│  ├─ PointManager.cs      ポイントの保持と表示
│  ├─ GameCursor.cs        盤上のカーソル
│  ├─ GameController.cs    決定/キャンセル入力
│  ├─ GameCamera.cs        手番に応じたカメラ回転
│  ├─ HandUIManager.cs     持ち駒UI・メッセージ表示
│  ├─ DeckSelectController.cs  スキル編成画面
│  ├─ NetworkManager.cs    オンライン対戦用クライアント（開発中）
│  ├─ Piece/               駒（GamePiece を継承し CanMove で動きを定義）
│  ├─ Rules/               ルール判定（IGameRule：移動・打ち・二歩など）
│  └─ Skill/               スキルの仕組み（定義・効果・デッキ）
├─ SkillData/              スキルのデータ（ScriptableObject）
│  ├─ Database/            全スキルの一覧
│  ├─ Definition/          各スキルの名前・コスト・説明・アイコン
│  ├─ Effect/              各スキルの効果アセット
│  └─ Deck/                デッキ
├─ Prefabs/                駒・盤のマス・UIパーツ
├─ Image/                  画像素材
└─ Plugins/Demigiant/      DOTween

ShogiServer/               オンライン対戦用サーバー（.NET 8 / TCP 7777）
Ultimate-Shogi_WebBuild/   WebGL ビルド成果物
```

---

## スキルの仕組み

| 型 | 役割 |
|---|---|
| `SkillDefinition` | スキル1つ分のデータ（名前・アイコン・コスト・説明・効果） |
| `SkillEffect` | 効果の抽象クラス。`Execute(SkillContext)` を実装する |
| `SkillDeck` | プレイヤーのデッキ（最大5枠） |
| `SkillDatabase` | 編成画面に並ぶ全スキルの一覧 |
| `DeckTransferManager` | 編成画面 → 対局画面へデッキを受け渡す |

### スキルを追加する手順

1. `Assets/Scripts/InGame/Skill/SkillData/` に `SkillEffect` を継承したクラスを作る

   ```csharp
   [CreateAssetMenu(menuName = "UltimateShogi/Effects/My Skill")]
   public class MySkillEffect : SkillEffect
   {
     public override void Execute(SkillContext context)
     {
       // context.GameSystem / context.GameBoard / context.Player が使える
     }
   }
   ```

2. Project ウィンドウで右クリック → **Create > UltimateShogi > Effects > My Skill** → `SkillData/Effect/` に保存
3. **Create > UltimateShogi > Skill Definition** で定義を作り、名前・コスト・説明・アイコン・Effect を設定 → `SkillData/Definition/` に保存
4. `SkillData/Database/SkillDatabase.asset` の `All Skills` に追加する

---

## オンライン対戦サーバー（開発中）

```bash
cd ShogiServer
dotnet run
# → ポート 7777 で待ち受け
```

接続先は `NetworkManager` の `Server Address` / `Server Port` で変更します。<br>
現在 `NetworkManager` はどのシーンにも配置されていないため、ゲームはローカル対戦として動きます。

---

## 開発ルール

- `main` に直接 push しない。`feature/〇〇` ブランチを作り、Pull Request で取り込む
- **シーン（`.unity`）を編集するときはチームに一声かける**（同時編集すると統合がほぼ不可能なため）
- UIなどはできるだけプレハブに切り出し、シーンの直接編集を減らす
- コミットメッセージには「何をしたか」を書く（例：`スキル枠ボタンをGameSystemに接続`）
- Unity のバージョンを上げるときはチームで相談する

<!-- TODO: インデント（2スペース/4スペース）などのコーディング規約を決めて追記 -->

---

## 現状と今後の課題

- [ ] `OrganizationScene` をビルド設定に追加し、最初に開くシーンを `GameTitle` にする
- [ ] スキル枠ボタン5つを `GameSystem.OnSkillSlotClicked` に接続する
- [ ] 駒を取ったときにポイントを加算する（`GamePiece.piecePoint` は用意済み、加算処理は未実装）
- [ ] `GainCostEffect` のコスト回復処理を実装する
- [ ] 爆発・位置交換・王将複製で、対象のマスを選べるようにする
- [ ] 王将が2枚あるときの勝敗判定
- [ ] 使われていないクラス（`DeckBuildController`、`SelectionManager`、`GameSelectPanel`）を整理する
- [ ] 効果音・BGM、駒の移動やスキルの演出

<!-- TODO: GitHub Issues を使い始めたら、このリストは Issues へのリンクに置き換える -->

---

## ライセンス・使用素材

- ソースコード：MIT License（`LICENSE` を参照）<!-- TODO: 著作権者が「KC3-Management」のままでよいか確認 -->
- [DOTween](https://dotween.demigiant.com/)（Demigiant）<!-- TODO: ライセンス条件を確認 -->
- [Noto Sans JP](https://fonts.google.com/noto/specimen/Noto+Sans+JP)：SIL Open Font License 1.1
- [Material Symbols](https://fonts.google.com/icons)：Apache License 2.0（スキルアイコン）
<!-- TODO: 駒・盤面画像の出典（自作か素材か）を追記 -->
