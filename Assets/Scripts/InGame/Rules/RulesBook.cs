using System.Collections.Generic;
using System.Text;

// ルール説明の中身（Prototype の src/scenes/RulesScene.js の pages）。
// MonoBehaviour に依存しないプレーンなクラスで、表示（RulesOverlayView）とは分けている。
//
// 錬成・必殺技・魔法の一覧は定義データ（ForgeRecipe・SpecialMoveBook・SkillDefinition）の
// 名前・コスト・説明から作るので、データを足すと説明にも自動で出る：
//  ・錬成 … ForgeRecipeBook にレシピを足す
//  ・必殺技 … SpecialMoveBook に技を足す
//  ・魔法 … SkillDatabase（編成の候補）に定義を足す。ランクごとに並ぶ
// 一部の魔法（ファランクス・停止世界・アンデッド・パラダイス）は「魔法の補足」ページに詳しい決まりを載せる。
// その魔法が候補にないときは補足も出さない。
//
// 文面は DEVELOPMENT_PLAN の決定事項に合わせる（Prototype と違うところは Unity 側の決まりで書く）
public enum RulesLineKind
{
    Heading, // 「■ 基本ルール」
    Sub,     // 「【通常魔法】」などの小見出し
    Text,    // 本文
}

public class RulesLine
{
    public RulesLineKind Kind;
    public int Indent;      // 字下げ（全角文字の数）。折り返した行も同じだけ下げる
    public string Text;
    public string Badge;    // 本文の前に色を変えて出す短い印（「先手」「後手」など。なければ null）
    public UnityEngine.Color? Color; // 文字の色（小見出しのランクの色など。null なら既定の色）
}

public class RulesPage
{
    public string Title;
    public readonly List<RulesLine> Lines = new List<RulesLine>();

    public RulesPage(string title)
    {
        Title = title;
    }

    public RulesPage Heading(string text)
    {
        Lines.Add(new RulesLine { Kind = RulesLineKind.Heading, Text = text });
        return this;
    }

    public RulesPage Sub(string text, UnityEngine.Color? color = null, int indent = 1)
    {
        Lines.Add(new RulesLine { Kind = RulesLineKind.Sub, Indent = indent, Text = text, Color = color });
        return this;
    }

    public RulesPage Text(string text, int indent = 1, string badge = null)
    {
        Lines.Add(new RulesLine { Kind = RulesLineKind.Text, Indent = indent, Text = text, Badge = badge });
        return this;
    }

    // 書式なしの文字列（テスト・ログ用）
    public string ToPlainText()
    {
        StringBuilder sb = new StringBuilder();
        foreach (RulesLine line in Lines)
        {
            sb.Append(new string('　', line.Indent));
            if (line.Kind == RulesLineKind.Heading) sb.Append("■ ");
            if (!string.IsNullOrEmpty(line.Badge)) sb.Append('［').Append(line.Badge).Append("］");
            sb.Append(line.Text).Append('\n');
        }
        return sb.ToString();
    }
}

// ルール説明を作るのに使う定義データ。null・空の項目は「（まだありません）」になる
public class RulesSources
{
    public IReadOnlyList<ForgeRecipe> ForgeRecipes;
    public SpecialMoveBook Specials;
    public IReadOnlyList<SkillDefinition> Magics;          // 編成の候補（SkillDatabase.allSkills）
    public IReadOnlyCollection<SkillDefinition> Player1Deck; // この対局の編成（一覧に「先手」の印を付ける。なければ null）
    public IReadOnlyCollection<SkillDefinition> Player2Deck;
    public System.Func<int, string> ForgeKeyLabel;          // 錬成のキー（GameSystem.ForgeKeyLabel。なければ null）
}

public static class RulesBook
{
    public static List<RulesPage> Build(RulesSources src)
    {
        if (src == null) src = new RulesSources();
        List<RulesPage> pages = new List<RulesPage>
        {
            BasicPage(),
            EnergyPage(src),
            ForgeAndSpecialPage(src),
            MagicPage(src),
        };
        RulesPage extra = MagicDetailPage(src);
        if (extra != null) pages.Add(extra);
        pages.Add(ControlsPage(src));
        return pages;
    }

    // ------------------------------------------------------------
    //  基本ルール・勝ち負け・ターンと陣地
    // ------------------------------------------------------------
    static RulesPage BasicPage()
    {
        RulesPage p = new RulesPage("基本ルール");
        p.Heading("基本ルール")
         .Text("本将棋と同じ初期配置・動き。取った駒は持ち駒になり、空きマスに打てる（二歩・行き所のない駒・打ち歩詰めは禁止）。")
         .Text("敵陣（相手側の3段）に入る・敵陣の中で動く・敵陣から出るときに、成るか成らないかを選べる。行き所がなくなる駒は必ず成る。");

        p.Heading("勝ち負け")
         .Text("王手：王将が取られる状態になると「王手」と表示される。王手を放置する手も指せるが、次の手で王将を取られたら負け。")
         .Text("詰み：王手を外す手（移動・持ち駒を打つ・狙撃）が1つもなければ、その時点で負け。魔法・必殺技・錬成は詰みの判定に含めない。")
         .Text("王将が2枚以上あるとき（王将複製など）は、王手・詰みはない。王将がすべて盤上からいなくなったら負け（取られる・炎上や落とし穴で消失するなど）。")
         .Text("千日手：同じ局面が4回現れたら引き分け。ただし王手をかけ続けていた側は負け。");

        p.Heading("ターンと陣地")
         .Text("先手・後手が1回ずつ指して1ターン（画面に「第Nターン（第M手）」）。手番が替わるたびに盤が回り、手番の側が手前になる。")
         .Text("盤の手前3段＝自陣 ／ 中央3段＝中立 ／ 奥3段＝敵陣（色分けと境界線で表示）。")
         .Text($"第 {MatchConfig.EnemyZoneDropLockTurns} ターンまでは、敵陣に持ち駒を打てない。");
        return p;
    }

    // ------------------------------------------------------------
    //  エナジー・取りと消失・マスの状態
    // ------------------------------------------------------------
    static RulesPage EnergyPage(RulesSources src)
    {
        RulesPage p = new RulesPage("エナジー・マス");

        List<string> values = new List<string>();
        foreach (PieceType type in PieceNames.All)
        {
            int v = PieceValues.Of(type);
            if (v > 0) values.Add($"{PieceNames.Full(type)} {v}");
        }
        PieceType ex = PieceType.Rook;
        string example = $"例：{PieceNames.Full(ex)}を取られた側 +{PieceValues.CapturedSideGain(ex)} ／ 取った側 +{PieceValues.CapturerGain(ex)}";

        int energyMax = MatchConfig.EnergyMax; // 0 = 上限なし（定数のまま比べると「到達できないコード」の警告が出るので変数に入れる）
        p.Heading("エナジー")
         .Text($"自分の手番の開始時に +{MatchConfig.PointsPerTurn}。駒が取られると、取られた側が駒の価値の全額、取った側がその半分（切り捨て）を得る。")
         .Text($"駒の価値：{string.Join(" ／ ", values)}（{example}）")
         .Text(energyMax > 0
             ? $"エナジーは駒錬成と魔法スキルに使う（最大 {energyMax}）。"
             : "エナジーは駒錬成と魔法スキルに使う。");

        p.Heading("取りと消失")
         .Text("取り（通常の移動・必殺技・狙撃・剣・ファランクスなど）：上のエナジー配分あり。取った駒は持ち駒になる。")
         .Text("消失（火薬の爆発・爆発・隕石・破壊砲・炎上・落とし穴・マス破壊など）：エナジーなし。持ち駒にもならない。")
         .Text("王将は爆発（火薬・魔法の「爆発」）では倒せない（範囲にいても残る）。")
         .Text("盾：盾状態の駒（水色の六角形のバリア）は、相手の取りを1回だけ防ぐ（盾は割れる）。消失は防げない。")
         .Text("通常の移動で盾の駒を取ろうとした駒は、元のマスへ跳ね返る（手番は使う）。必殺技の突進は盾の駒の手前で止まる。", 2);

        p.Heading("マスの状態")
         .Text("破壊：穴が開いたマス。移動も通過もできず、持ち駒も打てない。壊れた瞬間にそこにいた駒は消失する。対局終了まで戻らない。")
         .Text("炎上：入った駒は消失する（打った駒も）。すでにいる駒は無事。右上の数字＝残りターン。時間がたつと元に戻る。")
         .Text("落とし穴：暗い渦（縁の色＝仕掛けた側）。敵味方問わず、入った駒は消失し、穴も消える。持ち駒は打てない。");
        bool hasWall = FindEffect<WallEffect>(src) != null;
        if (hasWall)
        {
            p.Text("壁：石積みの壁（縁の色＝建てた側）。敵味方問わず、移動・通過・打ち込みができない（走り駒・突進技は手前で止まり、光線もさえぎる）。")
             .Text("桂の跳躍は飛び越せ、狙撃はさえぎらない。対局終了まで残る（取り・爆発・隕石・炎上では壊れない。マスが破壊されると壁ごと崩れる）。", 2);
        }
        p.Text(hasWall
            ? "走り駒（香車・角行・飛車）は炎上・落とし穴のマスで止まり、破壊マス・壁の手前で止まる。"
            : "走り駒（香車・角行・飛車）は炎上・落とし穴のマスで止まり、破壊マスの手前で止まる。");
        return p;
    }

    // ------------------------------------------------------------
    //  駒錬成・狙撃・必殺技
    // ------------------------------------------------------------
    static RulesPage ForgeAndSpecialPage(RulesSources src)
    {
        RulesPage p = new RulesPage("錬成・必殺技");

        p.Heading(MatchConfig.ForgeEndsTurn
            ? "駒錬成（画面右上の「駒錬成」パネル。手番を使う）"
            : "駒錬成（画面右上の「駒錬成」パネル。手番は使わない）");
        bool hasSniper = false;
        int count = 0;
        if (src.ForgeRecipes != null)
        {
            for (int i = 0; i < src.ForgeRecipes.Count; i++)
            {
                ForgeRecipe r = src.ForgeRecipes[i];
                if (r == null) continue;
                string name = string.IsNullOrEmpty(r.label) ? PieceNames.Full(r.pieceType) : r.label;
                string key = src.ForgeKeyLabel != null ? src.ForgeKeyLabel(i) : "";
                string keyText = string.IsNullOrEmpty(key) ? "" : $"・{key} キー";
                p.Text($"・{name}（{r.cost}エナジー{keyText}）：{Clean(r.description)}", 2);
                if (r.pieceType == PieceType.Sniper) hasSniper = true;
                count++;
            }
        }
        if (count == 0) p.Text("（錬成できる駒はまだありません）", 2);
        else p.Text("エナジーを払って駒を作り、持ち駒に加える。打つときは通常の持ち駒と同じルール（序盤は敵陣に打てない、など）。");

        if (hasSniper)
        {
            p.Heading("狙撃（狙撃手）")
             .Text("自分の手番に自分の狙撃手を選び、「狙撃」（G キー）→ 撃つ相手の駒を選んで F。手番を使う。")
             .Text("王将以外の相手の駒なら、盤上のどこでも撃てる（射程無制限・間に駒があっても撃てる）。")
             .Text("撃った駒は「取り」になる（エナジー配分・持ち駒化あり。盾で防がれる）。火薬を撃つとその場で爆発する。")
             .Text("狙撃手は動かず、成らない。取られた狙撃手は相手の持ち駒になる。");
        }

        p.Heading("必殺技")
         .Text("駒ごとに移動回数を数える（通常の移動だけ。打つ・魔法で動かすのは数えない）。必要な回数に達すると駒にオーラが出る。")
         .Text("オーラの出ている自分の駒を選び、「必殺技」（E キー）で発動。手番を使う。使うと移動回数は 0 に戻る。")
         .Text("駒を選ぶと、移動回数とあと何回で使えるかが画面に出る。成った駒も同じ技を使える。");
        int specials = 0;
        if (src.Specials != null && src.Specials.entries != null)
        {
            foreach (SpecialMoveBook.Entry e in src.Specials.entries)
            {
                if (e == null || e.move == null) continue;
                p.Text($"・{PieceNames.Short(e.pieceType)}「{e.move.moveName}」（{e.move.requiredMoves}回移動）：{Clean(e.move.description)}", 2);
                specials++;
            }
        }
        if (specials == 0) p.Text("（必殺技はまだありません）", 2);
        p.Text("共通：味方の駒・破壊マス・壁の手前で止まる。炎上・落とし穴に入ると消失し、火薬に当たると爆発する。敵陣に入る・出ると自動で成る。取った駒は持ち駒になり、王将も取れる（取ったら勝ち）。");
        return p;
    }

    // ------------------------------------------------------------
    //  魔法スキル（編成・発動・一覧）
    // ------------------------------------------------------------
    static RulesPage MagicPage(RulesSources src)
    {
        RulesPage p = new RulesPage("魔法スキル");

        List<string> picks = new List<string>();
        foreach (SkillRank rank in SkillRanks.All) picks.Add($"{SkillRanks.Label(rank)} ×{SkillRanks.Pick(rank)}");

        p.Heading("編成（対局前）")
         .Text($"対局を始める前に、それぞれ魔法を選ぶ：{string.Join(" ／ ", picks)}。")
         .Text("候補が選ぶ数以下のランクは全部が自動で入る。候補がないランクの枠は「候補なし」。");

        p.Heading("発動")
         .Text("自分の手番に、画面の魔法ボタン（名前とコスト）を押す。発動するとエナジーを払う。")
         .Text("対象を選ぶ魔法は、光っているマスにカーソルを合わせて F。Space で取り消せる（取り消したらエナジーは減らない）。")
         .Text("範囲を選ぶ魔法は、カーソル位置の範囲が塗られる。赤い範囲は選べない。向きのある魔法は R キーで向きを変える。")
         .Text("「手番を使う」魔法は、駒を動かす・打つ代わりになる。「手番を使わない」魔法は、使ったあとも続けて指せる。")
         .Text("エナジーが足りない・効果の対象がないときは押せない。");
        if (HasRank(src, SkillRank.Transcendent))
        {
            p.Text("超常魔法は解放条件があり、1対局に1回だけ使える（「魔法の補足」のページを参照）。");
        }

        p.Heading("魔法スキル一覧");
        if (HasDeck(src)) p.Text("［先手］［後手］＝この対局で編成している魔法。");
        foreach (SkillRank rank in SkillRanks.All)
        {
            p.Sub($"【{SkillRanks.Label(rank)}】", SkillRanks.ColorOf(rank));
            int n = 0;
            if (src.Magics != null)
            {
                foreach (SkillDefinition s in src.Magics)
                {
                    if (s == null || s.rank != rank) continue;
                    string turn = s.EndsTurn ? "手番を使う" : "手番を使わない";
                    p.Text($"・{s.skillName}（{s.cost}エナジー・{turn}）：{Clean(s.description)}", 2, DeckBadge(src, s));
                    n++;
                }
            }
            if (n == 0) p.Text("（まだありません）", 2);
        }
        return p;
    }

    // ------------------------------------------------------------
    //  魔法の補足（候補にある魔法の分だけ）
    // ------------------------------------------------------------
    static RulesPage MagicDetailPage(RulesSources src)
    {
        RulesPage p = new RulesPage("魔法の補足");

        SkillDefinition wall = FindEffect<WallEffect>(src);
        if (wall != null)
        {
            p.Heading($"{wall.skillName}（通常魔法）")
             .Text("自陣か中立の空きマス（駒がなく、破壊・炎上・落とし穴・壁でないマス）に壁を建てる。縁の色＝建てた側。")
             .Text("壁のマスは敵味方問わず、移動・通過・持ち駒の打ち込みができない：")
             .Text("走り駒・突進技（突撃・貫通槍）は壁の手前で止まり、光線（光芒四射・龍王旋風）は壁に当たって止まる。押し出された駒（金剛衝）は壁にぶつかって止まる。", 2)
             .Text("ファランクスの前進・アンデッドの自動行動も壁を通れない。桂の跳躍（通常の移動・天馬跳躍）は飛び越せる。狙撃はさえぎらない。", 2)
             .Text("壁は対局終了まで残る（取り・爆発・隕石・炎上では壊れない）。マスが破壊される（破壊砲・世界の終わり）と壁ごと崩れる。天変地異ではマスと一緒に動く。");
        }

        SkillDefinition phalanx = FindEffect<PhalanxEffect>(src);
        if (phalanx != null)
        {
            p.Heading($"{phalanx.skillName}（必殺魔法）")
             .Text("盾状態の自分の駒（王将以外）を1つ選んでファランクス状態にする。盾はそのまま残り、駒は前方へ金色の槍を構える。")
             .Text("ファランクスの駒は、持ち主の手番の行動が終わったあと、手番が替わる前に自動で動く：")
             .Text("① 1マス前進する。前が盤外・破壊マス・壁・駒で塞がっていれば前進しない。炎上・落とし穴なら消失し、火薬なら爆発する。敵陣に入る・出ると成る。", 2)
             .Text("② 生き残っていれば（前進できなくても）、前方1×3マス（左前・前・右前）の相手の駒を取る（盾で防がれる。相手の火薬は爆発する）。", 2)
             .Text("複数いるときは、盤の奥（相手の陣地側）にいる駒から順に動く。2回行動で手番が続くときは動かない。")
             .Text("盾が割れてもファランクス状態は続く。取られて持ち駒になると解除される。");
        }

        if (HasRank(src, SkillRank.Transcendent))
        {
            p.Heading("超常魔法の解放条件")
             .Text("最初は使えない。次の2つが同時に成り立つと解放される（一度解放されたら、条件が崩れても解放のまま）。")
             .Text("① 相手の駒が1つ以上、自陣（自分側の3段）にいる　② 盤上の駒の数（火薬を除く）が、自分より相手のほうが多い", 2)
             .Text("達成状況（侵入 ○／×、駒数）は超常魔法のボタンの下に出る。1対局に1回だけ使える（使うと「使用済み」）。");
        }

        SkillDefinition timeStop = FindEffect<TimeStopEffect>(src);
        if (timeStop != null)
        {
            int moves = MatchConfig.TimeStopMoves;
            p.Heading($"{timeStop.skillName}（時間停止）")
             .Text($"画面の色が反転して時間が止まり、{moves} 回まで続けて行動できる（同じ駒を何回動かしてもよい）。")
             .Text("行動＝移動・持ち駒を打つ・魔法・必殺技・狙撃（各1回。錬成は数えない）。2回行動は使えない。相手の駒は動かない。")
             .Text("残り回数は画面の上に出る。「解除」ボタン（Q キー）で、回数が残っていても終われる。")
             .Text("行動の効果はその場では起こらず「予定」になる（マスの角などに印が付く）：")
             .Text("取り → 相手の駒は灰色になって残る。以降はないものとして扱う（その上を通ったり止まったりできる）。", 2)
             .Text("火薬に重なる・撃つ → 爆発の予定。炎上・落とし穴に入る → 消失の予定。その駒はもう動かせない。", 2)
             .Text("成り・隕石や破壊砲で消える駒・マスの変化（落とし穴・壁の設置・破壊・炎上）も予定になる。", 2)
             .Text("時間停止中はエナジーが増えない（魔法の消費はその場で減る）。ファランクス・アンデッドなどの自動行動も止まる。")
             .Text("回数を使い切るか「解除」を押すと時間が動き出し、予定を起きた順にまとめて処理する（取りのエナジー・盾も、このとき有効になる）。");
        }

        SkillDefinition undead = FindEffect<UndeadParadiseEffect>(src);
        if (undead != null)
        {
            p.Heading($"{undead.skillName}（アンデッド）")
             .Text("復活するのは、倒された自分の初期配置の駒（王将以外。取られた駒も消失した駒も）。自陣の空きマスが足りない分は復活しない。")
             .Text("紫がかった半透明の駒で、緑の鬼火をまとう。持ち主は操作できず、必殺技も使えない。自分の魔法の対象にも選べない。")
             .Text("持ち主の手番の1手が終わるたび（手番が替わる前）に、全員が自動で1手ずつ動く（相手の王将に近い駒から順に）。")
             .Text("相手の王将を取れる手が最優先。なければ相手の王将に一番近づく手（同じなら相手の駒を取る手）。炎上・落とし穴のマスには入らない。", 2)
             .Text("相手の攻撃は通常どおり受ける。倒されると「成仏」して消える（持ち駒にならず、エナジーも入らない）。");
        }

        return p.Lines.Count > 0 ? p : null;
    }

    // ------------------------------------------------------------
    //  操作（キーボード・マウス）
    // ------------------------------------------------------------
    static RulesPage ControlsPage(RulesSources src)
    {
        RulesPage p = new RulesPage("操作");
        p.Heading("盤の操作")
         .Text("カーソル移動：W A S D ／ 矢印キー")
         .Text("決定：F　　取り消し：Space")
         .Text("駒を動かす：動かす駒で F → 光っているマスで F")
         .Text("持ち駒を打つ：H → 左右で駒を選んで F → 打つマスで F")
         .Text("成る・成らない：左右で選んで F（J キーで「成らない」）");

        List<string> forgeKeys = new List<string>();
        if (src.ForgeRecipes != null && src.ForgeKeyLabel != null)
        {
            for (int i = 0; i < src.ForgeRecipes.Count; i++)
            {
                ForgeRecipe r = src.ForgeRecipes[i];
                string key = src.ForgeKeyLabel(i);
                if (r == null || string.IsNullOrEmpty(key)) continue;
                string name = string.IsNullOrEmpty(r.label) ? PieceNames.Full(r.pieceType) : r.label;
                forgeKeys.Add($"{key}＝{name}");
            }
        }

        p.Heading("錬成・必殺技・魔法")
         .Text(forgeKeys.Count > 0
             ? $"駒錬成：画面右上のボタン（{string.Join("・", forgeKeys)}）"
             : "駒錬成：画面右上のボタン")
         .Text("必殺技：オーラの出ている駒を選んで E（「必殺技」ボタン）")
         .Text("狙撃：狙撃手を選んで G（「狙撃」ボタン）→ 撃つ駒で F")
         .Text("魔法：画面の魔法ボタンをクリック → 対象・範囲を F で決定（R：向きを変える ／ Space：やめる）")
         .Text("時間停止の解除：Q（「解除」ボタン）");

        p.Heading("この画面")
         .Text("開く・閉じる：Tab（「ルール」ボタン）。閉じる：Esc・Space・枠の外をクリックでも閉じる")
         .Text("ページの切り替え：上のタブ ／ ← →・A D　　スクロール：マウスホイール ／ ↑ ↓・W S");
        return p;
    }

    // ------------------------------------------------------------
    //  補助
    // ------------------------------------------------------------
    static bool HasRank(RulesSources src, SkillRank rank)
    {
        if (src.Magics == null) return false;
        foreach (SkillDefinition s in src.Magics)
        {
            if (s != null && s.rank == rank) return true;
        }
        return false;
    }

    static SkillDefinition FindEffect<T>(RulesSources src) where T : SkillEffect
    {
        if (src.Magics == null) return null;
        foreach (SkillDefinition s in src.Magics)
        {
            if (s != null && s.effect is T) return s;
        }
        return null;
    }

    static bool HasDeck(RulesSources src)
    {
        return (src.Player1Deck != null && src.Player1Deck.Count > 0) || (src.Player2Deck != null && src.Player2Deck.Count > 0);
    }

    static string DeckBadge(RulesSources src, SkillDefinition s)
    {
        bool p1 = src.Player1Deck != null && Contains(src.Player1Deck, s);
        bool p2 = src.Player2Deck != null && Contains(src.Player2Deck, s);
        if (p1 && p2) return "先手・後手";
        if (p1) return "先手";
        if (p2) return "後手";
        return null;
    }

    static bool Contains(IReadOnlyCollection<SkillDefinition> deck, SkillDefinition s)
    {
        foreach (SkillDefinition d in deck)
        {
            if (d == s) return true;
        }
        return false;
    }

    // 定義データの説明文を 1 行にそろえる（アセットの改行は詰める）
    static string Clean(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Replace("\r", "").Replace("\n", "").Trim();
    }
}
