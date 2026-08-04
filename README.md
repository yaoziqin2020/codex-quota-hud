# Codex Quota HUD

[![CI](https://github.com/yaoziqin2020/codex-quota-hud/actions/workflows/ci.yml/badge.svg)](https://github.com/yaoziqin2020/codex-quota-hud/actions/workflows/ci.yml)
[![Release](https://img.shields.io/github/v/release/yaoziqin2020/codex-quota-hud)](https://github.com/yaoziqin2020/codex-quota-hud/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%20%7C%2011-4B8BF5)](#系统要求--requirements)

Codex Quota HUD 是一个轻量的 Windows 桌面额度浮窗。它读取本机
`codex app-server` 返回的官方额度数据，以常驻桌面的动态浮窗、藏边进度条
和数字托盘图标展示 5 小时与每周剩余额度。

Codex Quota HUD is a lightweight Windows desktop companion that visualizes
the five-hour and weekly quota data returned by the local
`codex app-server`. It provides an animated floating HUD, a themed edge bar,
and a numeric tray icon without scraping web pages or storing credentials.

> 这是一个独立开源项目，并非 OpenAI 官方产品。
>
> This is an independent open-source project and is not an official OpenAI
> product.

## v1.2.2 installation / 安装

Primary path: download `CodexQuotaHud-Setup-v1.2.2.exe` and double-click it.
主要方式：下载 `CodexQuotaHud-Setup-v1.2.2.exe` 后直接双击运行。

Setup automatically offers Simplified Chinese or English, installs only for
the current Windows user, and needs no administrator permission. It installs
to `%LOCALAPPDATA%\\Programs\\CodexQuotaHud`.

安装器会自动提供简体中文和英文；它只为当前 Windows 用户安装，不需要管理员权限，安装路径为
`%LOCALAPPDATA%\\Programs\\CodexQuotaHud`。

The Setup task page contains startup at sign-in and creation of the normal
desktop shortcut, both selected by default. The desktop and Start-menu
shortcuts launch the real HUD without `--preview`. Developer Preview is not
offered by Setup; source and ZIP users can launch it explicitly with
`CodexQuotaHud.App.exe --preview`.

Setup also offers **Install Skin Designer / 安装皮肤设计器** as an optional
component. It is visible but unchecked by default. Selecting it adds only the
Designer files and a Start-menu entry; the normal HUD can import and use
`.cqskin` files without the Designer.

Setup 还提供可选组件 **安装皮肤设计器**，默认不勾选。勾选后只增加设计器文件和
开始菜单入口；不安装设计器的普通用户仍可在正式 HUD 中导入和使用 `.cqskin` 皮肤。

Setup 的任务页包含开机启动和创建正式版桌面快捷方式，两项默认勾选。桌面和开始菜单
快捷方式均直接启动真实 HUD，不带 `--preview`。Setup 不提供开发预览入口；源码和 ZIP
用户可明确运行 `CodexQuotaHud.App.exe --preview` 进入预览模式。

Install `v1.2.2` directly over `v1.0.0`, `v1.1.0`, `v1.1.1`, `v1.2.0`, or `v1.2.1`; personal HUD
settings, installed custom skins, Designer drafts, and Developer Preview window
state are retained by default. Normal uninstall preserves user data. Select the
explicit purge option only to remove the exact approved product-data roots.

可从 `v1.0.0`、`v1.1.0`、`v1.1.1`、`v1.2.0` 或 `v1.2.1` 直接升级，默认保留 HUD 设置、已安装皮肤、
设计器草稿和开发预览窗口状态。普通卸载会保留用户数据；只有明确选择清除选项时，
才会删除安装器批准的准确产品数据目录。

The release Setup is unsigned, so Windows SmartScreen may show an
unknown-publisher warning. Verify its SHA-256 against `SHA256SUMS.txt` before
running it:

```powershell
Get-FileHash .\\CodexQuotaHud-Setup-v1.2.2.exe -Algorithm SHA256
```

当前 Setup 未签名，Windows SmartScreen 可能显示未知发布者提示。运行前请使用
`SHA256SUMS.txt` 和上述命令核对 SHA-256。

If Setup is unavailable, `CodexQuotaHud-v1.2.2-win-x64.zip` plus its bundled
PowerShell script is the fallback. GitHub Packages is not used for this
application or its release assets.

若 Setup 不可用，`CodexQuotaHud-v1.2.2-win-x64.zip` 及其中 PowerShell 脚本是后备路径；
本应用和发布资产均不使用 GitHub Packages。

## v1.2.x Skin Designer / 皮肤设计器

Public `v1.2.0` adds safe data-only `.cqskin` import
to the normal HUD, a shared `CodexQuotaHud.Skins` runtime library, and a
separate Skin Designer application. It also adds an About
window, fixes Designer preview recovery after selecting `None`, and restores
formal-HUD automatic display after quota arrives during startup. The Designer
now has simple animation presets, visibly separate center-breathing and ring-
glow effects, compact grouped preview controls, readable dropdowns, immediate
custom-skin synchronization, a shared Documents exchange directory, and its
own application icon.

公开版 `v1.2.0` 新增了正式 HUD 的安全 `.cqskin` 导入、共享的
`CodexQuotaHud.Skins` 运行库、独立皮肤设计器、“关于”窗口、选择 `None` 后恢复
预览的修复，以及启动读取额度后正式 HUD 自动显示的修复。设计器还新增了简化动画
预设、可见的中心呼吸与额度环光晕、紧凑分组的预览控制、可读下拉框、即时自定义皮肤
同步、共享文档交换目录和独立应用图标。

`v1.2.1` corrects two runtime animation mappings without changing the
`.cqskin` schema: the full-ring glow now remains visibly below the solid
progress arc, and decoration floating uses a practical, responsive travel and
timing range. Existing skin packages automatically use the corrected behavior
after the HUD is upgraded.

`v1.2.1` 在不改变 `.cqskin` 格式的前提下修正两项运行时动画：整圈光晕的峰值
亮度不再接近真实进度弧，装饰浮动也改为清晰、响应及时的位移和速度范围。升级 HUD
后，现有皮肤包会自动使用修正后的效果，无需重新制作。

`v1.2.2` corrects skin-package metadata presentation without changing the
`.cqskin` schema. Import preview now shows the package author without implying
an identity-verification system that does not exist, and the built-in template
automatically declares HUD `1.2.0` as its true minimum version. Older drafts
and packages that recorded `1.1.1` are normalized when edited or displayed.

`v1.2.2` 修正皮肤包元数据的显示与生成方式，不改变 `.cqskin` 格式。导入预览不再
显示并不存在的作者身份验证状态；内置模板会自动声明真实最低兼容版本 HUD `1.2.0`。
旧草稿和旧皮肤包中误写的 `1.1.1` 会在编辑或显示时自动纠正。

The Setup shows an optional component named **Install
Skin Designer**, with this description: “Optional visual editor for creating
and exporting Codex Quota HUD skins. It is not required to run or import
skins.” The Simplified-Chinese component is **安装皮肤设计器**：
“用于创建并导出 Codex Quota HUD 皮肤的可选可视化编辑器。运行主程序或导入皮肤无需安装此组件。”
It is visible but unchecked by default. When selected, it adds only the
Designer files under `designer\` and a Start-menu entry; it creates no Designer
desktop shortcut or startup entry. Rerunning Setup can add or remove that
component while preserving HUD settings, installed skins, drafts, recovery,
and import storage. The fallback ZIP deliberately remains normal-HUD-only.

`.cqskin` is a bounded data archive, not an extension-code format. It contains
strict `manifest.json` and `theme.json` data plus zero to three optional
PNG/JPEG assets. Validation rejects executable/script/XAML content, remote
references, unsafe paths or links, malformed/oversized archives and images,
and hash mismatches. The normal HUD never depends on the Designer executable.

Run either application directly from source:

```powershell
dotnet run --project .\src\CodexQuotaHud.App\CodexQuotaHud.App.csproj
dotnet run --project .\src\CodexQuotaHud.SkinDesigner\CodexQuotaHud.SkinDesigner.csproj
```

Automated Task 18 evidence is green for the latest trustworthy reruns, but one
earlier full-solution run at the same source commit reported a single storage
test failure that could not be reproduced or root-caused. The local v1.2.0
upgrade and installed Designer's `None -> Dual/5h/Week` regression paths
passed. Fresh install/component removal/uninstall, full layout/DPI/image
matrices, sign-out/restart, and the real About-window visual check remain
incomplete. Those gaps remain documented as `NOT RUN`; they are not represented
as completed acceptance in the release record.

![Codex Quota HUD overview](docs/assets/codex-quota-hud-overview.png)

## 功能 / Features

- 同时显示 5 小时额度与每周额度；缺少某一窗口时自动退化为单层显示
- 后台每 60 秒刷新，双击浮窗可立即刷新
- 单击查看重置时间、最近更新时间和当前剩余比例
- 五套动态皮肤：科技仪表、双彩能量环、流体玻璃球、克制极光、液位储能舱
- 读取期间动画加速，空闲时低帧率缓慢运行，隐藏后停止动画
- 拖到显示器外侧边缘后自动收起为对应皮肤的额度进度条
- 多显示器与不同 DPI 下按当前工作区保存和恢复位置
- 托盘图标直接显示当前剩余百分比，并提供刷新、皮肤、动画、关于和退出菜单
- 单实例运行、静默启动、当前用户开机启动与安全卸载
- 不读取浏览器 Cookie，不保存 OAuth Token，不开放网络端口

## 下载与安装 / Download

从 [最新 Release](https://github.com/yaoziqin2020/codex-quota-hud/releases/latest)
下载 ZIP 后备包 `CodexQuotaHud-v1.2.2-win-x64.zip`，解压后在该目录运行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

程序会安装到：

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud
```

安装脚本会启动一个后台实例，并为当前 Windows 用户添加
`Run\CodexQuotaHud` 启动项。不会弹出黑色命令窗口。

卸载：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

## 操作 / Controls

| 操作 | 结果 |
|---|---|
| 单击浮窗 | 显示或关闭额度详情 |
| 双击浮窗 | 立即刷新额度 |
| 拖动浮窗 | 自由移动并保存位置 |
| 右键浮窗 | 打开皮肤、动画、刷新和退出菜单 |
| 移入藏边条 | 展开浮窗 |
| 右键托盘图标 | 打开完整控制菜单 |

浮窗靠近当前显示器的外侧边缘后，会在鼠标离开约 5 秒后收起，只留下
24px 可见区域中的主题额度条。共享边缘不会误触发藏边；顶部、底部和副屏
外侧边缘均按实际显示器布局判断。

## 额度显示 / Quota Display

中央数字表示当前主要额度窗口：

- 同时读到两项时：中央显示 5 小时额度，外层显示每周额度
- 只读到每周额度时：自动显示为单层“每周”
- 只读到 5 小时额度时：自动显示为单层“5 小时”
- 两项都读不到时：隐藏浮窗，不用占位值冒充真实额度

剩余百分比来自官方返回的 `usedPercent`，按
`100 - usedPercent` 计算并限制在 `0%..100%`。

### Low-quota alert colors

- Above `20%` keeps the selected skin's normal color. `>10%..20%` is Warning
  amber `#FFFFB547`; `<=10%` is Critical red `#FFFF5A67`.
- In dual-quota mode, the primary and secondary quotas are classified and
  colored independently. The alert color is shown on the floating HUD (all
  five skins), collapsed edge bar, tray percentage icon, and detail rows.
- This is color-only feedback: it does not add flashing, popups, sounds,
  settings, or change refresh behavior. The Developer Preview sliders are the
  manual boundary and mixed-state inspection tool.

## 五套皮肤 / Skins

| ID | 名称 | 视觉特点 |
|---|---|---|
| `HudDial` | HUD 科技仪表 | 双向扫描刻度与青蓝仪表环 |
| `EnergyRing` | 双彩能量环 | 紫罗兰能量弧与倾斜轨道 |
| `LiquidGlass` | 流体玻璃球 | 半透明玻璃、柔光与流体感 |
| `Aurora` | 克制极光 | 低饱和青绿极光带 |
| `LiquidTank` | 液位储能舱 | 真实额度水位与缓慢波面 |

皮肤、动画开关、窗口位置和最近成功刷新时间保存在当前用户的本地设置中。

## 隐私与安全 / Privacy

Codex Quota HUD：

- 不读取或保存浏览器 Cookie
- 不读取或保存 OAuth Token、账号信息或额度响应正文
- 不抓取 Codex 网页，也不调用私有网页接口
- 不开放本地或远程网络端口
- 只通过本机 `codex app-server` 子进程读取额度
- 只保存窗口坐标、动画开关、皮肤和最近成功刷新时间

安装和卸载脚本只操作精确的
`%LOCALAPPDATA%\Programs\CodexQuotaHud` 路径与当前用户启动项，并在移动、
替换或删除前检查目标路径和重解析点。

## 系统要求 / Requirements

- Windows 10/11 x64
- Codex Desktop 或可用的 `codex` CLI
- 从源码构建需要 .NET SDK `9.0.316`
- Release 为自包含 `win-x64` 单文件版本，无需另装 .NET Runtime

项目使用 `codex app-server` 的标准输入输出 JSONL 协议：
[OpenAI Codex app-server README](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md)。

## 从源码运行 / Build from Source

```powershell
git clone https://github.com/yaoziqin2020/codex-quota-hud.git
cd codex-quota-hud
dotnet restore .\CodexQuotaHud.sln
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

### 开发预览 / Developer Preview

当真实 5 小时额度暂时不可用时，可启动隔离的视觉预览工具：

```powershell
dotnet run --project .\src\CodexQuotaHud.App -- --preview
```

控制面板可切换双额度、仅 5 小时、仅每周和无额度状态，并调整百分比、
皮肤、动画、详情及四方向藏边。预览数据只存在于内存，不连接
`codex app-server`，不注册开机启动，也不写入正式设置。它用于视觉与交互
验收，不能替代真实双额度数据恢复后的最终链路验证。

控制面板底部的“退出预览并打开正式版”会先完整关闭预览并释放单实例锁，
再启动 `%LOCALAPPDATA%\Programs\CodexQuotaHud\CodexQuotaHud.App.exe`。
未安装正式版时按钮会禁用。

On the maintainer's machine, a separately created **Codex Quota HUD Developer
Preview** shortcut performs the opposite handoff: it closes installed mode
before the preview HUD and control window open. The installed build exits through its normal cleanup
path. Older builds use a fallback only when the running executable resolves to
the exact standard installation path above; a same-name process at any other
path is never force-closed. If replacement cannot be completed, the shortcut
shows a failure message and does not open preview.

The reverse handoff remains **退出预览并打开正式版**: preview cleans up first and
then starts the executable at the same exact installation path. The installed
binary, startup registration, shortcut arguments, settings preservation, and
both uninstall modes were accepted on a real Windows desktop. The visual
two-direction handoff remains an optional manual UI check.

预览控制面板默认完整显示，并将大小和位置单独保存到
`%LOCALAPPDATA%\CodexQuotaHud\preview-window.json`。该文件不包含模拟额度、
皮肤或正式 HUD 设置；小屏幕和高 DPI 下仍可使用纵向滚动。

元数据修正版从标签 `v1.2.2` 发布，`v1.2.0` 与 `v1.2.1` 的标签和发布资产保持不变。
不要用已有版本号生成不同二进制，也不要移动历史标签或替换历史 Release 资产。

## 项目结构 / Project Structure

```text
src/CodexQuotaHud.Core/       额度模型、映射、刷新状态和设置
src/CodexQuotaHud.Skins/      共享皮肤协议、校验、存储、打包与运行时渲染
src/CodexQuotaHud.App/        WPF 浮窗、皮肤导入、托盘与 app-server 集成
src/CodexQuotaHud.SkinDesigner/ 独立皮肤设计器进程、草稿、预览、应用与导出
tests/                        Core、Skins、App/UI 与 Designer 自动化测试
scripts/                      发布、安装、卸载和 Release 打包脚本
docs/                         设计、实现计划、验收记录和预览资源
.github/workflows/            Windows CI
```

## 验证 / Verification

`v1.2.2` 源码包含 1367 项自动化测试：

- Core：75 项
- Skins：335 项
- App / UI：609 项
- Skin Designer：348 项
- Total：1367 项

```powershell
dotnet test .\CodexQuotaHud.sln -c Release --no-restore
dotnet build .\CodexQuotaHud.sln -c Release --no-restore
```

GitHub Actions 会在每次推送和拉取请求中执行恢复、测试、构建和 Windows
自包含发布检查。CI 包固定使用临时版本 `0.0.0`，只在 Runner 内做安装烟测，
不会冒用或覆盖公开 Release 版本。

最新串行 Release 测试为 `1367/1367`、跳过 `0`，Release build 为
`0` warnings / `0` errors；三套安全与回滚筛选分别为 `223/223`、`224/224`
和 `116/116`。同一提交较早的一次完整测试曾出现 `1` 项未能复现、未能取得
`result.Errors` 的存储删除失败，因此验证记录仍保留这项未解决的历史异常。详见
[`docs/verification/2026-08-02-optional-skin-designer-acceptance.md`](docs/verification/2026-08-02-optional-skin-designer-acceptance.md)。

v1.2.2 的七个隔离安装/升级/组件/卸载场景及两个已提交清理失败场景全部通过。
本机 v1.2.1 到 v1.2.2 升级、安装文件一致性、启动项、快捷方式和用户数据保留均已核对。
GitHub `main` 的发布门槛 CI 全绿；v1.2.2 是 Latest、非草稿、非预发布，Setup、ZIP
与 `SHA256SUMS.txt` 的线上大小和 SHA-256 均与本地发布文件一致。

Release verification for `v1.1.1` passed Core `55/55`, App/UI `333/333`, and
total `388/388`; the Release build completed with zero warnings and zero
errors. Isolated installer smoke coverage passed clean install, upgrade/task
replacement, default-uninstall-preserve, and purge-uninstall scenarios. Real
Windows acceptance passed overwrite install, settings preservation, startup
and shortcut verification, normal uninstall, and explicit purge uninstall.

## 许可证 / License

Released under the [MIT License](LICENSE).
