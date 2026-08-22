using HarmonyLib;
using Manager;
using Monitor;
using UnityEngine;

namespace AquaMai.Mods.Fancy.GamePlay.CustomNoteTypes;

/// <summary>独立地雷 TouchC 类（C 传感器触摸，判定走继承的 NoteBase.Judge）。</summary>
public class MineTouchNoteC : TouchNoteC, ISelfJudgingMineNote
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
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

    public static MineTouchNoteC CreateFrom(TouchNoteC prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTouchNoteC, TouchNoteC>(prefab, parent, m => m.RunBaseAwake());
    }
}
