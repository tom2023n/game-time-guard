# 游戏时间守护 GameTimeGuard

给家里电脑用的游戏时间管理工具。它在后台记住孩子今天玩了多久，快到点时提醒，时间用完或不在允许时段时，先等 30 秒，再结束游戏进程。

这是 0.2.0。协议是 [MIT](LICENSE)。

## 它管什么

默认先管这三样，和家里以前那套每分钟运行的脚本一致：

- Steam 里 `steamapps\common` 下的游戏（Steam 客户端自己挂在后台不算）
- WeGame 和它目录里的游戏
- 三角洲行动

还可以打开：Epic、战网 / 暴雪、网易游戏、米哈游 / HoYoverse（原神、星穹铁道、绝区零等）、常见腾讯 PC 游戏。也可以自己加一个文件夹或某个 exe。

识别看的是安装路径和数字签名里的发行方，不是 exe 文件名。把 `game.exe` 改名成别的，只要还在原来的目录里，照样算。微信、QQ、网易云音乐不会被算进去。

每天共用一个额度。默认周日 180 分钟，其他日子 120 分钟，全天都可以玩。允许时段可以之后再设，例如每天 18:00–21:00。临时加时 15 / 30 / 60 分钟，只对今天有效，要家长密码。

## 家长密码是唯一的开关

暂停、停止、退出、卸载和改设置都要家长密码。托盘里可以直接「暂停 30 分钟」「停止保护」或「退出程序」。退出后保护不再限制游戏，开始菜单里的「游戏时间守护」可以再打开，用密码恢复。

服务管理器、`sc`、任务管理器、结束进程和直接删记录，会被尽量拦住：服务的访问权限不给管理员停止或删除，进程拒绝普通的结束请求，数据和程序目录由 SYSTEM 拥有并拒绝管理员改写。主服务旁边还有一个看门狗服务，以及每分钟以 SYSTEM 运行的计划任务；主服务被停掉或删掉时，它们会把它装回去并重新启动。

这些挡不住一个下了决心、又知道怎么取得所有权或打开调试权限的管理员。那样做会留下只追加的签名记录，下次打开时托盘会请家长输入密码才能关掉提示，并把服务中断期间的时间补进今天的额度。看不准孩子当时有没有在玩时，默认记 30 分钟罚时，也可以在设置里改成把整段中断都算上。

孩子的账户是管理员还是标准用户都可以用，不需要为了安装去改账户类型。当前账户是管理员时，程序会说明上面这些限度，不会拦住你继续用。

密码只用 PBKDF2-SHA256 加盐保存，没有明文，也没有万能密码。安装时请家长在场，亲自设密码。

## 如果仍然想给孩子用标准账户

这不是必须的。标准账户会让上面那些绕过更难做到。不需要微软账号。

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

1. 用家长的管理员账户运行 `GameTimeGuard-Setup-0.2.0.exe`。
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

正路只有家长密码。其余路径会尽量失败，失败不了就留下记录并补记账。

- **改系统时间。** 服务用包含睡眠时间的单调时钟（`QueryInterruptTime`）推进「可信时间」。同一段开机里把时钟往前拨，不会直接进入下一天；往回拨超过大约 3 分钟，今天的额度按已用完处理，并写入异常记录。能联网时对照阿里云、腾讯和 Windows 的时间服务器。
- **删掉或改掉记录。** 配置、用量和异常记录有三份签名副本：`C:\ProgramData\GameTimeGuard`、注册表 `HKLM\SOFTWARE\GameTimeGuard`，以及 SYSTEM 配置目录下的一份隐藏副本。删掉一份会从另外两份恢复。目录所有权归 SYSTEM，管理员默认不能改、不能删，除非先取得所有权。
- **停服务、结束进程。** 服务的安全描述符不把停止、删除和改配置交给管理员。进程拒绝普通的结束和挂起请求，但不会把自己标成关键进程，所以不会因此蓝屏。看门狗服务和每分钟的 SYSTEM 计划任务会把被删掉的主服务装回去。任务管理器如果带调试权限，仍可能结束进程；进程一停，下次启动会按中断时长补记。
- **卸载和退出。** 要家长密码。密码正确后，服务会放开大约十分钟的停止权限，安装程序才能卸掉。

仍然防不住的事：

- 管理员取得所有权、改掉上面的权限，并且把主服务、看门狗和计划任务一起删掉。
- 关机后在 BIOS 里把日期往后拨，并且断开网络让校时失败，有可能进入下一天。
- 把游戏整个拷到一个没有特征的目录，而且文件没有对应发行方的签名。把那个文件夹加进「自定义游戏」即可。只加某个 exe 的话，改名就能躲开。

解除锁定后，今天已经扣掉的时间不会回来。如果一解除又被锁上，先到 Windows 设置里打开「自动设置时间」，联网校时，再解除。之后如果还想玩，用临时加时。

## 数据放在哪

| 内容 | 位置 |
| --- | --- |
| 配置、用量、状态 | `C:\ProgramData\GameTimeGuard` |
| 签名密钥 | 同目录下的 `secrets`，注册表，以及隐藏副本 |
| 异常记录 | 上面三处里的 `tamper-log.json`，只追加 |
| 隐藏副本 | `C:\Windows\System32\config\systemprofile\AppData\Local\GameTimeGuard` |
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

安装脚本自带简体中文语言文件 `installer/ChineseSimplified.isl`。它来自 [Inno Setup 官方翻译](https://jrsoftware.org/files/istrans/)，许可证和出处写在 `installer/ChineseSimplified.isl.license.txt`。安装好的 Inno Setup 6 并不自带这个文件，所以不要改回 `compiler:Languages\...`。

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
- 服务和进程的访问权限、看门狗、每分钟计划任务
- 服务中断后的补记（进程启动时间、有没有人登录）
- DPAPI 加密的邮箱口令和真正发出的 SMTP 邮件
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

The parent password is the only intended way to pause, stop, exit, uninstall, or change settings. The service keeps running when a parent stops protection, so it can be resumed from the Start menu. Other ways of stopping it are blocked where practical. A determined administrator can still take ownership or use a debug privilege; that is logged, and the missing time is charged on the next start. The child's account does not have to be a standard user.

Core policy (quota, day rollover, time windows, clock tampering, rule matching, HMAC) lives in `GameTimeGuard.Core` and is covered by unit tests. The service, tray UI, and Inno Setup installer target Windows and were cross-compiled, not run, in the environment that produced this tree. `.\build\publish.ps1` on Windows builds a self-contained installer. Tag `v*` publishes that installer to a GitHub Release.
