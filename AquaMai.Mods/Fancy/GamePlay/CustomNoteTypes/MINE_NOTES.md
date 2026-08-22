# Sinmai 地雷键（Mine Notes）完整开发文档

> **跨会话交接文档**：继续做地雷键 / 排查 bug 前先读这个文件。
> 最后更新：2026-08-18 —— 地雷键完整 + SV/HS（Majdata 风格实时变速）+ BOUNCE 弹跳完成。
> 本文件替代早期版本（旧文档里"Tap 独立其余换皮"等描述已过时）。
> **诊断 dump 已全部删除**（DumpSlideFan / DumpMinePoolObject / LogTextureColor / DumpNoteObjectTree / slide_judge_dump 全套 + 各点调试日志），只保留启动/每局一次的确认日志与 Error/Warning。

## 1. 关键位置速查

| 内容 | 路径 |
|---|---|
| 仓库根 | `C:\项目\AquaMai-main\AquaMai-main` |
| 地雷键主代码 | `AquaMai.Mods\Fancy\GamePlay\CustomNoteTypes\CustomNoteTypes.cs`（约 4000 行，含 SV/HS/BOUNCE） |
| 独立地雷类 | 同目录 `Mine*.cs`（14 个文件，见 §3.1） |
| 组件替换工厂 + 判定工具 | `MineNoteFactory.cs`（`ISelfJudgingMineNote` / `MineJudgeHelper` / `MineNoteFactory.CreateFrom`） |
| 行为组件（Hold/Slide 判定标记用） | `MineNoteBehaviour.cs` |
| 自定义 Slide 辅助库 | `Libs\`（SlideDataBuilder / SlideCodeParser / SlidePathGenerator / ParametricSlidePath / MaiGeometry / CustomSlideNoteData） |
| 游戏 DLL（编译引用） | `Libs\Assembly-CSharp.dll`、`Assembly-CSharp-firstpass.dll`、`0Harmony.dll`、`Mono.Cecil.dll` |
| 构建 | `build.ps1` → `Output\AquaMai.dll` → 拷到游戏 `MelonLoader\Mods\` |
| 地雷贴图（运行时读） | 游戏运行目录 `LocalAssets\CustomNoteTypes\*.png`（`FileSystem.ResolvePath`） |
| 贴图源素材 | 仓库根 `地雷键-绝赞touch\*.png`（58 个，全部转小写 key 加载） |
| 测试谱 | 仓库根 `fan_mine_test.ma2`（fan slide 专项，8 块对照）；`016013_04.ma2`（全地雷）；转换脚本 `convert_to_mine.py` 等 |
| IL dump 参考 | 仓库根 `ILDUMP.txt`（GameCtrl.RegistNote 完整 IL） |

## 2. 构建与部署

```powershell
cd C:\项目\AquaMai-main\AquaMai-main
.\build.ps1          # Release → Output\AquaMai.dll
```

- **本机 dotnet SDK 缺 Workload 定位器目录**（`Sdks\Microsoft.NET.SDK.WorkloadAutoImportPropsLocator` 不存在）→ `dotnet build` / `dotnet restore` **静默失败**（MSB4276，报"0 错误"但 exit 1）。
- **build.ps1 已改为直调 MSBuild.dll**：`dotnet <sdk>\MSBuild.dll AquaMai.slnx /t:Restore,Build /p:MSBuildEnableWorkloadResolver=false /v:m`。**不要改回 `dotnet build`**。
- 单独编译排查：`dotnet <sdk>\MSBuild.dll AquaMai.Mods\AquaMai.Mods.csproj /t:Build /p:Configuration=Release /p:MSBuildEnableWorkloadResolver=false /v:m`。
- `dotnet msbuild` 也会静默失败，必须直调 MSBuild.dll。

## 3. 地雷键架构（当前完整形态）

### 3.1 独立类清单（14 个 .cs）

| 类 | 基类 | 判定处理 | 覆盖的 MA2 类型 |
|---|---|---|---|
| `MineTapNote` | TapNote | **自反转**（override Judge/JudgeToolate/PlayJudgeSe） | MNTAP / MXTAP |
| `MineBreakNote` | BreakNote | 自反转 | MBTAP / MZTAP |
| `MineStarNote` | StarNote | 自反转 | MNSTR |
| `MineBreakStarNote` | BreakStarNote | 自反转 | MBSTR |
| `MineTouchNoteB` | TouchNoteB | 自反转（SE 不反转，保持原版） | MNTTP / MBTTP |
| `MineTouchNoteC` | TouchNoteC | 自反转 | C 传感器触摸 |
| `MineHoldNote` | HoldNote | **transpiler 反转**（`JudgeTotalResult` 非虚） | MNHLD |
| `MineBreakHoldNote` | BreakHoldNote | transpiler 反转 | MBHLD |
| `MineTouchHoldC` | TouchHoldC | transpiler 反转 | MNTHO |
| `MineSlideRoot` | SlideRoot | transpiler 反转（`SlideRoot.Judge` 非虚） | MNSCR/MNSCL/MNSI_/MNSSS 等 |
| `MineSlideFan` | SlideFan（继承 SlideRoot） | transpiler 反转（继承的 Judge 非虚） | MNSF_ 等 fan slide（Wi-Fi） |
| `TouchBreakNoteB` | TouchNoteB | **命中强制 CP**（非地雷） | BRTTP（绝赞 touch） |
| `MineNoteFactory` | - | 组件替换工厂 + `ISelfJudgingMineNote` + `MineJudgeHelper` | - |
| `MineNoteBehaviour` | MonoBehaviour | 判定标记（Hold/Slide 用）+ 贴图 | - |

- **判定语义（2026-08 最终规则，当前）**：
  - **简单键**（tap/star/break/hold/touch/touchhold）：**按键到达判定线那一帧起自动判 Critical Perfect（不需要玩家碰），之后不再有任何 late/miss 判定**——只取 autoplay 的"到线判 CP"时机，**不改变其它任何行为**（不自动推进、不忽略触摸）。玩家在判定线之前碰到 → 正常判定路径（刚好那帧 → CP；打偏 good/great → Miss）。
  - **hold / touchhold / breakhold**：**不碰 → 像 autoplay 一样自动按住（头自动 CP、条自动按满）直到判定期间最后（TailMsec）判 CP；判定期间（判定线前 ~16.7ms 到尾部）被碰到 → 立即 Miss（直接收尾）**。实现：`MineHoldAutoPlayTranspiler`（patch `HoldNote/BreakHoldNote/TouchHoldC` 的 `NoteCheck`/`JudgeTotalResult`/`SetAutoPlayJudge`，把 `IsAutoPlay()`→`IsAutoPlayOrMineHold`、`AutoJudge()`→`MineHoldAutoJudge`）+ `MineHoldTouchCheck`（NoteCheck 后置检测触摸 → 登记 `MineHoldTouched` + 写 JudgeResult=TooLate + 直接 EndNote；`ApplyMineJudgeTiming` 对登记对象强制 Miss，防 JudgeTotalResult 覆盖）。⚠️ HoldNote **override 了 SetAutoPlayJudge**，打在 NoteBase.SetAutoPlayJudge 上的通用自动 CP 对 hold 不生效（历史 bug #26），必须单独 patch。
  - **slide / fan（用户 2026-08 最终规则）**：**星星跟着轨道滑到最后（玩家没划完轨道）→ Critical Perfect；玩家提前划掉全部轨道 → Miss**。实现：`ApplyMineJudgeTiming` slide 特判（`IsMineSlideCompleted`：SlideRoot `_hitIndex >= _hitAreaList.Count-1`、fan 三条线各自达标）→ 划完 TooLate(14)、没划完 Critical(7)；`MineJudgeInversion` 目标方法含 `SlideRoot.Judge/JudgeToolate`（后置反转，不受 br.s 分支跳过影响——bug #23 已修）；`SlideGraceJudgeTranspiler` 把 NoteCheck 末尾的 13 宽限写入替换成 helper（地雷保持 14=Miss，非地雷 13）。
  - 绝赞 touch 命中 = Critical Perfect、未命中 Miss（独立类，不受影响）。
  - 旧规则（已废弃）：① 打中 = Miss、不打 = CP；② 刚好打中(CP 帧) = CP、其余 = Miss；③ slide 自动播放 autoplay 化。
  - 实现：
    - 自动 CP：`MineAutoJudgePostfix`（`NoteBase.SetAutoPlayJudge` 后置）——未判定 && 到判定线前 4.17ms（原版 autoplay 同款门槛）→ 直接写 `JudgeResult = Critical` + PlayJudgeSe（`AutoJudge()` 在非 autoplay 模式返回 TooFast，不能用，必须直接写 Critical）。
    - 触摸映射（后置反转，`MineJudgeHelper.Invert`）：`timing == Critical ? Critical : TooLate`——自反转类 override + 非自反转类（hold/touchhold）后置 transpiler 都走它。
- **自反转类**实现 `ISelfJudgingMineNote`：全局判定 transpiler 对它们**放行**（`ApplyMineJudgeTiming` 里 `if (note is ISelfJudgingMineNote) return timing;`），不挂 MineNoteBehaviour。
- **非自反转类**（Hold/Slide）：必须挂 MineNoteBehaviour（transpiler 靠 `behaviour.IsMine` 识别反转），贴图由类自己负责。

### 3.2 MA2 类型注册（`OnAfterPatch` 行 283）

- `Ma2fileRecordID.s_Ma2fileRecord_Data` 数组尾部追加地雷记录（Traverse 拿私有字段 + 反射构造 struct）。
- 命名：`MN`=NM 地雷、`MB`=BR 地雷、`MX`=EX、`MZ`=BX、**`BR`=绝赞系（非地雷，命中强制 CP）**：`BRTTP`=绝赞 touch（原版绝赞 touch，TouchBreakNoteB）、`BRTHO`=绝赞 touchhold（新增，TouchBreakHoldC）、**`MBTTP`=地雷绝赞 touch（恢复原名）、`MBTHO`=地雷绝赞 touchhold（新增，MineTouchHoldC+break 素材）**、`MNTTP`=地雷 touch、`MNTHO`=地雷 touchhold。
- 原生 slide 变体循环注册（MNSCR/MBSCR/MZSCR...）。
- `MineToBaseMap`（行 141）：类型名 → 基础类型（MNTAP→NMTAP 等），`BRTTP`→NMTTP、`MBTTP`→NMTTP 特殊处理。

### 3.3 解析与地雷标记

- `FindIDPrefix`（行 536）：拦截 `Ma2fileRecordID.findID`，地雷类型返回基础类型 ID（原版解析器无感），`PendingMineFlags`/`PendingTouchBreakFlags` 队列按谱面顺序入队。
- `ApplyOptionalHyperSpeed` postfix（行 870）：出队写 `NoteKinds[note.indexNote] = CustomNoteKind`（Mine / TouchBreak / MineTouchBreak / None）。

### 3.4 独立对象池（`CreateNotePoolPostfix` 行 1131）

- `MinePools`：`Dictionary<GameCtrl, Dictionary<string, object>>`，**14 个池字段**：
  `_tapObjectList _holdObjectList _breakHoldObjectList _starObjectList _breakStarObjectList _breakObjectList _touchBObjectList _touchCTapObjectList _touchBHoldObjectList _touchCHoldObjectList _slideObjectList _fanSlideObjectList _arrowObjectList _breakArrowObjectList`（后两个是 slide 轨道箭头池，640 个）。
- `AddMinePoolWithFactory<TBase>`（行 1167）：**泛型参数必须显式给基类类型**（`<TapNote>` 等）——游戏字段是 `List<基类>`，若按 lambda 推断成 Mine* 子类会 InvalidCastException（历史 bug #6）。`applyTextures:false` 用于 slide/fanSlide（贴图会遍历子物体误伤内部星）。
- `AddArrowPools`（行 1200）：箭头池对象直接贴 `slide_mine` / `slide_break_mine`（不走 ApplyMineTextures 分支——SpriteRenderer 不匹配任何类型分支）。
- 绝赞 touch 独立池：`TouchBreakPools`（只 `_touchBObjectList`；C 传感器绝赞回退原版池）。
- 池对象：`SetActive(false)` + `set_ParentTransform`（原版 CreateNotePool 同款）+ 组件替换/贴图。
- 生命周期：`OnGameStart`/`OnRelease` 清空所有字典/集合。

### 3.5 取池重定向（`RegistNote` 三件套）

- `RegistNotePrefix`（行 1560）：读 `NoteKinds` → `_activeMineFields`（地雷）/ `_activeTouchBreakFields`（绝赞），按 `note.type.getEnum()` 映射（`GetMineFieldsForNoteType` 行 1488：Slide 系返回 4 字段含箭头）。
- `RegistNoteTranspiler`（行 1527）：`ldfld _xObjectList` → `ldstr 字段名` + `call GetXObjectList(GameCtrl, string)`。
- **访问器签名必须是 `(GameCtrl instance, string fieldName)` 双参数**（历史 bug #5）。
- `GetMinePoolList`：地雷→绝赞→原版三级查找。

### 3.6 判定（`MineJudgeInversion` 行 1079）

- TargetMethods 8 个：`NoteBase.Judge / JudgeToolate`、`TouchNoteB.Judge`、`SlideRoot.Judge / JudgeToolate`、`HoldNote / BreakHoldNote / TouchHoldC .JudgeTotalResult`。
- 全部同一 IL 模式：算出 `ETiming` 后 `stfld JudgeResult`（或 JudgeHeadResult）。transpiler 在 stfld 后插：
  ```
  stfld JudgeResult          ← 原指令保留（分支目标有效）
  ldarg.0
  ldstr "JudgeResult"
  call ApplyMineJudgePostWrite(object, string)
  ```
- **⚠️ 必须是"后置反转"（历史 bug #23）**：`SlideRoot.Judge` 的两条路径（`GetSlideJudgeTiming` 与 autoplay）都是 `br.s` 直接跳到 stfld——**前置插入（ldarg.0; call）会被分支跳过**，原值直写（dump 表现：JudgeEntry 每帧出现但 JudgeTiming 从不出现，NoteCheckEnd 是原始 FastGreat/FastGood 等）。后置插入保留原 stfld 身份（分支目标不变），插入代码在 stfld 之后必然执行；`ApplyMineJudgePostWrite` 读回刚写的值 → `ApplyMineJudgeTiming` 决定 → 写回。
- **⚠️ 千万不要加 `dup`**（历史 bug #7：残留 timing → InvalidProgramException → patch 全挂）。
- `ApplyMineJudgeTiming`（行 ~1183）：`ISelfJudgingMineNote` 放行；否则 `GetComponent<MineNoteBehaviour>().IsMine` 才反转（MineSlideRoot/MineSlideFan 按类兜底，历史 bug #22）。
- **规则（当前）**：`InvertMineTiming` → `MineJudgeHelper.Invert`：`Critical → Critical`，其余（含 TooFast/TooLate/End、good/great）→ `TooLate(Miss)`。**slide 不再有完成度特判**——判定只由原版 timing 决定（划完刚好=CP、划完但偏=Miss、没划完=Miss）。`IsMineSlideCompleted` 已删除。
- **`SlideGraceJudgeTranspiler`（行 ~1140）**：`SlideRoot.NoteCheck` / `SlideFan.NoteCheck` 末尾有原版宽限写入 `ldc.i4.s 13; stfld JudgeResult`（"滑完全部轨道但略迟 → LateGood/GOOD"）——地雷的非 Critical 一律 Miss，所以宽限写入的地雷结果保持 Miss(14)，非地雷保持 13。transpiler 把 `ldc+stfld` 换成 `call ApplyMineSlideGraceJudge`（注意保留前一条 `ldarg.0` 作 this，标签/异常块转移到 call）。
- **Hold/TouchHold 只反转 JudgeTotalResult 最终结果**，头判保持原版（JudgeHoldHead 反转会破坏体判逻辑）。
- 反转后的 JudgeResult 被原版用于：计分（SetPlayResult）、显示（EndNote→JudgeGrade.Initialize）、特效、SE。
- **GameManager.AutoJudge 只在自动播放调用**，手动判定不走它（早期 patch 它完全无效）。
- **HoldOn 亮态贴图**（`HoldOnMineTranspiler` 行 1015）：HoldOn 非虚无法 override，transpiler 把 `SpriteRenderer.set_sprite` 换成 `ldarg.1; call SetHoldSpriteWithMine(SpriteRenderer, Sprite, bool on)`，按住/释放时重贴 hold_mine_on/hold_mine 等。**⚠️ 被替换的指令若带分支标签/异常块，必须把 `labels`/`blocks` 转移到第一条替换指令**（历史 bug #17）。

### 3.7 贴图系统

- `LoadMineTextures`（行 432）：启动从 `LocalAssets/CustomNoteTypes/*.png` 加载，**key = 小写文件名**。
  - **文件名两位数字的必须 `ToString("D2")` 拼 key**（`slide_fun_mine_00.png` 的 key 是 `slide_fun_mine_00`，拼成 `_0` 会 Missing（历史 bug #13））。
- `ApplyMineTextures`（私有）：按**类名**分支——`MineTapNote`→Tap 分支、`MineHoldNote`→Hold 分支、`MineTouchHoldC`→TouchHold 分支（在 Hold 之前判断）等。子物体名 StartsWith 匹配（"Tap"/"Hold"/"HoldEnd"/"Star"/"Point"/"Up"/"Right"/"Down"/"Left"/"Just"/"SlideLaneStar"/"SlideArrow"/"Effect"）。
- **Slide 贴图三件套**（关键，别乱改）：
  - 轨道箭头：独立地雷箭头池（`_arrowObjectList`/`_breakArrowObjectList`）+ `MineSlideRoot.SetEach` override 重贴（**SetEach 会给箭头赋原版 NormalSlide/EachSlide sprite，覆盖池贴图**——必须重贴）。
  - 移动星：`ApplyMineSlideStarTextures`（行 1295）精准贴 `_starNote`→star_mine、`_breakStarNote`→star_break_mine（不遍历子物体）；`MineSlideRoot.Initialize` 和 `SetEach` override 里调用。
  - **slide 本体不贴图**（池创建 applyTextures:false + behaviour applyTextures:false）：`ApplyMineTextures` 遍历子物体会误伤内部星。
- `ApplyMineTexturesToObject`（行 2103）：`GetComponent<NoteBase>() ?? GetComponentInChildren<NoteBase>(true)`，拿不到回退 Transform + Warning。
- **Sprite 缓存**（行 1923 `CreateSpriteFromTexture`）：
  - **ppu 必须沿用 `original.pixelsPerUnit`**（固定 1f → hold 条等细长贴图尺寸/拉伸全错）。
  - **9-slice border 按新贴图尺寸等比换算**（`original.border × 新纹理/原rect`）——原版 hold 条是 Sliced 渲染（中间段拉伸），border 直接套用会切错位置。
  - `SpriteMeshType.FullRect`（Tight 会裁剪细长贴图）。
  - **缓存 key 必须含完整几何**（name+rect+pivot+border+ppu）——图集 sprite 的 name 可能为空，只按 name 缓存会串用（历史 bug #9）。
  - 缓存解决卡顿（原来每 note 每子物体新建 Sprite，touchhold 7+ 子物体时 GC 爆炸）。

### 3.8 组件替换（`MineNoteFactory.CreateFrom<TNew,TOld>`）

Unity 组件类型无法原地更换，流程：
1. `Instantiate(prefab.gameObject, parent)` 克隆；
2. `SnapshotFields(old)` 快照游戏逻辑字段（**跳过 `UnityEngine.*` 命名空间字段**——m_CachedPtr 等 native 绑定复制会悬垂）；
3. `DestroyImmediate(old)`；
4. `AddComponent<TNew>()` —— 触发 Awake，此时字段还是 null、原版 Awake 会 NRE → 各 Mine* 类用 `new` 遮蔽空 Awake；
5. `RestoreFields` 恢复；
6. `postCreate` 补执行基类 Awake（多数 `base.Awake()`；**MineSlideRoot 的 SlideRoot.Awake 是 private**，用反射 `GetMethod("Awake", NonPublic|Instance).Invoke`——Awake 会实例化内部星并缓存组件引用；**MineSlideFan 的 SlideFan.Awake 也是 private**，同样反射调用）。

### 3.9 NoteGuide（提示圈：蓝色外框 + 判定前橙色闪光）地雷化

- 现象根源：普通 note 的"蓝色外框 + 判定前橙色闪光"其实是 **NoteGuide**（`NoteBase.GuideObj`，`_guideObjectList` 共享池对象，不是 note 的子物体——所以 ReplaceChildSprite 永远匹配不到）。
- `ApplyMineGuideTexture(NoteBase)`（行 1969）：Traverse 读 `GuideObj`，把其下所有 SpriteRenderer 贴成 `mine`（Mine.png），并登记进 `MineGuides`（HashSet）。
- **`NoteGuide.SetColor` 每次被调用都会把主体 sprite 重置回原版**（每个 SetEach 都调）→ `NoteGuideSetColorPostfix`（行 2044）在 SetColor 后把已登记的 guide 重新贴回 `mine`。
- 调用点：`ApplyMineTexturesToObject` 末尾（普通地雷 note）+ `ApplyFanStarGuideTextures`（fan 的星 `_starObjs/_breakStarObjs/_baseStarObjs` 上的 NoteBase）。
- ⚠️ **已知限制：GuideObj 是共享池对象，普通 note 复用同一 guide 时也会是地雷圈**（TODO：独立地雷 guide 池才能彻底隔离）。

### 3.10 绝赞光效（橙色闪光）替换

- 根源：`BreakNote.EffectSprite`（private SpriteRenderer 字段，BreakNote/BreakStarNote/BreakSlide 都有），由 SetEach/SetMulti/SetSlideStar/SetSprite 赋 `*_EFF_*` 原版贴图 + 橙色（NotesEffectColorTable.BreakColor）。
- 替换点（全部带"重贴后会被重置"的坑，必须 postfix/override）：
  - **MineBreakNote.SetEach override**（MineBreakNote.cs 行 59）：`EffectSprite` → `tap_break_eff_mine`。
  - **BreakSlide.SetSprite postfix**（行 2010）：已登记 `MineBreakSlides` 的 → `slide_break_eff_mine`。登记点：`AddArrowPools`（break 箭头池创建时）。
  - **BreakStarNote.SetMulti postfix**（行 ~2035，**非虚方法**）：`multiFlag` 双星 → `star_break_double_eff_mine`、单星 → `star_break_eff_mine`。⚠️ **Initialize 对单星/双星都会调 SetMulti**（`multiFlag = child.Count >= 2`），单星分支之前漏了（历史 bug #18）。
  - **BreakStarNote.SetSlideStar postfix**（行 ~2050，**非虚方法**）：slide/fan 内部单星 → `star_break_eff_mine`。
- **判断用登记表 `MineBreakStars`（HashSet<BreakStarNote>），不能 `is MineBreakStarNote`**：slide/fan 内部星是原版 BreakStarNote 实例（来自 slide 自己的 prefab，不是地雷池），`is` 判断永远 false（历史 bug #18）。
- 登记 + 立刻替换（`MineifyBreakStarEffect`，行 ~2018）：`MineBreakStarNote.Initialize`（standalone，SetMulti 在 base 里已执行）、`ApplyMineSlideStarTextures`（slide 内部 `_breakStarNote`）、`ApplyMineFanSlideTextures`（fan `_starObjs/_breakStarObjs/_baseStarObjs`）——postfix 在登记前触发时由这里兜底，登记后再触发由 postfix 处理。
- 工具：`ApplyMineEffectTexture(SpriteRenderer, key)`（行 ~2012）。

### 3.11 Fan slide（Wi-Fi 扇形滑）最终贴图

- **fan slide 分 LR**：`_spriteLines` = 11 组 × 2 = 22 个（Fan{N}L root + Fan{N}R child[1]），`_effectSprites` = 22（每组 child[0] + child[1].child[0]）。
- 轨道线：`slide_fun_mine_` + `(i / 2 % 11).ToString("D2")` → `slide_fun_mine_00..10`（**i/2 才成对，i%11 会让 L/R 错位**——历史 bug #12）。
- 信号线：`slide_fun_eff_mine_` + D2 → `slide_fun_eff_mine_00..10`。
- 星：`_spriteStars` / `_baseSpriteStars` → `star_mine`（普通）/ `star_break_mine`（break，读 `BreakFlag`）。
- **轨道线是白色贴图，用 `MineSlideFan.UpdateAlpha` override 染灰**（`new Color(0.5f, 0.5f, 0.5f, c.a)`，保留 base 算好的 alpha 淡入淡出）——不要改贴图本身。
- 调用点：`MineSlideFan.Initialize` / `SetEach` override → `ApplyMineFanSlideTextures(this)`（行 1330，base 执行完原版贴图赋值后再贴）。
- 星自带 NoteGuide 提示圈 → `ApplyFanStarGuideTextures`（行 1405，见 §3.9）。
- 缺贴图时一次性 Warning：`Missing fan textures: 'slide_fun_mine_00'`。
- **fan 星/轨道/eff 全部用自有 png，不用 slide 系贴图**（star_mine/slide_mine 只属于普通 slide）。

## 3.12 SV / HS（scroll 位置驱动模型 + 类型分组，2026-08-23 恢复；对齐 MV alpha：时停/闪现/冻结）

**ma2 语法**（`NotesRecord.addRecord` 前缀拦截，未知标签行游戏自动跳过）：
```
SVSP\t<bar>\t<grid>\t<倍率|N:M|NULL>            全局 SV
SVSP\t<bar>\t<grid>\ttap=2:1,hold=4:1,...       分类 SV（tap/star/hold/break/touch/touchhold/slide）
HS\t<bar>\t<grid>\t<倍率|N:M|NULL>              全局 HS（时间轴命令）
HS\t<bar>\t<grid>\ttap=2:1,hold=4:1,...         分类 HS
```
`N:M` = `60/BPM×4/N×M` 秒（`ParseSpeedValue`，BPM 用 `NotesReader.GetBPM_Time(msec)`）；行尾 `x0.5` = 内嵌 HS（优先）。

**分类 NULL（Majdata 语义）**：SV 的 `tap=NULL` = 该类型从该时刻起**重新跟随全局 SV**
（`SvClearTimes` 清除点 → `GetCurveMultAt`/`BuildEffectiveCurve` 回退全局曲线）；HS 的
`tap=NULL` = 清除该类型的额外倍率（按 1.0 入曲线）。

**数据流**：
- `AddRecordSvParsePrefix` → `PendingSvSegments`/`PendingHsSegments` → `LoadMa2MainSvInjectPostfix` → `BuildSpeedCurves`（`SvCurves`/`HsCurves`，键 `""`=全局 + tap/star/hold/break/touch/touchhold/slide；SV 的 NULL 进 `SvClearTimes`）。
- `BuildSvCumulatives`：每类型累计表 `SvCumCurves[key]=(Msec, Mult, Cum)`，**只积 SV**（∫sv，段前 ∫=t；MV：HS 不进积分，只进每音符 speed）；`SvMaxCurves[key]`=单调 max 前缀（诊断用，出现判断已不用——2026-08-22 曾因 `SvMaxAt` 段前分支返回表首累计值导致"反转着出来"，改 return msec 后仍弃用）。
- 每音符预构建：`SvTypeByNoteIndex`（`ResolveSpeedType`）、`SvScrollPosByNoteIndex = ∫sv(type, T)`（判定时刻 scroll）、`SpeedMultByNoteIndex = mHs`（**speed 只由 HS 决定**，MV 语义；激活提前量用）、`HsMultByNoteIndex = mHs`（W 用）、`SvTailScrollPosByNoteIndex = ∫sv(type, T+Len)`（hold 尾，`note.end != null` 时）、`EachChildByNoteIndex`（each 伙伴 indexNote 列表）。

**下落位置**（`SvRealTimeVisualPostfix` patch `NoteBase.GetNoteYPosition`，scroll 公式全程驱动）：
```
v5 = 1 − (scrollPos − ∫sv(type, now)) / W      W = d / mHs（d=原始 DefaultMsec；mHs≤0.0001→W=d）
```
- **恒速窗口检测**（无变速 100% 原版）：`mHs≈1 且 |scrollPos − ∫sv(type, T−W) − W| < 0.5` → return 原版（渐入/下落/外框/速度偏移全原版，`SvFadeScale` 同款检测）。
- SV=0 冻结段：∫sv(now) 停 → v5 停 → **真时停**（停在当前位置）；恢复后继续 → "时停着一个一个出现，全部出现后一起下落"。
- v5<0（未达出现阈值/负 SV 段）：**钉在生成圈 StartPos 排队**（`FStartPos`，1814 行 `__result = FStartPos`——避免原版时间驱动下落/横穿；排队音符渐入动画照常）。
- v5>1：clamp 1（防 y<0 对称面横穿）；判定后 `now≥T` 且 hold 系（`HoldNote`/`BreakHoldNote`）锁头 v5=1（对齐 MV HoldDrop：判定后头强制判定线，负 SV 不反向拉回头）。
- SPAWN：spawnR 提前计算，`spawnFloor = (spawnR − 1.225)/3.575`，`v5 < spawnFloor` 钳制（R=−1.225 → 阈值提前 1.685×W，进场 v5=−0.685 起步，对齐 MV `HasReachedSpawnRadius`）。
- 外框 NoteGuideTrans.localScale = 0.25+0.75×v5（门 `now ≥ T−defaultMsec`，s<0.25 clamp）。
- 变速段 scale 渐入：`SvFadeScale` + `SvFadeScaleAfterNoteCheck`（patch `NoteBase.NoteCheck`——NoteCheck 每帧重设 localScale 会覆盖 postfix 设置的 scale，必须在其后重设）——0.5 段成形后慢速下落、SV=0 排队半透明、50 段猛冲成形。

**等效流速层**（`SvApplyNoteSpeedOnInit`/`SvApplyNoteSpeedOnTouchInit`，`NoteBase.Initialize`/`TouchNoteB.Initialize` 后置）：
```
mult = SpeedMultByNoteIndex[index]（判定时刻 mSv×mHs 缩放 DefaultMsec；m≤0.0001 或 |m−1|<0.001 不缩放）
newD = D / mult；DefaultMsec = newD；StartMsec = T − 2×newD（touch：T − newD − newD/4）
```
→ 渐入/激活窗口同步缩放（下落位置本身由上面 scroll 公式驱动）。

**激活提前量**（`SvActivationLeadTranspiler` patch `GameCtrl.UpdateCtrl`）：`apperMsecTap/Touch`
读取替换为按音符总倍率 m 缩放（bug #35）：激活时刻 = T − 2D'（tap）/ T − 1.25D'（touch），
与渐入窗口起点精确对齐（低流速段音符不会激活太晚导致"没有渐入直接下落"）。

**Each 双押**：伙伴倍率差 ≥ 0.001 → 隐藏连接辅助条（`SvApplyNoteSpeedOnInit` 内 `HideEachGuide`）。

**hold 身体 scroll 驱动**（`HoldBodyScrollTranspiler` patch `HoldNote.Execute`/`BreakHoldNote.Execute` + `SvHoldTailProgress`）：
原版身体进度 `num4 = 1−(TailMsec−adj−now)/DefaultMsec` 是**纯时间驱动**——SV 交替段（如
`{64}6x/4hx[8:4],<SV*hold=-3.0>,<SV*hold=4.0>,...` 来回 23 次，谱师用 SV 做 hold 前后抽搐特效）
影响不了身体（用户实测视觉匀速）。transpiler 把 num4 计算（`ldc.r4 1, ldc.r4 1, ldloc, ldarg.0,
ldfld DefaultMsec, div, sub, mul → stloc` **9 指令模式**，含 ldarg.0——曾漏掉导致永不匹配）替换为
`SvHoldTailProgress(this)`：
```
p = 1 − (∫sv(T+Len) − ∫sv(now)) / D'    （D' = 缩放后的 DefaultMsec 当前值）
```
- 只积 SV；负 SV 段 s 递减 → p 摆动 → hold 条抽搐；SV=0 冻结段 p 停 → 身体时停；0.5 段慢速收束
- 无 SV 表时 s(t)=t → p = 1−(T+Len−now)/D' = 原版公式（行为不变；D' 已含等效流速缩放，
  身体速度与头一致）
- 表缺失：`SvTailScrollPosByNoteIndex`（∫sv(T+Len)，`note.end != null` 时预构建）
- 失败兜底：pattern 未命中打 Warning "HoldBodyScrollTranspiler: num4 pattern not found..."

**已知限制**：负 HS 段 tap/star 出屏（W=d 保底）；全局曲线（无类型）影响所有音符=MV 语义（谱师应写类型化
SV 或 `<SV*1.0>` 恢复）；历史：等效流速模型（2026-08-23 04:08 版，`CustomNoteTypes.svhs-v2-equivalent.cs.bak`）无法表达 0 倍冻结/段内变速，用户选回 scroll（本模型）。

**可重叠流局部曲线（ma2 扩展语法，2026-08-23）**：转换器把每条 `@{N}` 流分配类型键 `s1`/`s2`/...，
流内 SV/HS 输出为 `SVSP/HS <bar> <grid> s1=50.0`（类型化曲线行），流内音符行尾带 `s1` 字段。
游戏端 `TryPeekStreamId` 解析行尾 `s{N}`（仅 s+纯数字）→ `StreamTypeByNoteIndex[index]="s1"` →
该音符类型键 = `s1`（`IsStreamType` 判定）：只吃本流曲线，**无本流曲线/清除后一律原速 1，
绝不跟随全局**（`GetCurveMultAt`/`SvIntegral`/`BuildEffectiveCurve`/`GetSpawnRadiusAt` 均隔离）。
流内音符的 scroll/窗口/渐入/激活自动按 s1 曲线驱动（50 段闪现、0 段冻结排队、0.5 段慢速）。

## 3.13 BOUNCE（弹跳音符，2026-08-18 完成；2026-08 分类型命令）

**ma2 语法**（Majdata 分组语义）：
```
BOUNCE\t<bar>\t<grid>\t<时长|NULL>                全局（展开到 tap/star/hold；each 双押在游戏里仍是 tap/star）
BOUNCE\t<bar>\t<grid>\ttap=8:1,hold=4:1,...       分类（tap/star/hold/break；NULL = 该类型不弹跳，覆盖全局）
```
时长 = 秒数或 `N:M`。不含 touch/touchhold/slide（Majdata BOUNCE 类型）。break 有分类曲线时用 `break` 键，否则回落基础类型。

**公式**（移植 Majdata `NoteDrop.GetBounceDistance`）：
```
judgeOffset = now − T；窗口 [−B, 0)；elapsed = clamp(judgeOffset+B, 0, B)
distance = 1.225 + 0.5×(8×(4.8−1.225)/B²)×(elapsed−B/2)²
bounceY = StartPos + (EndPos−StartPos)×t + V_9
```

**关键实现点**（踩坑后定型）：
- `BounceNoteVisualPostfix`（GetNoteYPosition 后置）：弹跳窗口内 __result=bounceY；**窗口前音符 alpha=0 + 光效 SetActive(false) + 外框 SetActive(false)**（note 弹出前全部不可见——避免生成点闪现和"外框从中间快速放大"）；判定后恢复。
- **外框（NoteGuide 弧形提示线）**：**弧心固定**（localPosition 不动 = 屏幕圆心）、`scale = clamp01(distance/4.8)`——与音符轨道同心同径（平移弧会破坏同心，bug #38）。光效/绝赞挂到 NoteObj 下跟随。
- **激活帧闪现**（bug #39）：`BounceHideOnInit`（base Initialize 后置）+ 5 个子类 Initialize 后置在注册流程内（渲染前）隐藏；`SetGuideObject` 在 RegistNote 里 Initialize 之后才激活外框（`BounceHideGuideOnSet` 后置再隐藏）；UpdateCtrl 注册晚于 UpdateNotes → 必须在注册流程内完成隐藏。
- **Hold body 保持原版**（用户确认 hold 弹跳没问题，不做整体弹跳）。
- 判定不 patch（弹跳回到判定线 = 音频判定时刻）。

## 3.14 SPAWN（环形音符视觉出生半径，2026-08 完成）

**ma2 语法**（Majdata 分组语义）：
```
SPAWN\t<bar>\t<grid>\t<半径|NULL>                 全局（作用于 tap/hold/star/break；each 双押在游戏里仍是 tap/star）
SPAWN\t<bar>\t<grid>\ttap=0,hold=4.8,...          分类（tap/hold/star/break/each；NULL = 该类型回退全局）
```
半径范围 −4.8～4.8（1.225=原版生成点，0=圆心，4.8=本侧判定线，−4.8=对面判定线）。
touch/touchhold/slide 不受影响（Majdata SPAWN 类型）。

**实现**（移植 Majdata `NoteDrop.GetCurrentVisualDistance` Pending 钳制）：
- 真实距离 = 1.225 + 3.575×V_5（V_5 = 滚动进度）；未达出生半径 R 前视觉停在 R 处
  （`SvRealTimeVisualPostfix` 的 v5 下限 `(R−1.225)/3.575`），到达后按 SV×HS 正常移动；
  判定时刻不变。R>1.225 → 更靠近判定线等待；R<1.225 → 生成点上方（含圆心/对面）。
- 停驻期外框 scale 固定为 `0.25+0.75×floor`（跟随停在 R 处的音符），离开停驻后恢复
  原版时间轴公式。
- **BOUNCE 联动**：弹跳抛物线起点/终点 = 出生半径（`accel = 8×(4.8−R)/B²`），
  y 映射仍用标准 `t=(distance−1.225)/3.575` 与停驻位置无缝衔接；R=4.8 时 accel=0
  弹跳退化为原地（音符本就在判定线）。

## 4. 游戏侧反编译事实（改代码前必读）

### 4.1 NoteJudge.ETiming（本版本，无 "Miss" 名）

```
TooFast=0 FastGood=1 FastGreat3rd=2 FastGreat2nd=3 FastGreat=4
FastPerfect2nd=5 FastPerfect=6 Critical=7
LatePerfect=8 LatePerfect2nd=9 LateGreat=10 LateGreat2nd=11 LateGreat3rd=12 LateGood=13
TooLate=14 End=15
```
- Miss = TooFast(0) / TooLate(14) / End(15)（ConvertJudge 都映射 JudgeBox.Miss）。
- EJudgeType：Tap=0 HoldOut=1 SlideOut=2 Touch=3 ExTap=4 Break=5 End=6。

### 4.2 方法 virtual 表（决定能否 override）

| 方法 | 状态 |
|---|---|
| `NoteBase.Judge / JudgeToolate / Initialize / Execute / NoteCheck / Awake / EndNote / SetPlayResult / PlayJudgeSe / ReserveTapJudgeSe / ReserveExJudgeSe` | **protected virtual** ✓ 可 override |
| `TouchNoteB.Judge / Initialize / NoteCheck / Awake / PlayJudgeSe` | protected virtual ✓（Judge 是 override NoteBase 的） |
| `HoldNote.JudgeTotalResult / JudgeHoldHead / HoldOn`、`TouchHoldC.JudgeTotalResult / JudgeHoldHead / HoldOn`、`SlideRoot.Judge / JudgeToolate` | **非虚** ✗ 不能 override（判定只能 transpiler；HoldOn 不能拦截"按住时贴图被原版覆盖"） |
| `BreakStarNote.SetMulti / SetSlideStar`、`BreakSlide.SetSprite` | **非虚** ✗（光效替换只能 Harmony postfix） |
| `SlideRoot.Initialize / SetEach / NoteCheck` | public/protected virtual ✓（SetEach public virtual，MineSlideRoot 用它重贴箭头/星） |
| `SlideRoot.Awake`、`SlideFan.Awake` | **private 非虚**（反射调用） |
| `GetJudgeResult()` | public ✓；`NoteJudge.ConvertJudge` public static ✓ |

### 4.3 判定入口

- `GameManager.AutoJudge` **只在自动播放**调用（SetAutoPlayJudge / autoplay 分支）。
- `NoteBase.NoteCheck` 每帧：`SetAutoPlayJudge(); if (输入 && IsJudgeNote() && !IsUsedThisFrame) Judge(); if (JudgeResult == 15) judged = JudgeToolate(); else judged = true; if (judged) EndNote();`——**EndNote 只看 JudgeResult != 15**，反转后的 Critical/TooLate 都能正常收尾。
- 计分/显示都读 `JudgeResult` 字段（private，用 Traverse 写）。

### 4.4 池字段 ↔ 类型映射（RegistNote 的 switch）

Tap(0)/ExTap(2)→`_tapObjectList`；Hold(3)/ExHold(4)→`_holdObjectList`；BreakHold(12)/ExBreakHold(13)→`_breakHoldObjectList`；Star(6)/ExStar(8)→`_starObjectList`；BreakStar(7)/ExBreakStar(17)→`_breakStarObjectList`；Break(1)/ExBreakTap(11)→`_breakObjectList`；Slide 系(5/14/15/16/18)→`_slideObjectList`+`_fanSlideObjectList`+`_arrowObjectList`+`_breakArrowObjectList`；TouchTap(9)→`_touchBObjectList`/`_touchCTapObjectList`；TouchHold(10)→`_touchBHoldObjectList`/`_touchCHoldObjectList`。

### 4.5 sprite 覆盖点（贴图会被覆盖的地方，必须重贴）

- `SlideRoot.SetEach`：给箭头赋 NormalSlide/EachSlide + 给星赋 NormalStar/EachStar/BreakStar → **MineSlideRoot.SetEach override 重贴**。
- `HoldNote.SetEach / HoldOn / Initialize`：给 hold 条赋 NormalHold/EachHold/HoldOff/NormalHoldOn/ExHold → MineHoldNote 在 Initialize base 后贴（SetEach 在 base 内先执行 ✓）；HoldOn 已用 transpiler 修（§3.6）。
- `SetArrowObject / ResetArrowObject`：只做 SetParent/SetActive/列表，**不重置 sprite** ✓ 箭头池贴图安全。
- `NoteGuide.SetColor`：每次调用重置主体 sprite → `NoteGuideSetColorPostfix` 重贴（§3.9）。
- `BreakNote/BreakStarNote/BreakSlide` 的 `EffectSprite`：SetEach/SetMulti/SetSlideStar/SetSprite 都会重置 → override/postfix 重贴（§3.10）。

## 5. 已知限制 / TODO

**已全部合并到同目录 `TODO.md`**（对照 MajdataViewAlpha 功能差距 + 源码搬来的 TODO + 已知限制 + 建议实现顺序），此处不再重复维护。

要点摘录：
- [x] Slide 判定：星星到底=CP、提前划完全部轨道=Miss（当前规则，见 §3.6）。
- [ ] NoteGuide 独立池（共享池污染）。
- [ ] MBTTP（地雷绝赞 touch）绝赞贴图、BRTHO（绝赞 touchhold）SE、C 传感器绝赞池。
- [ ] D 区 / Touch Slide / rp-rq / 无尾自旋星 / conn. slide 独立追踪时长（对照 MajdataViewAlpha）。
- [x] 代码清理：`MineNoteBehaviour.Instances`、`GetSlideArrowNum` 注入恢复、slide dump 配置化（**slide_judge_dump 已整体删除**）。

## 6. 历史 Bug 记录（防回归，每条都真实炸过）

1. **判定 patch 打在 `GameManager.AutoJudge`** → 只在自动播放调用，完全不生效。判定必须打 §3.6 的 8 个方法。
2. **`MissTimings` 数组为空**：ETiming 没有名为 Miss 的枚举值。用 TooFast/TooLate/End 判断。
3. **ApplyMineTextures 收到 GameObject**：`instance as Component` 为 null 直接 return。必须 `ApplyMineTexturesToObject` 先取 NoteBase 组件。
4. **AutoJudge/判定路径误判**：判定字段是 private `JudgeResult`，只能 Traverse 写。
5. **RegistNote transpiler 访问器签名不匹配**：栈 `[instance, fieldName]` 但访问器只收 `(GameCtrl)` → 字符串当 GameCtrl 弹出 → InvalidCastException → **所有音符注册失败**（日志：noteIndex=0 反复且不递增）。访问器签名必须 `(GameCtrl, string)`。
6. **AddMinePoolWithFactory 泛型推断**：T 按 lambda 推断成 Mine* 子类 → `GetValue<List<Mine*>>()` 转 `List<基类>` 失败 → 11 个池全挂。泛型参数必须显式给基类。
7. **MineJudgeInversion 多插 `dup`**：栈残留 timing → InvalidProgramException → patch 应用失败 → 判定完全不反转。只能 `ldarg.0; call`。
8. **slide 内部星被误贴**：三条路径都要堵——池创建（applyTextures:false）、MineSlideRoot.Initialize（不贴）、**behaviour 挂载（Setup applyTextures:false）**——后一条最隐蔽。
9. **Sprite 缓存 key 串用**：图集 sprite name 可能为空，只按 name 缓存 → 不同子物体串用 border/pivot → hold 拉伸。key 必须含完整几何（name+rect+pivot+border+ppu）。
10. **Sprite.Create ppu=1 + Tight**：hold 条等细长贴图拉伸变形。ppu 用 `original.pixelsPerUnit`，mesh 用 FullRect，border 等比换算。
11. **slide 箭头被 SetEach 覆盖**：地雷箭头池贴图被 Initialize 里的 SetEach 抹掉 → MineSlideRoot.SetEach override 重贴。
12. **fan 轨道线用 `i % 11` 错位**：`_spriteLines` 是 L/R 成对的 22 个，`i%11` 让同一条线 L/R 用不同贴图 → 必须 `i / 2 % 11`。
13. **fan key 缺 D2**：素材是 `slide_fun_mine_00.png`，key 拼成 `slide_fun_mine_0` → Missing 贴图 → key 必须 `ToString("D2")`（`slide_fun_mine_00`）。
14. **NoteGuide 不是 note 子物体**：`ReplaceChildSprite(root,"NoteGuide",...)` 永远匹配不到（GuideObj 是共享池对象）→ 必须 `ApplyMineGuideTexture` 从 NoteBase.GuideObj 下手，且 SetColor 每次重置 sprite → postfix 重贴。
15. **绝赞光效在非虚方法里被重置**：`BreakStarNote.SetMulti / SetSlideStar`、`BreakSlide.SetSprite` 非虚，override 无效 → 只能 Harmony postfix（SetMulti 还区分 multiFlag，双星/单星用不同贴图）。
16. **fan 线不染色时是白色**：`slide_fun_mine_00..10` 是白色贴图，原版靠 SpriteRenderEx 调色 → 用 `UpdateAlpha` override 染灰（0.5,0.5,0.5,a）保留 alpha。
17. **transpiler 替换指令丢标签 → "Label #N is not marked"**：HoldOnMineTranspiler 把 `set_sprite` 调用换成 ldarg+call 时直接丢弃原指令，但原指令带分支标签（HoldOn 里有 branch 指向它）→ Harmony DMD 编译失败，`HoldOnMineTranspiler` 和 `NoteSpeedContextPatch`（也 patch HoldOn，级联失败）一起挂。**替换指令时必须 `labels.AddRange(inst.labels)` + `blocks.AddRange(inst.blocks)` 转移到第一条替换指令**（RegistNoteTranspiler 同样修了）。
18. **单星绝赞光效没被替换（两个坑叠加）**：
    - `BreakStarNote.Initialize` 对**单星也会调 `SetMulti(false)`**（`multiFlag = child.Count >= 2`），之前 postfix 只处理 `multiFlag=true` → 单星 EffectSprite 保持原版 BreakStarEff（橙色闪光）。
    - slide/fan 内部星是**原版 BreakStarNote 实例**（slide 自己的 prefab 实例化，不走地雷池），`is MineBreakStarNote` 判断永远 false → SetSlideStar postfix 也不触发。
    - 修法：登记表 `MineBreakStars`（HashSet）+ `MineifyBreakStarEffect` 在登记时立刻替换（SetMulti/SetSlideStar 在 base.Initialize 里先于登记执行），postfix 只认登记表、按 `multiFlag`/`MultiSlide` 选单星/双星贴图。
19. **slide 划完后的最终判定错（GOOD/CP 而不是 Miss，两个坑）**：
    - `SlideRoot.NoteCheck` / `SlideFan.NoteCheck` 末尾有原版宽限：`JudgeResult==14 且全部轨道碰到 → 直接写 13 (LateGood/GOOD)`——地雷的 Miss(14) 被覆盖成 GOOD。
    - `SlideRoot.Judge`（非虚）只在 `hitIndex >= count`（全部划完）时被调用，`JudgeToolate` 反之——但反转后的值语义反了：划完但超窗 → 原版 TooLate → 反转成 Critical(7) → 显示 CP。
    - 修法：`ApplyMineJudgeTiming` 加 slide 特判（划完→Miss、没划完→CP，`IsMineSlideCompleted` 按 `_hitIndex >= _hitAreaList.Count-1`，fan 三条线各自达标）+ `SlideGraceJudgeTranspiler` 把 13 宽限写入换成 helper（地雷→14）。
20. **MineSlideFan 判定完全不反转（最重要的一坑）**：`SlideFan.Initialize` **完全重写、不调用 `SlideRoot.Initialize`**（反编译确认）→ 挂在 `SlideRoot.Initialize` 上的 behaviour 附加 postfix 对 fan **永不触发** → MineSlideFan 没有 MineNoteBehaviour → transpiler 反转直接放行 → 划完显示**原版** good/great/criticalperfect。修法：`CustomNoteTypes.EnsureMineBehaviour(note, data)` 在 `MineSlideFan.Initialize`（和 `MineSlideRoot.Initialize` 兜底）里显式挂 behaviour。**凡是"行为组件靠某个 base 方法 postfix 附加"的类，都要确认它的 Initialize 是否真的调用 base。**
21. **slide 判定诊断 dump**（2026-08 已整体删除）：游戏目录 `slide_judge_dump.txt`（每局 OnStart 清空，最多 300 行）——`JudgeTiming`（transpiler 生效 + behaviour 存在性 + raw timing + completed）、`GraceJudge`（宽限写入替换生效）、`NoteCheckEnd`（EndFlag/DispJudge/JudgeResult 终值 + mine 标志）、`AttachBehaviour`。某段缺失 = 对应 patch 没生效。排查判定问题先看这个文件（历史记录，代码已删）。
22. **slide 的 NoteKinds indexNote 对不上 → behaviour 没挂 → 判定完全不反转（dump 实证）**：slide 初始化时拿到的 `NoteData.indexNote` 可能与注册时不一致（测试谱里部分 MineSlideRoot 划完 hitIndex=3~5 但无 `AttachBehaviour`/`JudgeTiming` 记录 → 显示原版 FastGreat/FastGood/Critical）。**修法：Mine\* 类只可能来自地雷池，直接按类判定地雷**——`EnsureMineBehaviour`、`ApplyMineJudgeTiming`、`ApplyMineSlideGraceJudge`、NoteCheckEnd dump 里全部加 `note is MineSlideRoot or MineSlideFan` 兜底，不再依赖 `NoteKinds[indexNote]`。
23. **transpiler 前置插入被 br.s 分支跳过（slide 判定不反转的最终根因，dump 实证）**：`SlideRoot.Judge` 的 IL 是 `...; br.s IL_0084; ...; IL_0084: stfld JudgeResult`——分支直接指向 stfld。Harmony 保持 stfld 指令身份并把分支重映射到它的新位置，**插在 stfld 前面的代码（ldarg.0; call）被分支跳过**。dump 表现：`JudgeEntry` 每帧出现（prefix 生效）但 `JudgeTiming` 从不出现（transpiler 的调用没执行），NoteCheckEnd 是原始 FastGreat/FastGood/Critical（good/great/cp）。**修法：改后置插入**——保留原 stfld（分支目标有效），在其后插 `ldarg.0; ldstr 字段名; call ApplyMineJudgePostWrite(object,string)`，读回刚写入的值再反转写回；stfld 后栈为空，插入不破坏栈形。之前"fan 偶尔正常"是因为那几次走的是 `JudgeToolate` 的写入（无分支，前置插入生效）。
24. **判定规则改版（用户 2026-08 要求）**：旧规则"打中=Miss、不打=CP"→"刚好打中(CP 帧)=CP、其余=Miss"→**最终规则"到判定线自动判 CP（像 autoplay，不碰也自动 CP）"**。实现：简单键 `MineAutoJudgePostfix`（SetAutoPlayJudge 后置直接写 Critical——`AutoJudge()` 在非 autoplay 模式返回 TooFast 不能用）；slide/fan `MineSlideAutoPlayTranspiler` 把 IsAutoPlay()/AutoJudge() 换成地雷感知版本走原版 autoplay 分支。触摸打偏 → 后置反转 → Miss。绝赞 touch 不受影响。（slide 的 autoplay 化后被用户否决、撤销，slide 判定暂不做。）
25. **自动 CP 对自反转类不生效（"普通键还是会 miss"）**：`MineAutoJudgePostfix` 最初用 `GetComponent<MineNoteBehaviour>().IsMine` 判断地雷，但 **ISelfJudgingMineNote 类（tap/star/break/touch）按历史设计不挂 behaviour** → 自动 CP 对它们从不触发 → 不碰时走原版 too-late → Miss。**修法：按类判断**（`IsMineSimpleNote`：9 个 Mine* 简单键类，排除 TouchBreakNoteB）。凡是"按 behaviour 找地雷"的代码都要警惕这个坑。
26. **hold/touchhold 变原版判定（"普通键变原版"）**：通用自动 CP postfix 打在 `NoteBase.SetAutoPlayJudge` 上，但 **HoldNote/TouchHoldC override 了 SetAutoPlayJudge**（virtual dispatch 只跑 override，base 的 patch 不触发）→ hold 完全走原版。**修法：hold 系列单独实现**——`MineHoldAutoPlayTranspiler` 直接 patch `HoldNote/BreakHoldNote/TouchHoldC` 的 `NoteCheck`/`JudgeTotalResult`/`SetAutoPlayJudge`（IsAutoPlay/AutoJudge → 地雷感知版本，自动按住到尾部判 CP）+ `MineHoldTouchCheck`（判定期间触摸 → 立即 Miss）。**凡是"override 了被 patch 方法"的类型都要单独处理。**
27. **MineHoldAutoPlayTranspiler "Label #N is not marked"（patch 全挂，hold 依旧原版）**：`HoldNote.NoteCheck` 的 `IL_00E6: brfalse.s IL_00f1` 分支目标正是被替换的 `call IsAutoPlay`——替换时没转移原指令上的标签 → DMD 编译失败 → 整个 patch 回滚（同方法触摸 postfix 也一起没了）→ `NoteSpeedContextPatch` 级联失败。**修法：替换指令必须 `labels.AddRange(inst.labels)` + `blocks.AddRange(inst.blocks)` 转移到第一条替换指令**（bug #17 同族，所有替换式 transpiler 都要遵守）。
28. **绝赞地雷 hold（MineBreakHoldNote）不生效**：`BreakHoldNote` 的基类是 **NoteBase（不是 HoldNote）**，它自己 override 了 `SetAutoPlayJudge`——transpiler 只 patch 了 `HoldNote/TouchHoldC.SetAutoPlayJudge`，漏了 `BreakHoldNote.SetAutoPlayJudge` → 绝赞 hold 的头自动判定缺失。**修法：TargetMethods 补上 `BreakHoldNote.SetAutoPlayJudge`。查"有哪些 override"时按类型逐一确认基类和方法表，不能假设继承关系。** 附带：`MineBreakHoldNote.SetEach`（protected override，注意访问修饰符必须与基类一致）把 `BreakEffectSprite` 替换成 `hold_break_eff_mine`（用户 2026-08 补充素材）。
29. **slide 判定最终规则（用户 2026-08）**：星星滑到最后=CP、提前划掉全部轨道=Miss——恢复三件套：`ApplyMineJudgeTiming` slide 特判（`IsMineSlideCompleted` 完成度判断）+ `MineJudgeInversion` 加回 `SlideRoot.Judge/JudgeToolate` + `SlideGraceJudgeTranspiler`（13 宽限写入→helper）。这次基于后置反转（bug #23 修复），分支跳过问题不存在；grace 只会在"划完且 TooLate"时触发，helper 保持地雷=Miss 与新规则一致。
30. **slide 没划完的 CP 迟到（"滑到底部过一会才判 CP"）**：NoteCheck 的收尾条件是 `lastWaitTime <= 0 || JudgeResult == TooLate(14)`——原版 Miss(14) 立即收尾，但地雷没划完的反转结果是 **Critical(7)**，`== 14` 不认 → 收尾要等 lastWaitTime（约 50~145ms）耗尽。**修法：`SlideEndWaitTranspiler`**（patch `SlideRoot.NoteCheck`/`SlideFan.NoteCheck`）把 `call GetJudgeResult; ldc.i4.s 14; ceq` 换成 `ldarg.0; call IsMineSlideEndWaitDone`——地雷 slide 且 JudgeResult==Critical 也立即收尾（星星到底部即判 CP）。
31. **绝赞 touchhold + 命名改版（用户 2026-08 定版）**：**BR = 绝赞（非地雷）、MB = 地雷版**。`BRTTP`=绝赞 touch（恢复原语义）、`BRTHO`=绝赞 touchhold（新，TouchBreakHoldC：绝赞独立池 `_touchBHoldObjectList` + `JudgeTotalResult` postfix 命中强制 CP + break 素材）、`MBTTP`=地雷绝赞 touch（恢复）、`MBTHO`=地雷绝赞 touchhold（MineTouchHoldC + `behaviour.IsTouchBreak` 应用 break 素材）。贴图共用 `touchhold_break_0..3`（Red/Yellow/Green/Blue）+ `touchhold_break`（HoldGauge 外框）+ `touch_break_point_mine`（Point）。
32. **SV 全局时钟重映射带偏判定**：早期方案 patch `GetCurrentMsec` 整体重映射 → 判定窗口跟着变 → 对不上音频（用户否决）。判定必须保持音频时间（`IsNoteCheckTimeStart` 只读 AppearMsec）。
33. **soflan 是死代码**：`getCurrentDrawMsec/Frame`（soflan 时间重映射入口）无调用者，注入 `_soflanList` 完全无效。
34. **SV 重映射未覆盖 scale（NoteCheck）→ 加速段全尺寸干站**：只重映射 `GetNoteYPosition` 时，scale 渐入（NoteCheck 里 `V_3 = StartMsec+DefaultMsec`）仍用原始时间 → ×2 段音符 T−600 已全尺寸但 T−300 才起飞（干站 300ms）。位置与出现动画的时钟必须一致（最终方案：等效流速 D' 缩放 + 实时 ∫sv，原版公式统一）。
35. **激活提前量未随 SV 缩放 → 低流速无渐入**：音符不是加载时全部激活——`GameCtrl.UpdateCtrl` 每帧按 `now >= noteTime − apperMsecTap/Touch` 从池注册（apperMsecTap = 2×DefaultMsec）。SV 缩放 DefaultMsec 后激活提前量不缩 → ×0.5 段激活太晚、渐入窗口（[T−4D, T−2D]）在激活前结束 → 直接全尺寸下落。修：`SvActivationLeadTranspiler` 把 apperMsecTap/Touch 读取换成按音符总倍率缩放（transpiler 需自动发现 NoteData 局部变量——按 `VariableType.FullName == "Manager.NoteData"` 匹配）。
36. **外框同步基准错误（组件 vs launcher 世界 y）**：NoteObj 的父是 launcher（世界 y=−420）而非组件（同 −420 但 Hold 的 NoteObj 显示在 body 中点）；bounceY 是相对 NoteObj 父（launcher）的局部坐标——同步外框位置必须用 `NoteObj.parent.position.y + bounceY`。日志实测：`noteParentY` 与 `baseY` 相同（组件在 launcher 原点），但 Hold 的 NoteObj 世界位置 = body 中点（head 弹跳、tail 原版 → 中点在生成点方向，视觉"中间闪"）。
37. **外框平移破坏同心 → 径向错位**：外框弧贴图 pivot 在弧心（屏幕圆心侧），把外框整体平移到音符位置后弧心偏离屏幕圆心 → 弧与音符轨道不同心。正确：外框 localPosition 不动（弧心=屏幕圆心），只 `scale = clamp01(distance/4.8)` 缩放（Majdata tapLine 同款）。
38. **外框 scale 跳变/延迟/快速移动（"闪一帧"/"从中间快速放大"）**：弹跳起点原版 scale≈0.77 → 弹跳值 1 的过渡：直接设置=跳变（闪）；min(弹跳值,原版值)=延迟跟随（弹到生成点才动）；插值 lerp=快速放大（肉眼可见移动）；预过渡=仍可见。**最终：弹跳窗口前外框 SetActive(false) 完全隐藏，弹跳开始才出现（scale 直接=distance/4.8）**——note 弹出前外框不可见（用户定版）。
39. **弹跳激活帧在生成点闪现一瞬**：音符激活（RegistNote）时 `note.gameObject.SetActive(true)`（Initialize 之前）→ 池中旧位置渲染一帧。修：base Initialize 后置 + 5 个子类 Initialize 后置在注册流程内（渲染前）隐藏（alpha=0 音符/绝赞、SetActive(false) 光效）；`SetGuideObject`（RegistNote 里 Initialize 之后激活外框）后置再隐藏外框。注：`UpdateCtrl`（注册）晚于 `UpdateNotes`（Execute）——不隐藏激活帧就闪。
40. **光效/外框用 alpha 隐藏无效（且破坏 Break 脉冲观感）**：EffectSprite/NoteGuide 的颜色每帧被原版 `GetNoteYPosition` 重设（脉冲/淡入）——alpha 覆盖被原版覆盖。只能用 `gameObject.SetActive(false/true)`（原版不碰这些对象的 active）。

## 7. 测试验证清单

1. `.\build.ps1` → 拷 `Output\AquaMai.dll` 到游戏 `MelonLoader\Mods\`（注意同时拷 `Output\AquaMai.Mods.dll` 等 embedded 不需要，只有 AquaMai.dll）。
2. 启动日志：`Mine pools ready: ... _arrowObjectList=640, _breakArrowObjectList=640`（14 池）；无 `Failed to create mine pool`。
3. 进曲：`Mine note independent class active`（自反转类）；`HoldOn transpiler active`（按住 hold 时）；`MineSlideFan textures applied: lines=22 eff=22 stars=3 baseStars=3`（fan 谱）；`ApplyMineGuideTexture: note=...`（首次）。
4. 玩法（当前规则）：刚好打中（原判定 CP 的帧）→ **CP**；打偏 good/great / 不打 → **Miss**；绝赞 touch 命中 → CP。
5. 视觉：hold 条 9-slice 正常（中间段拉伸、两端不拉伸）；slide 箭头 slide_mine、移动星 star_mine/star_break_mine；**fan 轨道线灰色、信号线地雷化、星地雷化、无蓝色外框/橙色闪光**；谱面中间无 stray 贴图。
6. 性能：touchhold 密集段不卡（Sprite 缓存生效）。
7. **SV/HS**：`SV/HS active: N sv segments, M hs segments` + `SV/HS note table built: N notes`；×2.0 段飞行中变速即时生效；SV=0 段音符视觉冻结、恢复后继续；到达判定线=音频判定时刻（判定不偏）；`HS` 时间轴行生效（含 `tap=2:1,hold=4:1` 分类）；行尾 `x0.5` 内嵌 HS 优先。
8. **BOUNCE**：`Bounce active: N segments`；音符从判定线弹出→生成半径→弹回判定线（抛物线）；外框弧心固定、scale 随 distance 缩放（同心）；弹跳窗口前音符/光效/外框全部不可见（无生成点闪现）；判定正常。测试谱：`000417_03_bounce_test.ma2`（全谱 BOUNCE 8:1）。

## 8. 反编译/调试工具（离线可用）

NuGet 不可达（SSL 失败），不能装 ilspycmd；用本地 Mono.Cecil 读 IL：

```powershell
$cecil = [System.IO.File]::ReadAllBytes("C:\项目\AquaMai-main\AquaMai-main\Libs\Mono.Cecil.dll")
$asm = [System.Reflection.Assembly]::Load($cecil)
$resolver = New-Object Mono.Cecil.DefaultAssemblyResolver
$resolver.AddSearchDirectory("C:\项目\AquaMai-main\AquaMai-main\Libs")
$rp = New-Object Mono.Cecil.ReaderParameters
$rp.AssemblyResolver = $resolver
$game = [Mono.Cecil.AssemblyDefinition]::ReadAssembly("C:\项目\AquaMai-main\AquaMai-main\Libs\Assembly-CSharp.dll", $rp)
$t = $game.MainModule.Types | Where-Object { $_.FullName -eq "Monitor.SlideRoot" }
($t.Methods | Where-Object { $_.Name -eq "SetEach" }).Body.Instructions | ForEach-Object { "{0:X4}: {1}" -f $_.Offset, $_ }
```

技巧：`MainModule.Types` 不含嵌套类型（递归 NestedTypes）；找调用者遍历所有方法体 Instructions 匹配 MethodReference；枚举值看 Fields 的 IsStatic&&IsLiteral。检查 virtual：`$m.IsVirtual / IsPublic / IsFamily / IsFinal`（决定能否 C# override）。
