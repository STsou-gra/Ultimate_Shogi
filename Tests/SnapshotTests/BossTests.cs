// 討伐任務（ボス）のシミュレーション（Match/BossEngine.cs・BoardSimulator.Boss.cs・RaidRules.cs）のテスト。
//  ・Prototype の tests/core/boss.test.js・raid-transcendent.test.js・raid-meteor.test.js を移したもの
//  ・Prototype の棋譜 tests/kifus/raid-akaoni-1〜4.json（PrototypeKifus/ に写した）を再生し、全行動のあとの盤面のハッシュが
//    Prototype の hash.js の値と一致するか（KassenTests.cs の PrototypeHashText に、ボス・討伐任務・手下の項目を足した）
//
// 座標：Prototype の (x = 筋, y = 段。先手は下 y = 6〜8) は Unity の (x = 8 − y, y = x)。テストの場面は Prototype の数値のまま書けるよう、
// PX(px, py)（Unity の段）・PY(px, py)（Unity の筋）・FP（編成の駒）・PPut（駒を置く）で直す
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;

static partial class Program
{
    static void TestBoss()
    {
        TestRaidKifus();
    }

    // ------------------------------------------------------------
    //  準備
    // ------------------------------------------------------------

    static BossDef AKAONI => BossDefs.Akaoni;

    // Prototype のマス → Unity のマス
    static MatchSquare PS(int px, int py) => new MatchSquare(8 - py, px);
    static RaidPlacement FP(PieceType t, int px, int py) => new RaidPlacement(t, 8 - py, px);
    static List<RaidPlacement> KingOnly() => new List<RaidPlacement> { FP(PieceType.King, 4, 8) };

    // 討伐任務の局面（Prototype の setupGame：エナジー 0・時間 0。編成を並べ、ボスは奥の中央）。loadout は Unity の魔法の Id
    static BoardSnapshot Raid(List<RaidPlacement> formation = null, uint seed = 1, PieceType[] picks = null, string[] loadout = null, List<SnapshotSkill> skills = null)
    {
        BoardSnapshot s = Kassen(seed, loadout ?? new[] { "Shield", "Sword", "Meteor" }, new string[0]);
        if (skills != null) s.players[0].skills = skills;
        RaidFormation f = new RaidFormation { pieces = formation ?? KingOnly() };
        if (picks != null) f.forgePicks.AddRange(picks);
        BoardSimulator.SetupRaid(s, AKAONI, f);
        return s;
    }

    // ------------------------------------------------------------
    //  Prototype の棋譜（tests/kifus/raid-akaoni-*.json）の再生
    // ------------------------------------------------------------
    static void TestRaidKifus()
    {
        foreach (string name in new[] { "raid-akaoni-1.json", "raid-akaoni-2.json", "raid-akaoni-3.json", "raid-akaoni-4.json" })
        {
            string path = FindKifu(name);
            Check(path != null, $"Prototype の棋譜 {name} がある（Tests/SnapshotTests/PrototypeKifus）");
            if (path == null) continue;
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement k = doc.RootElement;
            JsonElement cfg = k.GetProperty("config");
            Check(cfg.GetProperty("mode").GetString() == "bossRaid" && cfg.GetProperty("ruleset").GetString() == "kassen" && cfg.GetProperty("bossId").GetString() == "akaoni",
                $"{name}: 討伐任務・合戦将棋型・赤鬼王");

            // 編成（Prototype の setup.formation・forgePicks）と魔法（先手だけ。normal・ultimate・transcendent の順）
            RaidFormation f = new RaidFormation();
            JsonElement setup = k.GetProperty("setup");
            foreach (JsonElement e in setup.GetProperty("formation").EnumerateArray())
            {
                f.pieces.Add(FP(ProtoPieces[e.GetProperty("type").GetString()], e.GetProperty("x").GetInt32(), e.GetProperty("y").GetInt32()));
            }
            foreach (JsonElement e in setup.GetProperty("forgePicks").EnumerateArray()) f.forgePicks.Add(ProtoPieces[e.GetString()]);
            List<SnapshotSkill> deck = new List<SnapshotSkill>();
            JsonElement lo = k.GetProperty("loadouts")[0];
            foreach (JsonElement id in lo.GetProperty("normal").EnumerateArray()) deck.Add(ProtoSkills[id.GetString()]);
            foreach (string rank in new[] { "ultimate", "transcendent" })
            {
                if (lo.TryGetProperty(rank, out JsonElement v) && v.ValueKind == JsonValueKind.String) deck.Add(ProtoSkills[v.GetString()]);
            }
            uint seed = k.GetProperty("seed").GetUInt32();
            BoardSnapshot s = Kassen(seed);
            s.players[0].skills = deck;
            BoardSimulator.SetupRaid(s, AKAONI, f);

            List<JsonElement> actions = k.GetProperty("actions").EnumerateArray().ToList();
            int n = actions.Count, played = 0, matched = 0;
            string error = null;
            while (played < n && !s.gameOver && error == null)
            {
                while (played < n && actions[played].GetProperty("t").GetInt32() == s.clockTick)
                {
                    JsonElement e = actions[played];
                    MatchAction a = FromProto(s, e);
                    if (!BoardSimulator.Apply(s, a, out string reason))
                    {
                        error = $"{played + 1} 手目（t={s.clockTick}）{a} を当てはめられない：{reason}";
                        break;
                    }
                    string text = PrototypeHashText(s);
                    string h = MatchHash.Fnv1a(text);
                    string want = e.GetProperty("hash").GetString();
                    played++;
                    if (h != want)
                    {
                        error = $"{played} 手目（t={s.clockTick}）{a} のあとのハッシュが違う：Prototype {want} ／ Unity {h}";
                        string dump = Environment.GetEnvironmentVariable("PROTO_HASH_DUMP");
                        if (!string.IsNullOrEmpty(dump)) File.WriteAllText(dump, text);
                        break;
                    }
                    matched++;
                }
                if (error != null) break;
                if (played < n && actions[played].GetProperty("t").GetInt32() < s.clockTick)
                {
                    error = $"{played + 1} 手目の時刻（t={actions[played].GetProperty("t").GetInt32()}）を過ぎた";
                    break;
                }
                // 途中で JSON を往復させても同じ（BoardSnapshot のボス・討伐任務・手下の項目）
                if (s.clockTick % 101 == 0) s = BoardSnapshot.FromJson(s.ToJson());
                BoardSimulator.Tick(s);
            }
            Check(error == null, $"{name}: {error}");
            Check(matched == n, $"{name}: 全 {n} 手のハッシュが Prototype と一致（{matched} 手）");
            int endTick = k.GetProperty("endTick").GetInt32();
            Console.WriteLine($"prototype kifu {name}: {matched} / {n} 手のハッシュが一致（seed {seed}・最後の tick {s.clockTick} / {endTick}・ボス HP {s.boss.hp}・王将の被弾 {s.raid.kingHits}・{(s.gameOver ? (s.winner == 0 ? "討伐成功" : "討伐失敗") : "続行中")}）");
        }
    }
}
