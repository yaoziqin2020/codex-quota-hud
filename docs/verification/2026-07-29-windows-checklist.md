# Codex Quota HUD — Windows 验收记录

## 开发预览补充 — 2026-07-30

- [x] `--preview` 不触发正式开机启动注册。
- [x] 覆盖双额度、仅 5 小时、仅每周和隐藏状态。
- [x] 百分比限制在 `0..100`。
- [x] 预览设置只保存在内存。
- [x] 复用正式 ViewModel、皮肤、详情、托盘和藏边几何。
- [x] Core 测试：55/55。
- [x] App/UI 测试：198/198。
- [x] Release 构建：0 警告、0 错误。
- [x] 正式 HUD 已运行时，预览受同一单实例锁保护并安全退出。
- [x] 上述启动烟测前后，正式设置文件 SHA-256 未变化。
- [ ] 人工查看五套皮肤的双圈和两种单圈状态。
- [ ] 人工查看详情及左、右、上、下四种藏边。
- [ ] 在可用时检查副屏和不同 DPI。
- [ ] 5 小时额度恢复后复核真实 app-server 双窗口数据链路。

日期：2026-07-29
系统：Windows 11 x64，16 逻辑处理器，双显示器
安装目标：`%LOCALAPPDATA%\Programs\CodexQuotaHud`

状态说明：`PASS` 有直接证据；`PARTIAL` 只验证部分条件；`NOT VERIFIED`
表示当前环境没有所需真实状态，未制造假响应。

## 自动验证

| 项目 | 状态 | 证据 |
|---|---|---|
| Release 全量测试 | PASS | Core 55/55、App 153/153 |
| Release 构建 | PASS | 0 warnings、0 errors |
| win-x64 自包含单文件发布 | PASS | 非 PDB 载荷仅 `CodexQuotaHud.App.exe`，170,170,270 bytes |
| 可复现发布 | PASS | 连续两次 SHA-256 均为 `C61786D477863BA4D0EF9597E2C48DEA7452953E4F3D3E4C0518B3AF5924818A` |
| PE GUI subsystem | PASS | PE32+ magic `0x020B`，Subsystem `2` |
| 正式应用图标 | PASS | `ApplicationIcon` 指向品牌 ICO；发布 EXE 可提取 2 个 icon 资源 |
| 安装/卸载安全契约 | PASS | `PackagingScriptTests` 8/8 |
| 四边自动隐藏 | PASS | 多屏负坐标、内部 seam 排除、四边双轴动画、12px 命中条与短发光 pill 测试通过 |
| Popup 位置与五皮肤主题 | PASS | 左右/上下四向避让、实际阴影 inset 后 10px 间距、工作区限位及 5 个主题映射测试通过 |
| 托盘动态数值图标 | PASS | 84/100/无数据映射、16/20/32px 渲染、皮肤色及 HICON/替换清理测试通过 |

## 真实 Windows 桌面验证

| # | 项目 | 状态 | 证据 / 备注 |
|---:|---|---|---|
| 1 | 启动时无黑色命令窗口 | PASS | PE GUI subsystem；桌面窗口枚举无 console |
| 2 | 第二次启动不创建第二个进程 | PASS | 启动前后同一 PID，始终 1 个实例 |
| 3 | Codex 关闭时托盘保留、圆球隐藏 | NOT VERIFIED | 未中断用户正在使用的 Codex |
| 4 | 打开 Codex 后读取并显示圆球 | PASS | 真实 weekly-only 数据显示 `93% / 每周` |
| 5 | 关闭 Codex 后子 app-server 停止 | NOT VERIFIED | 同上；子进程父 PID 与 CLI 路径已核对 |
| 6 | 双额度：中央 5 小时、外层每周 | NOT VERIFIED | 本次官方响应没有 5 小时窗口 |
| 7 | 仅每周：单层且中央标明“每周” | PASS | 最终 LiquidTank 实机截图确认 |
| 8 | 五套皮肤即时切换且重启保持 | PARTIAL | 全部映射/持久化自动测试通过；最终实机确认 HudDial 与 LiquidTank |
| 9 | 悬停过期刷新并加速动画 | PARTIAL | 状态机和 4/24fps 自动测试通过；未制造过期官方响应 |
| 10 | 左右边缘自动隐藏与恢复 | PASS | 副屏左侧收起到 `-2040`；主屏右侧收起到 `1908`，均保留 12px；随后恢复原位置 |
| 11 | app-server 失败不弹错误框 | PASS | alpha CLI 曾返回认证错误，HUD 保持隐藏且进程稳定 |
| 12 | 空闲 CPU / 内存 | PASS | 动画开：63.2 秒增加 4.219 CPU 秒，约 6.7% 单核、0.42% 整机；WS 131.88→140.68MB。动画关对照约 0.06% 整机 |

## 安装与数据安全核对

| 项目 | 状态 | 备注 |
|---|---|---|
| 安装目录精确 | PASS | `%LOCALAPPDATA%\Programs\CodexQuotaHud` |
| 启动项精确 | PASS | 仅 `Run\CodexQuotaHud`，quoted exe + `--background` |
| 单实例 | PASS | 最终运行 1 个 `CodexQuotaHud.App.exe` |
| 无 Cookie / Token 文件 | PASS | 设置仅坐标、动画、皮肤、最近成功刷新时间 |
| 官方协议边界 | PASS | 只使用官方 `codex app-server`；未读 `auth.json` 内容、Cookie、Token 或私有端点 |
| 卸载边界 | 自动 PASS / 真实未执行 | 保留最终安装供用户使用 |

## 未伪造的验收边界

- 官方服务本次没有返回 5 小时窗口，因此双环不记通过。
- 没有为了测试关闭用户正在使用的 Codex。
- Popup 的五主题映射与位置算法已自动验证；最终实机只人工确认当前 LiquidTank，
  其余主题没有逐一截图。
