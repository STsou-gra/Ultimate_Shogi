using System.Collections.Generic;
using UnityEngine;

// SkillDefinition・SkillDeck は ScriptableObject のアセットになるので、クラス名と同じ名前のファイルに分けている
// （SkillDefinition.cs・SkillDeck.cs）。ここにはスキルの実行環境と効果の基底クラスを置く

// ==========================================
// 1. スキル実行に必要なコンテキスト（データ集約）
// ==========================================
public class SkillContext
{
  public GameSystem GameSystem { get; }
  public GameBoard GameBoard { get; }
  public PlayerType Player { get; }
  // プレイヤーが選んだ対象のマス（対象を選ぶスキルのみ使用）
  public List<Vector2Int> Targets { get; } = new List<Vector2Int>();

  // 範囲を選ぶスキルの向き（0 〜 SkillEffect.OrientCount - 1。対象選択中に R キーで切り替える）
  public int Orient { get; set; }

  // 2 段階で選ぶスキル（SkillEffect.UsesDestination。ワープ）の行き先（Prototype の Action の to）。
  // 対象を選び終わったあとに選ぶ。行き先を選ばないスキル・選んでいる途中は null
  public Vector2Int? Destination { get; set; }

  // 演出の時間軸（発動からの秒数）。必殺技と同じ SpecialContext を使う（Capture・Explode・Wait・Schedule など）。
  // 盤面のデータはすぐ変わり、演出は Time に合わせて後から再生される。発動するときに GameSystem が作る（判定だけのときは null）
  public SpecialContext Timeline { get; set; }

  // 魔法の演出（盾・剣・落とし穴など）。null なら演出なし
  public MagicFx Fx { get; set; }

  public SkillContext(GameSystem system, GameBoard board, PlayerType player)
  {
    GameSystem = system;
    GameBoard = board;
    Player = player;
  }

  public GamePiece PieceAt(Vector2Int pos)
  {
    return BoardCells.InBounds(pos.x, pos.y) ? GameBoard.GetPieceAt(pos.x, pos.y) : null;
  }

  // 演出の今の時刻（Timeline がなければ 0）
  public float Time => Timeline != null ? Timeline.Time : 0f;
}

// ==========================================
// 2. 対象選択中の表示（SkillEffect.GetPreview の結果）
// ==========================================
public class SkillPreview
{
  // マスのパネルを光らせるマス（選べるマス・選んだマス。今までの GetPreviewCells）
  public readonly List<Vector2Int> Panels = new List<Vector2Int>();
  // カーソル位置で選ぶ範囲（スキルの色で盤を塗る）
  public readonly List<Vector2Int> Area = new List<Vector2Int>();
  // 範囲を選べない原因のマス（1つでもあれば範囲全体が赤くなり、決定できない）
  public readonly List<Vector2Int> Blockers = new List<Vector2Int>();

  public bool IsBlocked => Blockers.Count > 0;

  public static SkillPreview FromPanels(IEnumerable<Vector2Int> cells)
  {
    SkillPreview preview = new SkillPreview();
    if (cells != null) preview.Panels.AddRange(cells);
    return preview;
  }
}

// ==========================================
// 3. スキル効果の抽象クラス
// ==========================================
public abstract class SkillEffect : ScriptableObject
{
  // 発動前にプレイヤーが選ぶマスの数（0ならすぐ発動）
  public virtual int TargetCount => 0;

  // そのマスを次の対象として選べるか（選択済みの対象は context.Targets に入っている）。
  // 範囲を選ぶスキル（UsesArea）は、範囲が空でなく、選べない原因のマスがないときに選べる
  public virtual bool IsValidTarget(SkillContext context, Vector2Int pos)
  {
    if (UsesArea) return IsAreaSelectable(context, pos);
    return true;
  }

  // 対象選択中に表示する案内文（index は何個目の対象か）
  public virtual string GetTargetPrompt(int index)
  {
    return "対象のマスを選択";
  }

  // 今の盤面で使えるか（エナジー・手番は GameSystem が見る。Prototype の canUse と「効果対象がない」の判定）。
  // 既定では、対象を選ぶスキルは選べるマスが1つ以上あるときに使える
  public virtual bool CanUse(SkillContext context)
  {
    if (TargetCount <= 0) return true;
    return HasAnyTarget(context);
  }

  // 対象選択中に光らせるマス（既定では選べるマスと選択済みのマス）
  public virtual List<Vector2Int> GetPreviewCells(SkillContext context, Vector2Int cursor)
  {
    List<Vector2Int> cells = new List<Vector2Int>();
    for (int x = 0; x < 9; x++)
    {
      for (int y = 0; y < 9; y++)
      {
        Vector2Int pos = new Vector2Int(x, y);
        if (context.Targets.Contains(pos) || IsValidTarget(context, pos))
        {
          cells.Add(pos);
        }
      }
    }
    return cells;
  }

  // ------------------------------------------------------------
  //  範囲の表示（Prototype の skill.area）
  //  対象選択中、カーソル位置の範囲を盤に塗る（スキルの色。選べない範囲は赤）。
  //  向きが2つ以上あるスキルは R キーで向きを切り替える（context.Orient）
  // ------------------------------------------------------------

  // true なら「範囲を選ぶスキル」：決定できるかを範囲で決める（IsValidTarget の既定の判定）
  public virtual bool UsesArea => false;

  // カーソル位置 cursor・向き context.Orient で選ぶ範囲。null なら範囲を塗らない
  public virtual List<Vector2Int> GetAreaCells(SkillContext context, Vector2Int cursor)
  {
    return null;
  }

  // 範囲を選べない原因のマス（空でなければ範囲が赤くなり、決定できない）
  public virtual List<Vector2Int> GetAreaBlockers(SkillContext context, Vector2Int cursor, List<Vector2Int> area)
  {
    return new List<Vector2Int>();
  }

  // 選べない範囲の説明（案内文に出す）
  public virtual string BlockedText => "その範囲は選べません";

  // 向きの数（2つ以上なら R キーで切り替えられる）と、向きの名前（「横」「縦」など）
  public virtual int OrientCount => 1;

  public virtual string GetOrientLabel(int orient)
  {
    return "";
  }

  // ------------------------------------------------------------
  //  2 段階で選ぶ（Prototype の skill.getDestinations。ワープの「駒 → 行き先」）
  //  TargetCount 個の対象を選び終わったあとに、行き先のマスを 1 つ選ぶ。行き先の候補は選んだ対象（context.Targets）で変わる。
  //  選んだ行き先は MatchAction.to に入り、Execute では context.Destination で受け取る。
  //  行き先を選ぶ段階で、最後に選んだ対象をもう一度選ぶと対象選びに戻り、ほかの対象を選ぶとその対象で選び直す
  //  （IsValidTarget は「行き先が 1 つ以上ある対象」だけを true にしておくこと）
  // ------------------------------------------------------------

  // true なら、対象を選んだあとに行き先を選ぶ
  public virtual bool UsesDestination => false;

  // 選んだ対象（context.Targets）から、to を行き先に選べるか
  public virtual bool IsValidDestination(SkillContext context, Vector2Int to)
  {
    return false;
  }

  // 行き先を選んでいるときの案内文
  public virtual string GetDestinationPrompt()
  {
    return "行き先のマスを選択";
  }

  // 選んだ対象（context.Targets）からの行き先の候補（盤の全マスのうち IsValidDestination が true のもの）
  public List<Vector2Int> GetDestinations(SkillContext context)
  {
    List<Vector2Int> cells = new List<Vector2Int>();
    if (!UsesDestination) return cells;
    for (int x = 0; x < BoardCells.Size; x++)
    {
      for (int y = 0; y < BoardCells.Size; y++)
      {
        Vector2Int pos = new Vector2Int(x, y);
        if (IsValidDestination(context, pos)) cells.Add(pos);
      }
    }
    return cells;
  }

  // 行き先を選んでいる段階か（対象を選び終わった）
  public bool IsChoosingDestination(SkillContext context)
  {
    return UsesDestination && context.Targets.Count >= TargetCount;
  }

  // 対象選択中の表示（GetPreviewCells に、カーソル位置の範囲と選べない原因を足したもの）。
  // 行き先を選んでいる段階は、選んだ対象と行き先の候補を光らせる（範囲は塗らない）
  public SkillPreview GetPreview(SkillContext context, Vector2Int cursor)
  {
    if (IsChoosingDestination(context))
    {
      SkillPreview dest = SkillPreview.FromPanels(context.Targets);
      dest.Panels.AddRange(GetDestinations(context));
      return dest;
    }
    SkillPreview preview = SkillPreview.FromPanels(GetPreviewCells(context, cursor));
    if (!BoardCells.InBounds(cursor.x, cursor.y)) return preview;
    List<Vector2Int> area = GetAreaCells(context, cursor);
    if (area == null || area.Count == 0) return preview;
    preview.Area.AddRange(area);
    List<Vector2Int> blockers = GetAreaBlockers(context, cursor, area);
    if (blockers != null) preview.Blockers.AddRange(blockers);
    return preview;
  }

  protected bool IsAreaSelectable(SkillContext context, Vector2Int cursor)
  {
    List<Vector2Int> area = GetAreaCells(context, cursor);
    if (area == null || area.Count == 0) return false;
    List<Vector2Int> blockers = GetAreaBlockers(context, cursor, area);
    return blockers == null || blockers.Count == 0;
  }

  // 選べるマスが1つでもあるか（範囲を選ぶスキルは、どれかの向きで）
  protected bool HasAnyTarget(SkillContext context)
  {
    int saved = context.Orient;
    try
    {
      int orients = UsesArea ? Mathf.Max(1, OrientCount) : 1;
      for (int o = 0; o < orients; o++)
      {
        if (UsesArea) context.Orient = o;
        for (int x = 0; x < 9; x++)
        {
          for (int y = 0; y < 9; y++)
          {
            if (IsValidTarget(context, new Vector2Int(x, y))) return true;
          }
        }
      }
      return false;
    }
    finally
    {
      context.Orient = saved;
    }
  }

  public abstract void Execute(SkillContext context);
}
