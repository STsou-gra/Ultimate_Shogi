using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

// 動作確認用：行動（MatchAction）の合法手を Console に出す（Unity でコンパイルしたあとに、行動の層を確かめるため）
//   L … 手番のプレイヤーの合法手（GameSystem.LegalActions）を種類ごとの数と一覧で出す。あわせて次を確かめる
//       ・合法手がすべて CanDispatch で実行できるか
//       ・合法手にない行動（全マスへの移動・打つ・狙撃・必殺技・対象 1 つの魔法・錬成・解除の候補）が CanDispatch で弾かれるか
//       食い違いがあれば警告で出す（なければ「食い違いなし」）
// エディタと Development Build でだけ動く。駒・持ち駒・スキルの対象を選んでいる間は使えない。
// シーンに置かなくても GameSystem が自動で追加する
public class ActionDebugTool : MonoBehaviour
{
    [SerializeField] private bool enableInReleaseBuild = false; // 出展用のビルドでも使いたいときだけオンにする
    [SerializeField] private int listLimit = 300;               // 一覧に出す行動の数の上限

    private GameSystem gameSystem;

    void Start()
    {
        gameSystem = FindAnyObjectByType<GameSystem>();
        if (!(Application.isEditor || Debug.isDebugBuild || enableInReleaseBuild)) enabled = false;
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || gameSystem == null) return;
        if (!keyboard.lKey.wasPressedThisFrame) return;
        if (RulesOverlayView.IsBlockingInput) return;
        if (!CanUse())
        {
            Debug.Log("[ActionDebug] 手番中で、駒・持ち駒・対象を選んでいないときに押してください");
            return;
        }
        LogLegalActions();
    }

    bool CanUse()
    {
        if (!gameSystem.IsInTurn || gameSystem.IsResolvingAction) return false;
        return gameSystem.SelectedPiece == null && !gameSystem.IsSelectingHand && !gameSystem.IsDroppingHand
            && !gameSystem.IsWaitingForPromoteChoice && !gameSystem.IsSelectingSkillTarget && !gameSystem.IsSelectingSnipeTarget;
    }

    PlayerType Side => gameSystem.IsPlayer1Turn() ? PlayerType.Player1 : PlayerType.Player2;

    void LogLegalActions()
    {
        PlayerType side = Side;
        List<MatchAction> legal = gameSystem.LegalActions(side);

        // 種類ごとの数
        Dictionary<MatchActionType, int> counts = new Dictionary<MatchActionType, int>();
        foreach (MatchAction a in legal)
        {
            counts.TryGetValue(a.type, out int n);
            counts[a.type] = n + 1;
        }
        StringBuilder summary = new StringBuilder();
        foreach (MatchActionType type in System.Enum.GetValues(typeof(MatchActionType)))
        {
            counts.TryGetValue(type, out int n);
            summary.Append($" {type} {n}");
        }

        // 合法手がすべて実行できるか・合法手にない候補が弾かれるか
        List<string> problems = new List<string>();
        HashSet<string> keys = new HashSet<string>();
        foreach (MatchAction a in legal)
        {
            if (!keys.Add(a.Key)) problems.Add($"合法手が重複: {a}");
            if (!gameSystem.CanDispatch(a, out string reason)) problems.Add($"合法手なのに実行できない: {a}（{reason}）");
        }
        int checkedCount = 0;
        foreach (MatchAction c in Candidates(side))
        {
            checkedCount++;
            bool can = gameSystem.CanDispatch(c);
            if (can && !keys.Contains(c.Key)) problems.Add($"実行できるのに合法手にない: {c}");
            if (!can && keys.Contains(c.Key)) problems.Add($"合法手なのに実行できない: {c}");
        }

        string who = side == PlayerType.Player1 ? "先手" : "後手";
        Debug.Log($"[ActionDebug] {who}の合法手 {legal.Count} 件：{summary}（候補 {checkedCount} 件と照合）");
        if (problems.Count == 0) Debug.Log("[ActionDebug] LegalActions と CanDispatch の食い違いなし");
        else Debug.LogWarning($"[ActionDebug] 食い違い {problems.Count} 件\n" + string.Join("\n", problems.GetRange(0, Mathf.Min(problems.Count, 50))));

        StringBuilder list = new StringBuilder();
        for (int i = 0; i < legal.Count && i < listLimit; i++) list.Append(legal[i]).Append('\n');
        if (legal.Count > listLimit) list.Append($"…ほか {legal.Count - listLimit} 件\n");
        Debug.Log($"[ActionDebug] 一覧\n{list}");
    }

    // 照合用の候補：ルールで弾かれるものも含めて、ひととおりの行動を作る（駒入替のような対象 2 つの魔法は、合法手の側だけで確かめる）
    IEnumerable<MatchAction> Candidates(PlayerType side)
    {
        GameBoard board = gameSystem.Board;
        if (board == null) yield break;
        List<Vector2Int> squares = new List<Vector2Int>();
        for (int x = 0; x < BoardCells.Size; x++)
        {
            for (int y = 0; y < BoardCells.Size; y++) squares.Add(new Vector2Int(x, y));
        }

        foreach (Vector2Int from in squares)
        {
            GamePiece p = board.GetPieceAt(from.x, from.y);
            if (p == null || p.player != side) continue;
            foreach (Vector2Int to in squares)
            {
                yield return MatchAction.Move(from, to, false);
                yield return MatchAction.Move(from, to, true);
                yield return MatchAction.Snipe(from, to);
                yield return MatchAction.Special(from, to);
            }
            yield return MatchAction.Special(from);
        }

        foreach (PieceType type in PieceRules.HandOrder)
        {
            foreach (Vector2Int to in squares) yield return MatchAction.Drop(type, to);
        }

        Player player = gameSystem.CurrentPlayer;
        if (player != null && player.SkillDeck != null && player.SkillDeck.skills != null)
        {
            foreach (SkillDefinition skill in player.SkillDeck.skills)
            {
                if (skill == null || skill.effect == null) continue;
                yield return MatchAction.Magic(skill.Id);
                if (skill.effect.TargetCount != 1) continue;
                int orients = Mathf.Max(1, skill.effect.OrientCount);
                for (int orient = 0; orient < orients; orient++)
                {
                    foreach (Vector2Int t in squares) yield return MatchAction.Magic(skill.Id, new[] { t }, orient);
                }
            }
        }

        foreach (ForgeRecipe recipe in gameSystem.ForgeRecipes)
        {
            if (recipe != null) yield return MatchAction.Forge(recipe.pieceType);
        }
        yield return MatchAction.ReleaseTimeStop();
    }
}
