# 平台规则怎么加

规则在仓库的 `rules/platforms.json`，也内置在程序里。家长可以把自己的一份放到：

`C:\ProgramData\GameTimeGuard\platforms.json`

程序优先读这一份。这个目录普通用户不能改，所以要家长用管理员账户放进去。

## 一条规则长什么样

```json
{
  "id": "example",
  "displayName": "示例游戏",
  "description": "家长在设置里会看到这段说明。",
  "enabledByDefault": false,
  "pathContainsAny": ["\\ExampleGame\\"],
  "pathNotContainsAny": ["\\ExampleGame\\Uninstall\\"],
  "publishers": ["Example Studio"],
  "publisherAlone": false,
  "publisherPathNotContainsAny": []
}
```

识别时看的是进程的完整路径和数字签名里的发行方，不把 exe 文件名当唯一条件。孩子把 `game.exe` 改成 `homework.exe`，只要还在原来的目录里，一样会被算进去。

- `pathContainsAny`：路径里包含其中任意一段就命中。反斜杠写出来，大小写不敏感。
- `pathNotContainsAny`：先看排除。用来躲开启动器里的无关程序、反作弊和卸载程序。
- `publisherAlone`：为 true 时，签名发行方命中就行，即使家长把游戏装到了自定义目录。只适合签名几乎只出现在游戏上的发行方，例如米哈游、暴雪、Riot。
- 腾讯、网易这种名字会签很多非游戏程序，`publisherAlone` 必须是 false，只靠目录。

改完后跑：

```bash
dotnet test tests/GameTimeGuard.Core.Tests
```

请在 `RuleMatcherTests` 里加一条路径样例：改名后的 exe 仍然命中，微信、QQ、网易云音乐这类程序不要误伤。
