using System.Collections.Generic;
using UnityEngine;

// 棋譜の受け渡し（シーンをまたぐ static）。
//  ・Current … 今の対局（または直前に終わった対局）の棋譜。結果画面（GameOver シーン）の「棋譜を保存」もこれを保存する
//  ・依頼   … 棋譜の再生は、Board シーンを読み込み直して始める。読み込み直す前に依頼を入れ、
//              GameSystem.Start が TakeRequest で受け取る（シード・編成・再生する棋譜）。局面の読み込みはシーンを読み込み直さない（GameSystem.ImportState）
public static class KifuSession
{
    public class MatchRequest
    {
        public uint Seed;
        public SkillDeck[] Decks;      // [先手, 後手]
        public KifuFile Replay;        // 再生する棋譜（なければ null）
        public float Speed = 1f;
    }

    private static MatchRequest pending;

    // 今の対局の棋譜（記録していなければ null）と、記録していない理由
    public static KifuFile Current { get; private set; }
    public static string NoKifuReason { get; private set; }

    // 直前の棋譜の再生の結果（結果画面に出す。再生していなければ null）
    public static string LastReplayResult { get; set; }

    public static void SetCurrent(KifuFile kifu, string noKifuReason)
    {
        Current = kifu;
        NoKifuReason = kifu == null ? noKifuReason : null;
    }

    public static void RequestReplay(KifuFile kifu, SkillDeck[] decks, float speed)
    {
        pending = new MatchRequest { Seed = kifu.seed, Decks = decks, Replay = kifu, Speed = speed };
    }

    // 依頼を受け取る（1 回だけ。なければ null）
    public static MatchRequest TakeRequest()
    {
        MatchRequest r = pending;
        pending = null;
        return r;
    }

    // 今の棋譜をファイルに保存する（保存ダイアログ）。戻り値：画面に出すメッセージ
    public static string SaveCurrent()
    {
        KifuFile k = Current;
        if (k == null) return NoKifuReason ?? "棋譜を記録していません";
        if (k.Count == 0) return "まだ 1 手も指していません";
        string error;
        string path = KifuFileDialog.SaveJson("棋譜を保存", k.FileName, k.ToJson(), out error);
        if (path == null) return error;
        return $"棋譜を保存した（{k.Count} 手）：{System.IO.Path.GetFileName(path)}";
    }

    // 棋譜・局面の編成（魔法の Id）から、対局に渡す編成を作る。見つからない魔法があれば null と理由。
    // 探す順：魔法の一覧（Resources/SkillDatabase）→ known（今の対局の編成・編成画面で選んだ編成。一覧から外した魔法を使っていた場合）
    public static SkillDeck[] ResolveDecks(List<KifuLoadout> loadouts, IEnumerable<SkillDeck> known, out string error)
    {
        error = null;
        if (loadouts == null || loadouts.Count != 2)
        {
            error = "編成が壊れています";
            return null;
        }
        Dictionary<string, SkillDefinition> byId = new Dictionary<string, SkillDefinition>();
        SkillDatabase db = SkillDatabase.LoadDefault();
        if (db != null && db.allSkills != null)
        {
            foreach (SkillDefinition s in db.allSkills) AddSkill(byId, s);
        }
        if (known != null)
        {
            foreach (SkillDeck deck in known)
            {
                if (deck == null || deck.skills == null) continue;
                foreach (SkillDefinition s in deck.skills) AddSkill(byId, s);
            }
        }

        SkillDeck[] decks = new SkillDeck[2];
        for (int i = 0; i < 2; i++)
        {
            SkillDeck deck = ScriptableObject.CreateInstance<SkillDeck>();
            deck.name = i == 0 ? "Kifu Player1 Deck" : "Kifu Player2 Deck";
            deck.hideFlags = HideFlags.DontUnloadUnusedAsset; // シーンを読み込み直しても消えないように
            deck.skills = new List<SkillDefinition>();
            foreach (string id in loadouts[i].skills)
            {
                if (string.IsNullOrEmpty(id))
                {
                    deck.skills.Add(null);
                    continue;
                }
                SkillDefinition skill;
                if (!byId.TryGetValue(id, out skill))
                {
                    error = $"{(i == 0 ? "先手" : "後手")}の魔法「{id}」が見つかりません";
                    return null;
                }
                deck.skills.Add(skill);
            }
            decks[i] = deck;
        }
        return decks;
    }

    static void AddSkill(Dictionary<string, SkillDefinition> byId, SkillDefinition s)
    {
        if (s == null) return;
        string id = s.Id;
        if (!string.IsNullOrEmpty(id) && !byId.ContainsKey(id)) byId.Add(id, s);
    }
}
