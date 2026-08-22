# CustomNoteTypes TODO / 路线图

> 本文件汇总所有 TODO：① 对照 MajdataViewAlpha（制谱器）特色功能的实现差距；② 从 .cs 源码搬来的 TODO；③ MINE_NOTES 已知限制。
> 更新日期：2026-08-22（新增 §6 当前 bug 清单；SV/HS 已多轮迭代：scroll 模型 → 等效流速 → per-note 飞行时间表）。状态：✅ 已完成 / 🔶 部分完成 / ⬜ 未开始 / ➖ 不适用（制谱器侧功能，非游戏 mod 范围）。

## 1. 对照 MajdataViewAlpha 特色功能

来源：`MajdataViewAlpha_v0.4.2_260813a\MajdataViewAlpha\README.md`（"新增功能 / 扩展谱面标记"）。

| 功能 | 状态 | 说明 / 下一步 |
|---|---|---|
| `m` Mine（地雷键） | ✅ | 全类型独立类 + 判定规则（简单键到线自动 CP/触摸 Miss；hold 自动按住；slide 星星到底 CP/提前划完 Miss）+ 贴图 + 池。见 MINE_NOTES.md |
| Break Touch / TouchHold（绝赞 touch/touchhold） | ✅ | `BRTTP`/`BRTHO`（绝赞，命中=CP）+ `MBTTP`/`MBTHO`（地雷绝赞）。见 §2.1 |
| 非 C 区 TouchHold | 🔶 | B 传感器 touchhold 池已建（`_touchBHoldObjectList`）；C 传感器绝赞 touchhold 无独立池（回退原版池，见 §2.3） |
| `d` D 区音符（Tap/Hold/Slide + D 区端点接入 Touch Slide 判定） | ⬜ | 完全未实现。游戏侧需注册 D 区记录 + D 区传感器判定。参考制谱器 README：D 区 `s/z` 保留原版中段判定路线 |
| Touch Slide（普通键 ↔ A/B/C/D/E 区互连、无头 `!`/`?`、连段 `<`、绝赞头/绝赞路径 `b`） | ⬜ | 完全未实现（对应源码头部 TODO "Touch-slides / slides not ending in group A"）——**下一项移植目标** |
| `rp` / `rq` 反向圆弧 Slide | ⬜ | 未实现 |
| 无尾自旋星（`1$$`） | ⬜ | 未实现（源码头部 TODO "Spinning tailless star"） |
| conn. slide 独立追踪时长 | ⬜ | 未实现（源码头部 TODO "Individual tracing duration in conn. slides"） |
| **SV（实时变速，Majdata 风格）** | ✅ | `SVSP` 行（全局 + 类型化 `tap=2:1,hold=4:1`），∫sv 积分实时变速、飞行中变速、SV=0 冻结、判定锁音频。见 §5.3 |
| **HS（时间轴命令，Majdata 风格）** | ✅ | `HS` 行（全局 + 类型化）+ 行尾内嵌 HS（优先）。见 §5.3 |
| **BOUNCE（弹跳音符）** | ✅ | `BOUNCE` 行（时长：秒或 N:M），判定线→生成半径→判定线抛物线，外框弧心固定缩放跟随。见 §5.4 |
| COLOR / ALPHA / SPAWN / JLINE / 滤镜 / AUDIO / PVOVERLAY | ⬜ | 未实现（后续）；JLINE/滤镜/AUDIO/PVOVERLAY 等纯视觉命令仍为制谱器侧（➖） |
| 动态音符属性按类型分设（tap/hold/slide/star/touch…） | ➖ | 同上，Viewer 侧 |
| 谱面内速度覆盖（Hyper Speed 尾字段 x0.5 等） | ✅ | 本 mod 已支持（NoteSpeedContextPatch + ApplyOptionalHyperSpeed）；尾字段 = 内嵌 HS，优先于 `HS` 时间轴 |

## 2. 地雷键 / 绝赞系 TODO（从 .cs 源码搬来 + 补充）

### 2.1 绝赞 / 地雷 touch 系贴图与判定收尾

- ⬜ **MBTTP（地雷绝赞 touch）的绝赞贴图**：`ApplyMineTextures` 的 Touch 分支 `isBreak = typeName.Contains("Break")` 对 `MineTouchNoteB` 不生效 → MBTTP 现在显示的是普通地雷 touch 贴图（touch_mine 系）。应仿照 `MineTouchHoldC`（按 `behaviour.IsTouchBreak`）在 `MineTouchNoteB.Initialize` 里对 kind=MineTouchBreak 时贴绝赞样式（touch_break_mine 系列）。
- ⬜ **BRTHO（绝赞 touchhold）的 SE**：`TouchBreakNoteB` 有 `PlayJudgeSe` override（CP 音效），`TouchBreakHoldC` 没有——命中 CP 时音效仍是原版判定音。可加 override 或 postfix。
- ⬜ **C 传感器绝赞 touch / touchhold 无独立池**（回退原版池 → 判定/贴图都是原版）。现有代码注释明确"未建池时回退原版池"。

### 2.2 从源码头部搬来的 TODO（CustomNoteTypes.cs 头部 "TODO (?)" 块）

- ⬜ Touch-slides / 不终止于 A 区的 slide（= Touch Slide 与跨区 slide，见 §1）。
- ⬜ conn. slide 独立追踪时长（每条轨道的追踪时长独立设置）。
- ⬜ Non-C TouchHold（见 §1，B 已建池，C 绝赞缺池）。
- ⬜ Spinning tailless star（`1$$` 无尾自旋星）。
- ✅ Mine notes —— 已完成（整个项目本体）。

### 2.3 贴图 / 表现收尾

- ⬜ **NoteGuide 独立地雷 guide 池**：GuideObj 是共享池对象，普通 note 复用时会显示地雷提示圈（源码 CustomNoteTypes.cs `ApplyMineGuideTexture` 处注释 + MINE_NOTES §5）。
- ⬜ **fan `_effectSprites`（信号线）未染色**：只替换了贴图，没做地雷风格染灰（只有轨道线在 `MineSlideFan.UpdateAlpha` 里染灰）。
- ⬜ **素材对齐**：`star_double_mine` / `star_break_double_mine` / `touchhold_off` 等未被使用；fan 双星 eff（`star_break_double_eff_mine`）未接 fan 双星场景。
- ⬜ **fan 双星 eff**：`MineifyBreakStarEffect` 按 MultiSlide 选双星贴图，但 fan 星只走单星路径（SetSlideStar），双星场景未验证。

### 2.4 判定边界（低优先）

- ⬜ `MineTouchNoteB` 的 SE 未反转（TouchNoteB.PlayJudgeSe 实现不同，保持原版——确认是否需要）。
- ⬜ 自动播放（attract/demo）与 skip（快进）不反转判定。
- ⬜ slide 超窗/边角 case：完成度判定条是 `hitIndex >= count - 1`（与原版 grace 一致），"摸到最后一段但超窗"等边界未逐一验证。
- ⬜ 绝赞 touchhold 命中判定条：当前 `TouchBreakHoldC` 判定为"原结果非 Miss 三值 → CP"，与原版 touchhold 的判定窗口一致，未细分 good/great。

### 2.5 代码清理

- ⬜ `MineNoteBehaviour.Instances / TryGet` 无消费方，可清理（判定只需要 behaviour 本体）。
- ⬜ **GetSlideArrowNum 的 IL 注入已临时禁用**（源码注释 "Temporarily disabled: the IL injection here can crash on built-in slide notes"）——恢复前需排查崩溃原因。
- ✅ `slide_judge_dump.txt` 诊断 dump 与 `SlideDump` 系列（JudgeEntry/JudgeToolateEntry/NoteCheckEnd）——已整体删除。

## 3. 建议实现顺序（2026-08-18 用户指定优先）

1. **SV / HS**（✅ 已完成，Majdata 风格实时变速 + 时间轴命令）——见 §5.3。
2. **BOUNCE**（✅ 已完成）——见 §5.4。
3. **非 C 区启动/结束的 slide**（Touch Slide / slides not ending in group A）——**下一项**（用户指定）。
4. MBTTP 绝赞贴图、BRTHO SE、NoteGuide 独立池（小改动，穿插进行）。
5. COLOR / ALPHA / SPAWN（后续）。
6. D 区、`rp`/`rq`、`1$$`、conn. slide 独立追踪时长。

## 4. 相关文档

- `MINE_NOTES.md`：地雷键架构 + 31 条历史 bug（防回归必读）。
- `MajdataViewAlpha_v0.4.2_260813a\MajdataViewAlpha\README.md`：制谱器功能对照来源。
- `MajdataViewAlpha-main\`：原项目源码（SV/HS/SPAWN/BOUNCE/COLOR/ALPHA 语义参考）。

## 5. Alpha 指令游戏侧实现设计（2026-08-18 研究结论，暂未开工）

> 用户指示：先补全文档，稍后再开工。以下为研究结论 + 设计，开工时按此执行。

### 5.1 MajdataViewAlpha 语义（源码研究结论）

数据流：`maidata.txt`（`&inote_1=` simai 文本）→ `SimaiProcess.cs` 静态表 → `majdata.json` → View `Majson.cs` → 运行时消费（`SvController.cs` / `JsonDataLoader.cs`）。

| 命令 | View 侧语义 | 关键源码 |
|---|---|---|
| **SV** | **时间→滚动距离的重映射**：`distance = 4.8 - speed × (noteScrollPos - ∫sv(t)dt)`；**noteScrollPos = ∫sv(判定时刻) 在加载时一次算好**（`JsonDataLoader.cs:666-667`）。SV 是分段常数曲线（断点处跳变，非插值）；**全局 SV 不改变 tap/hold/star 的判定时刻**；**`SV*slide=` 类型化曲线单独驱动 slide 路径进度（`GetTypedOnlyProgress`），并通过 `CalJudgeTiming` 反解改变 slide 最终判定时间**（`SlideDrop.cs:1502-1521`）。支持按类型分设与 `NULL` 恢复 | `SvController.cs`（Load/GetCumulativeScroll/GetCurrentSV/GetTypedOnlyProgress）、`NoteDrop.cs:44-46`、`SlideDrop.cs:1497-1521`、`WifiDrop.cs:505-506`、`TouchSlideDrop.cs:94-95`、`JsonDataLoader.cs:666-667` |
| **HS** | **乘进基速的常量**（note 生成时一次性确定速度）：`note.speed = noteSpeed × GetHSpeedAt(类型, time)`；fallback 链：类型化条目 → cell 级裸 HS（`timing.HSpeed`）→ 1.0；typed `NULL`→1.0。按类型分设 | `JsonDataLoader.cs:2368-2380`（GetHSpeedAt）、`BuildHSpeedTimeline:2353+` |
| **SPAWN** | 音符视觉出生半径 `NoteDrop.spawnRadius`（**默认 1.225**） | `JsonDataLoader.cs:2318-2351` |
| **BOUNCE** | `NoteDrop.bounceDuration`：音符从判定线运动到出生半径后抛物线回弹 | `NoteDrop.cs:49-61` |
| **COLOR / ALPHA** | `NoteColorTint` 材质属性 `_NoteColor` / `_NoteAlpha`（色相替换染色 + 透明度） | `JsonDataLoader.cs:2514-2562` / Shader |
| 命令存储 | **命令以文本内联在 maidata.txt 时间线中（必须位于 cell 开头，`AlphaCommandBoundary.cs:7-22`），生效时间 = cell 起始时刻**；不是游戏 .ma2；`<SV*(1.5,8:1)>` 时间窗形式不支持（被静默消费）。编辑器 `Serialize`（`SimaiProcess.cs:455-583`）解析进静态表（671-682 行），Edit 序列化 `majdata.json` 传给 View（`MainWindowCore.cs:4210-4220, 4346-4352`）；`case "SV"/"HS"`（`SimaiProcess.cs:922-937`）只是语法校验 | `SimaiProcess.cs`、`AlphaCommandBoundary.cs`、`MainWindowCore.cs` |

### 5.2 命令如何到达游戏（待开工前与用户确认）

- 用户说明：**有转谱器把 maidata 转为 ma2**。开工时需确认转谱器是否把 `<SV*...>` 命令行写进 .ma2：
  - 若写入（哪怕以未知标签行形式）→ mod 用 `NotesRecord.addRecord(String)` 前缀拦截每行原文即可（游戏 `MA2Record.init` 对未知标签返回 false、行被优雅跳过，`<SV*...>` 行可安全共存；ma2 字段为 TAB 分隔）。
  - 若丢弃 → mod 在加载时读同目录的 maidata.txt 自己解析（`NotesReader.loadMa2(path)` 前缀可拿到文件路径）。
  - 时间关联：命令位于时间线 cell 开头 → 其时间 = 该 cell 的时间（转谱器编码方式决定具体格式，开工时拿用户实际谱面样例确认）。

### 5.3 SV / HS 游戏侧最终实现（2026-08-18 完成，MajdataViewAlpha 风格）

**ma2 语法**：
```
SVSP\t<bar>\t<grid>\t<倍率|N:M|NULL>            全局 SV（从该时刻起生效，NULL 恢复 1.0）
SVSP\t<bar>\t<grid>\ttap=2:1,hold=4:1,...       分类 SV（tap/star/hold/break/touch/touchhold/slide）
HS\t<bar>\t<grid>\t<倍率|N:M|NULL>              全局 HS（时间轴命令，Majdata HS* 风格）
HS\t<bar>\t<grid>\ttap=2:1,hold=4:1,...         分类 HS
```
- `N:M` = `60/BPM×4/N×M` 秒（Majdata `TryParseCommandDuration` 同款，`GetBPM_Time` 查该时刻 BPM）。
- 每音符行尾 `x0.5` 等 = 内嵌 HS（优先于时间轴 HS，向后兼容）。

**SV 语义（与 Majdata SvController 一致）**：
- 数据：`SvCurves`/`HsCurves`（类型键 `""`=全局 + tap/star/hold/break/touch/touchhold/slide）+ `SvClearTimes`（SV 分类 NULL 清除点）+ `SvCumCurves`（∫sv 预计算累计，按**有效曲线**：类型覆盖全局、清除后回退全局；二分查询）。
- 分类 NULL（Majdata 语义）：SV `tap=NULL` = 该类型重新跟随全局 SV；HS `tap=NULL` = 清除该类型额外倍率（1.0）。
- 每音符（loadMa2Main 后置预构建）：`SvScrollPosByNoteIndex`（=∫sv(T)）、`SvWindowByNoteIndex`（=D'×m_sv(T)，m_sv(T)≤0 时退化为 D'）、`SvTypeByNoteIndex`、`SpeedMultByNoteIndex`（=m_sv×m_hs，激活提前量用）。
- **飞行进度**（`SvRealTimeVisualPostfix`，`NoteBase.GetNoteYPosition` 后置）：
  ```
  V_5 = 1 − (∫sv(T) − ∫sv(now)) / W
  __result = StartPos + span×V_5 + V_9
  ```
  ——实时变速（飞行中倍率切换即时生效）、SV=0 段 ∫sv 冻结（音符视觉暂停）、到达判定线 = 判定时刻（判定锁音频）。相位 0/1（排队/渐入）保持原版（等效流速 D' 缩放）。
- **等效流速**（`SvApplyNoteSpeedOnInit`，`NoteBase.Initialize` 后置）：`D' = D/(m_sv(T)×m_hs(T))`、`StartMsec = T−2D'`（touch 用 `T−D'−D'/4`）。TouchNoteB/TouchHoldC 走等效流速（实时变速第一版不覆盖 touch）。
- **激活提前量**（`SvActivationLeadTranspiler`，patch `GameCtrl.UpdateCtrl`）：`apperMsecTap/Touch` 读取处按音符总倍率缩放（bug #35 教训）。
- HS 时间轴倍率并入 Initialize 缩放（尾字段已在 DefaultMsec 内，时间轴再乘）。
- 与 BOUNCE 正交：bounce 用原始时间抛物线，后置执行顺序保证 bounce 覆盖 SV 位置。

**历史教训**（详见 MINE_NOTES §6 #32-#36）：全局时钟重映射会带偏判定；soflan 是死代码；重映射必须同时覆盖 NoteCheck 的 scale（否则加速段全尺寸干站）；激活提前量必须随倍率缩放；外框同步必须用 NoteObj 父（launcher）世界 y 作基准。

### 5.4 BOUNCE 游戏侧最终实现（2026-08-18 完成）

**ma2 语法**（Majdata 分组语义，2026-08 扩展）：
```
BOUNCE\t<bar>\t<grid>\t<时长|NULL>                全局（展开到 tap/star/hold；each 双押在游戏里仍是 tap/star）
BOUNCE\t<bar>\t<grid>\ttap=8:1,hold=4:1,...       分类（tap/star/hold/break；NULL = 该类型不弹跳，覆盖全局）
```
时长 = 秒数或 `N:M`（Majdata `BOUNCE*8:1` 同款换算）。作用于 Tap/Star/Hold/Break（不含 touch/touchhold/slide；break 有分类曲线时用 `break` 键，否则回落基础类型）。

**语义（移植 Majdata NoteDrop.GetBounceDistance / TapBase.Update）**：
```
judgeOffset = now − T（ms），弹跳窗口 [−B, 0)
elapsed = clamp(judgeOffset + B, 0, B)；a = 8×(4.8−1.225)/B²
distance = 1.225 + 0.5×a×(elapsed−B/2)²    ← 判定线→生成半径→判定线抛物线
t = (distance−1.225)/(4.8−1.225)；bounceY = StartPos + span×t + V_9
```
- `BounceNoteVisualPostfix`（`NoteBase.GetNoteYPosition` 后置）：弹跳窗口内 __result = bounceY（含 V_9 音符速度偏移）；窗口前音符 alpha=0 + 光效 SetActive(false) + 外框 SetActive(false)；判定后全部恢复。
- **外框（NoteGuide 弧形提示线）**：弧心固定（localPosition 不动 = 屏幕圆心），`scale = clamp01(distance/4.8)`（Majdata tapLine 同款）——音符圆环与弧同心同径；弹跳窗口前外框隐藏（note 弹出前不可见，避免"从中间快速放大"的视觉）。光效/绝赞挂到 NoteObj 下跟随（`ReparentBounceEffects`）。
- **激活帧闪现**：`BounceHideOnInit`（base Initialize 后置）+ 5 个子类 Initialize 后置（Tap/Break/Hold/BreakHold/Star）在注册流程内（渲染前）隐藏——`SetGuideObject` 在 RegistNote 里 Initialize 之后调用（外框激活），用 `BounceHideGuideOnSet` 后置再隐藏；UpdateCtrl 注册晚于 UpdateNotes（激活帧会渲染一次）——必须在注册流程内完成隐藏（bug #37-#39 教训）。
- **判定**：不 patch——弹跳回到判定线时刻 = 音频判定时刻。Hold body 保持原版（用户确认 hold 弹跳没问题，不做整体弹跳处理）。

**COLOR / ALPHA / SPAWN（后续）**：
- COLOR/ALPHA：游戏侧音符材质（SpriteRenderer 颜色/材质 `_NoteColor`/`_NoteAlpha`）——地雷键已有按类型染色的先例（fan 轨道线染灰、`ApplyMineEffectTexture`）。
- SPAWN：音符出生位置偏移——游戏侧 `GetNoteYPosition` 系（需找 NoteBase 的 spawn 相关方法）。

### 5.4 用户指定优先级（2026-08-18）

1. SV、HS（先做）
2. 非 C 区启动/结束的 slide（Touch Slide / slides not ending in group A）
3. 其余（COLOR / ALPHA / SPAWN / BOUNCE）
4. 穿插小项：MBTTP 绝赞贴图、BRTHO SE、NoteGuide 独立池
5. D 区、`rp`/`rq`、`1$$`、conn. slide 独立追踪时长

## 6. 当前 bug / 已知问题（2026-08-22 更新）

> 按严重度排序。每项含根因与下一步。

### 6.1 游戏端（AquaMai.Mods）

- ⬜ **BRTTP 绝赞计分（修复无效，未解决）**：用户实测（2026-08-22 晚）"BRTTP修改没效果 结算也不算做break"。已试两版：①原类上方法级 patch（方法名 `TouchNoteBEndNoteScoreTranspiler` 不以约定前缀开头 → PatchAll 静默跳过，日志 Applying 列表缺席）；②嵌套类 `TouchNoteBEndNoteScorePatch`（`[HarmonyPatch(typeof(TouchNoteB), "EndNote")]` + 标准 `Transpiler` 方法名，已构建部署）——**仍无效**。待查方向（下次修时按序排查）：
  - a) 确认部署版 dll 里嵌套类存在 + Latest.log Applying 列表是否含 `TouchNoteBEndNoteScorePatch`（PatchAll 是否应用）；
  - b) transpiler 模式是否匹配：`Calls(AccessTools.Method(typeof(GameScoreList), "SetResult"))` 前 6 条指令内找 `Ldc_I4_4`——若 EndNote 里 SetResult 的 kind 参数不是直接 ldc.i4.4（如经局部变量/其它调用传递）则替换不命中；
  - c) `GetTouchScoreKind` 返回链：`TouchBreakNoteB.IsTouchBreak`/`TouchStarNoteB.IsBreakStar` 是否在 Initialize 正确固化（`GetNoteKind(note) == CustomNoteKind.TouchBreakStar`）；
  - d) 或 `TouchNoteB.EndNote` 的 SetResult 经 `SetPlayResult` 包装路径（isJudged 守卫）——需反编译确认实际调用图。
  - 另：判定显示仍是普通 touch 样式（未仿 `BreakNote.EndNote` 的 `InitializeBreak` 显示层），若需要绝赞框特效需另做。
- 🔶 **SV/HS 变速视觉对齐 MV alpha 未完美**：已多轮迭代（scroll 积分模型 → 等效流速 → per-note 飞行时间表 `NoteScrollTableByNoteIndex` + 加载期预计算），最近一轮修复激活时机（`SvScaleActivationLead(float lead, float leadDiv, NoteData)` 用加载期数据，14:52:59 部署）**待用户复测**。已知差异：
  - 负 HS 段：游戏端 W=d 正常下落 vs MV 出屏反向（罕见用法，暂不深究）。
  - hold 身体 scroll 抽搐（`HoldBodyScrollTranspiler` 9 指令模式含 ldarg，02:51 修复）待用户确认效果。
- ⬜ **UpdateAlpha clamp 根因未修**（防崩已加，视觉缺失未解决）：AYO 谱长链 slide 同屏峰值 66 条 × 19 箭头 = 1254 > 640 箭头池 → `_arrowObjectList`（640）/`_breakArrowObjectList`（640）耗尽 → 后续 slide 分不到箭头（日志 dispLane=703 max=640 / max=0）→ 箭头显示缺失。另 SlideRoot 池仅 24 个（+4 fan），耗尽时 `RegistNote` 返回 false 音符不注册。**游戏端原生限制，转换器无责**；需扩展池（ExtendNotesPool 已有先例）或按需分配。
- ⬜ **负 HS 段音符行为**：`SpeedMultByNoteIndex=mHs`（只 HS）已修，但负 mHs 段 `window=d` 保底，音符正常下落（MV 是出屏反向）——已知语义差异，未处理。
- ⬜ **空 catch 20+ 处**（CustomNoteTypes.cs）：静默吞异常，排查问题时无日志；建议统一改为 MelonLogger.Error（保留防崩意图）。
- ⬜ **SvMaxAt / SvMaxCurves 死代码**：仅诊断用途，播放路径已不再调用（per-note 表取代）；可删或保留供调试。
- ⬜ **MINE_NOTES.md:112 文档过期**：`IsMineSlideCompleted` 实际仍在（CustomNoteTypes.cs ~3187-3230），文档写"已划掉"。
- ⬜ **MineTouchHoldC.cs:21 break 材质首次激活不生效**：behaviour 由 postfix 在 Initialize 后挂载，首次激活时 `IsTouchBreak` 判断在挂载前执行。
- ⬜ **ParametricSlidePath 零长路径**：sum==0 时 NaN/除零（Libs/SlidePathGenerator）；`CalcTangentAngle` Acos 域风险（Libs）。
- ⬜ **SlideNoteDataHack 静默降级**：内置 slide 未命中注入模式时静默保留原调用（功能降级无日志）——可加一次性 Error 日志。

### 6.2 转换器（MuConvert-master）

- ⬜ **`tests/MuConvert.Tests.csproj` 仍 net10.0**：被 `MuConvert.slnx` 引用 → slnx 构建 NETSDK1045（主项目 csproj 构建不受影响；`EnumerateLines`/`Sort()` 是 net10 API，改 net9 会编译错——需要双目标或升级 SDK）。
- ⬜ **`1d` D 区起点未支持**：`Simai.g4` SLIDE_TYPE 无 `'d'`、`Enum.cs FromSimai` 无 case 'd' → D 区起点被 ANTLR 错误恢复吞掉 = 静默误转普通滑键（已知限制，游戏侧 D 区也未实现，见 §1）。
- ⬜ **Python 转换器（MaiConverter-0.14.6）负 &first 导出必崩**：`ma2note.py:597`——Python 版已被 MuConvert 取代，仅作记录。

### 6.3 待用户反馈/确认（挂起项）

- ⬜ **AYO `&first` 音频对齐**：已按原版行为部署 `&first=0`（无平移）版 `016019_03.ma2`，等用户实测确认（音乐为官方 acb/awb，非 track.mp3）。
- ⬜ **overdead 流内变速复测**：s{N} 流类型曲线 + per-note 表 + 激活修复后的综合效果（3,4 时停/闪现、7h/2 不受影响、hold 抽搐）。
