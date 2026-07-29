# Codex Quota HUD

Codex Quota HUD 是一个轻量的 Windows 桌面额度浮窗。它在 Codex Desktop
运行时显示剩余额度，Codex 退出后自动隐藏浮窗；托盘图标会继续留在后台，
便于立即刷新、切换皮肤或退出程序。

## 显示内容

- 中央数字和内层视觉表示 **5 小时额度**的剩余百分比。
- 外层视觉表示**每周额度**的剩余百分比。
- 鼠标悬停可查看实际读到的额度、重置时间、最近更新时间和过期状态。
- 后台每 60 秒刷新一次。悬停时如果数据已经超过 60 秒，会立即补一次刷新。
- 读取期间动画平滑加速；完成后恢复慢速扫描。隐藏时动画停止。

剩余百分比来自官方返回的 `usedPercent`，按
`100 - usedPercent` 计算并限制在 `0%..100%`。

### 为什么可能没有 5 小时环

浮窗只显示官方 `codex app-server` 本次实际返回的额度窗口。若只返回每周
额度，界面会自动退化为单层，并在中央标明“每周”；若只返回 5 小时额度，
则中央标明“5 小时”。这不代表缺失的额度为 `0%`。

当两项额度都读不到时，圆球会隐藏，避免用占位值冒充真实额度。此时可查看
托盘状态进行排查。

## 五套皮肤

- `HudDial`：HUD 科技仪表，首次启动默认皮肤。
- `EnergyRing`：双彩能量环。
- `LiquidGlass`：流体玻璃球。
- `Aurora`：克制极光。
- `LiquidTank`：液位储能舱。

右键圆球或托盘图标均可即时切换皮肤、开关动画。选择会保存在当前 Windows
用户的本地设置中，下次启动继续使用。详情卡片也会随当前皮肤同步配色。

把圆球拖到当前显示器工作区的左边或右边后，它会在鼠标离开约 1 秒后自动
收起，只留下 12 像素的发光把手；鼠标移入会立即展开。顶部和底部不会触发
自动隐藏。停靠位置会保存，多显示器和不同 DPI 下会按当前屏幕重新限制位置。

## 系统要求

- Windows 10/11 x64。
- Codex Desktop 或可用的 `codex` CLI。
- 源码构建需要 .NET SDK 9；发布后的自包含版本不需要另装 .NET Runtime。

程序使用官方稳定的 `codex app-server` 标准输入输出 JSONL 协议。协议说明：
[OpenAI Codex app-server README](https://github.com/openai/codex/blob/main/codex-rs/app-server/README.md)。

## 构建与安装

在仓库根目录打开 PowerShell：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

发布脚本生成自包含、单文件、`win-x64` 版本到：

```text
artifacts\CodexQuotaHud-win-x64\
```

安装位置固定为：

```text
%LOCALAPPDATA%\Programs\CodexQuotaHud
```

安装脚本会先把新版本复制到同盘临时目录，停止且只停止“可执行文件路径与上述
安装位置完全匹配”的旧实例，再替换文件。它随后写入当前用户的启动项
`Run\CodexQuotaHud`，内容为带引号的可执行文件路径加 `--background`，并以
隐藏窗口方式启动一个实例。

### 更新

拉取新代码后，重新运行发布和安装两条命令即可。更新不会删除浮窗位置、皮肤
或动画偏好。

### 卸载

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

卸载脚本只会停止可执行文件路径与安装目录匹配的 `CodexQuotaHud.App` 进程、
删除 `Run\CodexQuotaHud` 这一项，并在严格校验路径后删除固定安装目录。它
不会按进程名批量结束其它程序，也不会删除 `%LOCALAPPDATA%\Programs`、
用户目录或其它同名文件夹。

## 使用

1. 安装完成后，托盘区出现 Codex Quota HUD 图标；不会弹出黑色命令窗口。
2. 打开 Codex Desktop。监视器检测到 Codex 后会立即读取一次额度。
3. 读到至少一项额度时显示圆球；可拖动圆球，位置会自动保存。
4. 右键圆球或托盘图标可切换皮肤、开关动画、立即刷新或退出。
5. 同时再次启动程序不会创建第二个后台实例。

## 隐私

Codex Quota HUD：

- 不读取或保存浏览器 Cookie；
- 不保存 OAuth 令牌、账号信息或额度响应正文；
- 不抓取 Codex 页面，也不调用私有网页接口；
- 不开放本地或远程网络端口；
- 只通过本机 `codex app-server` 子进程读取官方额度；
- 只保存浮窗坐标、动画开关、皮肤和最近一次成功刷新时间。

## 故障排查

### 托盘存在但没有圆球

这是以下情况的正常表现：

- Codex Desktop 没有运行；
- 官方响应中 5 小时和每周额度都缺失；
- 最近一次成功数据已经超过 5 分钟，旧数据不再展示。

先打开 Codex，再从托盘选择“立即刷新”。如果托盘显示“暂时读不到额度”，
继续检查下面的 CLI。

### 找不到或无法启动 app-server

在 PowerShell 中检查：

```powershell
Get-Command codex -ErrorAction SilentlyContinue
codex --version
```

浮窗会优先使用 `CODEX_QUOTA_HUD_CODEX_PATH` 指定的 CLI，其次查找
`%LOCALAPPDATA%\OpenAI\Codex\bin` 的用户本地 CLI，再尝试运行中的 Codex
Desktop 资源、`PATH` 和 WindowsApps。特殊安装可设置当前用户环境变量
`CODEX_QUOTA_HUD_CODEX_PATH` 为一个存在的绝对 `codex.exe` 路径，重启浮窗
后生效。

`codex app-server` 是长时间运行的 JSONL 服务，直接在终端启动后等待输入是
正常现象；按 `Ctrl+C` 结束即可。若 CLI 本身报登录或版本错误，请先在 Codex
中完成登录或更新 Codex。

个别 Codex alpha 版本可能出现 `codex login status` 显示已登录，但
`app-server` 的 `account/read` 仍返回空账号。这属于 CLI/app-server 认证
复用问题；浮窗会保持隐藏并在托盘显示读不到额度，不会读取 `auth.json`
内容、浏览器 Cookie，或自行保存令牌绕过。

### 托盘图标被折叠

Windows 可能把新图标收进托盘的“显示隐藏的图标”区域。可将它拖到任务栏
常显区。

### 设置没有保存

确认当前用户对 `%LOCALAPPDATA%` 有写权限。设置写入失败不会中断额度读取，
但托盘和下次启动可能恢复默认皮肤 `HudDial`。
