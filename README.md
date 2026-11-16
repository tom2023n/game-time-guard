# 游戏时间守护 GameTimeGuard

给家里电脑用的游戏时间管理工具。它在后台记住孩子今天玩了多久，快到点时提醒，时间用完或不在允许时段时，先等 30 秒，再结束游戏进程。

这是 0.1.0。协议是 [MIT](LICENSE)。

## 它管什么

默认先管这三样，和家里以前那套每分钟运行的脚本一致：

- Steam 里 `steamapps\common` 下的游戏（Steam 客户端自己挂在后台不算）
- WeGame 和它目录里的游戏
- 三角洲行动

还可以打开：Epic、战网 / 暴雪、网易游戏、米哈游 / HoYoverse（原神、星穹铁道、绝区零等）、常见腾讯 PC 游戏。也可以自己加一个文件夹或某个 exe。

识别看的是安装路径和数字签名里的发行方，不是 exe 文件名。把 `game.exe` 改名成别的，只要还在原来的目录里，照样算。微信、QQ、网易云音乐不会被算进去。

每天共用一个额度。默认周日 180 分钟，其他日子 120 分钟，全天都可以玩。允许时段可以之后再设，例如每天 18:00–21:00。临时加时 15 / 30 / 60 分钟，只对今天有效，要家长密码。

## 请先看这一段

**孩子必须使用标准用户，不能是管理员。** 管理员可以停掉服务、改文件、卸掉软件。任何家长控制软件都挡不住一个管理员账户。作者家里的孩子目前用的是管理员账户，所以程序发现当前登录的是管理员时，会给出明确提示。

家长自己留着管理员账户，用来安装和改设置。安装时请家长在场，亲自设密码。密码只用 PBKDF2-SHA256 加盐保存，程序里没有明文，也没有万能密码。

## 给孩子建一个本地标准账户

不需要微软账号。

1. 用家长的管理员账户登录。
2. 打开「设置 → 账户 → 其他用户」（Windows 10 里可能叫「家庭和其他用户」）。
3. 选择「将其他人添加到这台电脑」。
4. 点「我没有这个人的登录信息」。
5. 点「添加一个没有 Microsoft 帐户的用户」。
6. 写下孩子的用户名和密码。
7. 账户类型选 **标准用户**，不要选管理员。
8. 注销，用孩子的账户登录一次。右下角应出现游戏时间守护的图标。
9. 如果这个账户已经是管理员：仍在「其他用户」里打开它，把账户类型改成标准用户。

Windows 专业版也可以用 `lusrmgr.msc`：新建本地用户，不要把它加入 Administrators。家庭版没有这个管理器，用上面的设置步骤即可。

## 安装

1. 用家长的管理员账户运行 `GameTimeGuard-Setup-0.1.0.exe`。
2. 看完安装前说明，设一个家长密码。建议 8 位以上，不要用孩子知道的生日。
3. 确认默认规则：周日 180 分钟，其余 120 分钟，先启用 Steam、WeGame、三角洲行动。
4. 装好后，服务 `GameTimeGuard` 以 LocalSystem 自动运行。每个用户登录时都会打开托盘图标。

托盘图标可以看今天还剩多久、打开设置、申请临时加时。关掉图标不会停止保护，服务过一会儿会再打开它。

设置里可以改每天的分钟数、允许时段、要管哪些平台、自定义游戏，以及家长密码。临时加时和解除「时间被改动」的锁定也在这里，都要密码。

卸载同样要家长密码。安装程序会先弹出验证，密码不对就停在那里。

## 从以前的 PowerShell 脚本迁过来

以前是计划任务每分钟以 SYSTEM 跑一段脚本：看到 Steam、WeGame 或三角洲行动就给 `usage.json` 加一分钟，用完就结束进程；另一个用户级任务负责弹「还剩 5 分钟」和「时间到了」。

装上本程序后，请把那两个计划任务禁用或删除，否则两套东西会各算各的、可能连续关两次游戏。

1. 打开「任务计划程序」。
2. 找到每分钟运行、运行身份是 SYSTEM、会去结束游戏的那个任务。
3. 找到用户登录时弹出提醒的那个任务。
4. 先禁用，确认新图标和剩余时间正常后，再删除。
5. 旧的 `usage.json` 不会导入。从安装当天重新计算。

## 它怎么防着绕过

这些手段是为了挡住标准用户的常见做法，不是为了挡住管理员。

- **改系统时间。** 服务用包含睡眠时间的单调时钟（`QueryInterruptTime`）推进「可信时间」。同一段开机里把时钟往前拨，不会直接进入下一天，也不会多出额度；往回拨超过大约 3 分钟，今天的额度按已用完处理，直到家长解除锁定。电脑睡眠不会被当成篡改。跨过重启时，时钟若比上次记下的可信时间还早，同样锁定。能联网时，还会对照阿里云、腾讯和 Windows 的时间服务器。
- **删掉或改掉记录。** 配置和使用记录放在 `C:\ProgramData\GameTimeGuard`，并用 HMAC-SHA256 签名。注册表 `HKLM\SOFTWARE\GameTimeGuard` 里有一份副本。删掉其中一份会从另一份恢复。两份都坏了、或者用量被换成更旧的一份，就按额度用完处理。签名密钥只有 SYSTEM 和管理员能读。
- **结束进程。** 服务设为失败后 5 秒自动再启动。标准用户停不了它。服务也会看托盘在不在，不在就在用户会话里再打开。
- **卸载。** 要家长密码。

已知的边界，0.1 没有假装能堵住：

- 管理员孩子可以停服务、换文件、删注册表。
- 关机后在 BIOS 里把日期往后拨，并且断开网络让校时失败，有可能直接进入下一天。开机之后再拨时钟，不会这样。
- 把游戏整个拷到一个没有特征的目录，而且文件没有对应发行方的签名时，需要家长把那个文件夹加进「自定义游戏」。只加某个 exe 的话，改名就能躲开，所以优先加文件夹。

解除锁定后，今天已经扣掉的时间不会回来。如果一解除又被锁上，先到 Windows 设置里打开「自动设置时间」，联网校时，再解除。之后如果还想玩，用临时加时。

## 数据放在哪

| 内容 | 位置 |
| --- | --- |
| 配置、用量、状态 | `C:\ProgramData\GameTimeGuard` |
| 签名密钥 | 同目录下的 `secrets`，以及注册表副本 |
| 服务日志 | `C:\ProgramData\GameTimeGuard\logs\service.log` |
| 家长自定义规则 | `C:\ProgramData\GameTimeGuard\platforms.json`（没有这个文件就用安装目录里的） |

`status.json` 只是方便查看的状态，不算数。改它不会增加游戏时间。

## 忘记密码或配置损坏

没有后门。请家长用自己的管理员账户：

1. 以管理员打开命令提示符。
2. `sc stop GameTimeGuard`，然后 `sc delete GameTimeGuard`。
3. 删除 `C:\ProgramData\GameTimeGuard`。
4. 删除注册表 `HKEY_LOCAL_MACHINE\SOFTWARE\GameTimeGuard`。
5. 删除 `HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` 里的 `GameTimeGuard`。
6. 再运行安装包，重新设置密码。

这样做会清掉原来的使用记录。

## 在 Windows 上自己编译安装包

需要 .NET 8 SDK、Inno Setup 6，以及 64 位 Windows。

```powershell
.\build\publish.ps1
```

脚本会跑测试、发布两个自包含单文件（家长不需要另外装运行时），并在装了 Inno Setup 时生成：

`artifacts\installer\GameTimeGuard-Setup-0.1.0.exe`

只编译、不打包：

```powershell
dotnet test GameTimeGuard.sln -c Release
dotnet publish src\GameTimeGuard.Service -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish\service
dotnet publish src\GameTimeGuard.Tray -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts\publish\tray
```

手动安装服务时，还要自己执行：

```text
sc create GameTimeGuard binPath= "C:\Program Files\GameTimeGuard\GameTimeGuard.Service.exe" start= auto obj= LocalSystem
sc failure GameTimeGuard reset= 86400 actions= restart/5000/restart/5000/restart/5000
sc failureflag GameTimeGuard 1
sc start GameTimeGuard
```

`sc` 的等号后面必须有一个空格。

GitHub Actions 在 `windows-latest` 上跑测试、编译安装包。推送 `v*` 标签（例如 `v0.1.0`）时，会把安装包挂到对应的 Release。

## 程序是怎么分成几块的

```text
托盘程序 GameTimeGuard.Tray        每个登录用户一个，负责提醒和设置
        │  命名管道 \\.\pipe\GameTimeGuard
        ▼
服务 GameTimeGuard                 LocalSystem，大约每 5 秒看一次进程
        │
        ├─ GameTimeGuard.Core       配额、时段、可信时钟、规则、签名
        ├─ 结束已匹配的游戏进程
        └─ 签名后的文件 + 注册表副本
```

`GameTimeGuard.Core` 不依赖 Windows。配额、换日、时段、拨时钟、规则匹配和签名都有单元测试，在 Linux 上就能跑：

```bash
dotnet test tests/GameTimeGuard.Core.Tests -c Release
```

要加一个平台，看 [rules/README.md](rules/README.md)。

## 这台开发环境没能在真实 Windows 上跑过的部分

核心库的单元测试已经在 Linux 上通过。服务和托盘工程也用 `EnableWindowsTargeting` 交叉编译通过了，但没有在 Windows 上启动。

还没有在真实 Windows 上验证：

- 服务安装、失败自动重启、目录和注册表的 ACL
- 实际枚举进程、读取路径和数字签名，以及结束进程
- 命名管道、托盘图标、设置窗口、首次向导和弹出提醒
- 从服务往用户会话里拉起托盘
- 联网校时（报文解析有测试，真正的 UDP 请求没有）
- Inno Setup 安装包从安装到卸载的整段过程

请在一台 Windows 10 或 11 的 64 位电脑上，用家长账户走一遍安装、设密码、开一个受管控的游戏、等额度减少、再用卸载密码卸掉。

## 0.1 先不做

- 手机远程开关或加时
- 每周报告
- 每个孩子单独的额度
- 网页游戏和视频网站

## English

GameTimeGuard is a small Windows parental-control tool for game time. A LocalSystem service meters play against one shared daily quota and optional time windows, then closes matching games after a 30-second warning. A per-user tray app shows the remaining time and a password-protected settings window.

The child needs a standard (non-admin) Windows account. An administrator can disable any tool like this. Built-in rules cover Steam, WeGame, Epic, Battle.net, NetEase, miHoYo/HoYoverse, Delta Force, and common Tencent PC games, matched by install path and publisher signature rather than by executable name.

Core policy (quota, day rollover, time windows, clock tampering, rule matching, HMAC) lives in `GameTimeGuard.Core` and is covered by unit tests. The service, tray UI, and Inno Setup installer target Windows and were cross-compiled, not run, in the environment that produced this tree. `.\build\publish.ps1` on Windows builds a self-contained installer. Tag `v*` publishes that installer to a GitHub Release.
