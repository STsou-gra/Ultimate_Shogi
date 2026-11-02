using UnityEngine;

// スキル（魔法スキル）の定義（Prototype の src/defs/magic の各ファイルの id・name・tier・cost・color・description）。
// 効果の中身は SkillEffect（別のアセット）に書き、ここから参照する。
// ScriptableObject はクラス名とファイル名が同じでないとアセットからスクリプトを引けないので、このファイルに分けている
[CreateAssetMenu(menuName = "UltimateShogi/Skill Definition")]
public class SkillDefinition : ScriptableObject
{
    public string skillName;
    public Sprite icon;
    public int cost;
    [TextArea]
    public string description;
    public SkillEffect effect;

    [Header("Magic")]
    [Tooltip("ランク（通常 / 必殺 / 超常）。編成画面ではランクごとに選ぶ")]
    public SkillRank rank = SkillRank.Normal;

    [Tooltip("発動すると手番を消費するか（駒を動かす・打つ代わりになる）。MatchConfig.MagicEndsTurn が false なら常に消費しない")]
    public bool endsTurn = false;

    [Tooltip("カットイン・演出の色")]
    public Color color = new Color(0xb3 / 255f, 0xe5 / 255f, 0xfc / 255f);

    // 発動で手番を消費するか（Prototype の CONFIG.MAGIC_ENDS_TURN）
    public bool EndsTurn => endsTurn && MatchConfig.MagicEndsTurn;

    // Executorを廃止し、定義自身に実行させる（冗長化の削減）
    public void Use(SkillContext context)
    {
        if (effect != null)
        {
            effect.Execute(context);
        }
        else
        {
            Debug.LogWarning($"{skillName} の効果(Effect)が設定されていません。");
        }
    }
}
