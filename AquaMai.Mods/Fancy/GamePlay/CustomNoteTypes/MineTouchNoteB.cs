using HarmonyLib;
using MAI2.Util;
using Manager;
using Monitor;
using UnityEngine;

namespace AquaMai.Mods.Fancy.GamePlay.CustomNoteTypes;

/// <summary>
/// 独立地雷 Touch 类（B 类触摸）。
/// 判定/贴图自持：Judge / JudgeToolate override 反转（TouchNoteB.Judge 是 virtual override，可再 override）。
/// SE 保持原版 TouchNoteB 逻辑（不 override PlayJudgeSe）。
/// 地雷绝赞（MBTTP，kind=MineTouchBreak）：统计按 BREAK（由 TouchNoteB.EndNote transpiler 处理）。
/// </summary>
public class MineTouchNoteB : TouchNoteB, ISelfJudgingMineNote
{
    private bool _isMineTouchBreak;

    internal bool IsMineTouchBreak => _isMineTouchBreak;

    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        _isMineTouchBreak = CustomNoteTypes.GetNoteKind(note) == CustomNoteTypes.CustomNoteKind.MineTouchBreak;
        CustomNoteTypes.ApplyMineTexturesToObject(gameObject);
    }

    protected override bool Judge()
    {
        var result = base.Judge();
        if (result)
        {
            MineJudgeHelper.LogFirstUse();
            MineJudgeHelper.SetJudgeResult(this, MineJudgeHelper.Invert(GetJudgeResult()));
        }

        return result;
    }

    protected override bool JudgeToolate()
    {
        var result = base.JudgeToolate();
        if (result)
        {
            MineJudgeHelper.LogFirstUse();
            MineJudgeHelper.SetJudgeResult(this, MineJudgeHelper.Invert(GetJudgeResult()));
        }

        return result;
    }

    private new void Awake()
    {
    }

    internal void RunBaseAwake() => base.Awake();

    public static MineTouchNoteB CreateFrom(TouchNoteB prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTouchNoteB, TouchNoteB>(prefab, parent, m => m.RunBaseAwake());
    }
}
