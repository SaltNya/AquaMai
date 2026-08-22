using Manager;
using Monitor;
using UnityEngine;

namespace AquaMai.Mods.Fancy.GamePlay.CustomNoteTypes;

/// <summary>
/// 独立地雷 TouchHold 类。
/// 判定：JudgeTotalResult 非虚无法 override，仍由 transpiler + MineNoteBehaviour 负责。
/// </summary>
public class MineTouchHoldC : TouchHoldC
{
    public override void Initialize(NoteData note)
    {
        base.Initialize(note);
        // 显式挂 MineNoteBehaviour：本类不实现 ISelfJudgingMineNote，判定反转靠全局
        // transpiler + behaviour 识别；原依赖 NoteSpeedApplyPatch（已删）经基类 postfix 挂载。
        // 必须在下方 GetComponent<MineNoteBehaviour>() 判断之前执行。
        CustomNoteTypes.EnsureMineBehaviour(this, note);
        CustomNoteTypes.ApplyMineTexturesToObject(gameObject);
        // 绝赞 touchhold（BRTHO，kind=MineTouchBreak → behaviour.IsTouchBreak）：
        // 用 break 风格素材 touchhold_break_0..3 + 外框 touchhold_break（用户 2026-08 补充）。
        if (GetComponent<MineNoteBehaviour>()?.IsTouchBreak == true)
        {
            CustomNoteTypes.ApplyBreakTouchHoldTextures(this);
        }
    }

    private new void Awake()
    {
    }

    internal void RunBaseAwake() => base.Awake();

    public static MineTouchHoldC CreateFrom(TouchHoldC prefab, Transform parent)
    {
        return MineNoteFactory.CreateFrom<MineTouchHoldC, TouchHoldC>(prefab, parent, m => m.RunBaseAwake());
    }
}
