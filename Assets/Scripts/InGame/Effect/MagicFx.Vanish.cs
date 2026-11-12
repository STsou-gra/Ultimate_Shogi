using System.Collections.Generic;
using UnityEngine;

// 消失のポップアップ（Prototype の view/events.js の vanished と config.js の VANISH_COLORS）。MagicFx の続き（partial）
//
//  駒が消失すると、駒の上に「〈理由〉！」が理由ごとの色で飛び出す（王将なら下に「王 消失！」も）。
//  爆発・隕石・破壊砲・ボムは、それぞれの大きな文字（「爆発!!」「隕石落下!!」「破壊!!」「ボム!!」）を出すので、駒ごとには出さない（Prototype の quiet）。
//  出す時刻は、駒が消える見た目に合わせる：
//   ・必殺技・魔法・自動行動の中 … 攻撃が届く時刻（ActiveSpecial.HitTime）
//   ・移動して炎上・落とし穴のマスに入った … そのマスに着いたとき（GameSystem.EnterCellFxDelay）
//   ・アンデッドの成仏 … 鬼火が昇るのと同時（MagicFx.Undead.cs の OnUndeadDefeated から呼ぶ）
public partial class MagicFx
{
    // 消失理由ごとのポップアップ色（Prototype の VANISH_COLORS）。ない理由は橙（#ff9100）
    public static readonly Dictionary<string, Color> VanishColors = new Dictionary<string, Color>
    {
        { GameSystem.ReasonExplosion, Hex(0xff3d00) },  // 爆発
        { MeteorEffect.Reason, Hex(0xff5722) },         // 隕石
        { BoardCells.ReasonBurning, Hex(0xff6d00) },    // 炎上
        { BoardCells.ReasonTrap, Hex(0xb388ff) },       // 落とし穴
        { BoardCells.ReasonBroken, Hex(0xbcaaa4) },     // マス破壊
        { GameSystem.ReasonUndeadRest, Hex(0xb9f6ca) }, // 成仏
    };
    private static readonly Color VanishDefaultColor = Hex(0xff9100);
    private static readonly Color VanishOutlineColor = Hex(0x1a0a00);
    private static readonly Color KingLostColor = Hex(0xff1744);

    // 駒ごとには出さない理由（大きな文字を別に出すもの）
    static bool IsQuietVanish(string reason)
    {
        return reason == GameSystem.ReasonExplosion || reason == MeteorEffect.Reason || reason == CannonEffect.Reason || reason == BombEffect.Reason;
    }

    public static Color VanishColor(string reason)
    {
        Color c;
        return reason != null && VanishColors.TryGetValue(reason, out c) ? c : VanishDefaultColor;
    }

    // 倒された駒の通知（取り・消失）：消失のポップアップと、アンデッドの成仏
    void OnPieceDefeatedFx(PieceDefeat defeat)
    {
        if (defeat.WasUndead)
        {
            OnUndeadDefeated(defeat); // 成仏のポップアップもここから出す
            return;
        }
        if (defeat.Kind != DefeatKind.Vanish) return;

        SpecialContext ctx = gameSystem != null ? gameSystem.ActiveSpecial : null;
        float delay = ctx != null ? Mathf.Max(0f, ctx.HitTime) : (gameSystem != null ? gameSystem.EnterCellFxDelay : 0f);
        PopVanish(defeat, delay);
    }

    // 「〈理由〉！」（王将なら「王 消失！」も）を delay 秒後に出す
    void PopVanish(PieceDefeat defeat, float delay)
    {
        if (IsQuietVanish(defeat.Reason)) return;
        Vector3 pos = CellWorld(defeat.X, defeat.Y);
        string reason = defeat.Reason;
        bool king = defeat.Type == PieceType.King;
        At(delay, () =>
        {
            PopText($"{reason}！", pos + ScreenUp * (30f * PX), 34f, Color.white, VanishColor(reason), VanishOutlineColor, 40f, 0.9f);
            if (king) PopText("王 消失！", pos - ScreenUp * (20f * PX), 50f, Color.white, KingLostColor, Hex(0x2a0000), 40f, 1.4f);
        });
    }
}
