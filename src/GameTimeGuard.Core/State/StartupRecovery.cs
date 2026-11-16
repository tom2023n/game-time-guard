using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Quota;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;

namespace GameTimeGuard.Core.State;

public sealed record RecoveryResult(
    GuardConfig Config,
    UsageSnapshot Usage,
    ClockSample? Clock,
    bool TamperLocked,
    bool FirstRun,
    string Message);

public static class StartupRecovery
{
    public static RecoveryResult Resolve(StoreRead configRead, StoreRead usageRead, bool hmacKeyExisted)
    {
        if (configRead.Status == StoreStatus.Unavailable || usageRead.Status == StoreStatus.Unavailable)
        {
            return new RecoveryResult(
                DefaultPolicy.EmergencyLock(),
                UsageSnapshot.Fresh(DateOnly.FromDateTime(DateTime.UtcNow)),
                null,
                true,
                false,
                "配置存储暂时读不出来。在确认数据完好之前，先按锁定处理，并且不会覆盖原来的文件。");
        }

        GuardConfig? config = null;
        if (configRead.Status == StoreStatus.Authentic && configRead.Payload is not null)
            StateCodec.TryParseConfig(configRead.Payload, out config);

        UsageSnapshot? usage = null;
        ClockSample? clock = null;
        if (usageRead.Status == StoreStatus.Authentic && usageRead.Payload is not null &&
            StateCodec.TryParseUsage(usageRead.Payload, out var parsedUsage, out var parsedClock))
        {
            usage = parsedUsage;
            clock = parsedClock;
        }

        if (config is null)
        {
            if (!hmacKeyExisted && configRead.Status == StoreStatus.Missing && usageRead.Status is StoreStatus.Missing)
            {
                return new RecoveryResult(
                    DefaultPolicy.Uninitialized(),
                    UsageSnapshot.Fresh(DateOnly.FromDateTime(DateTime.UtcNow)),
                    null,
                    false,
                    true,
                    "这是第一次运行。");
            }

            var locked = DefaultPolicy.EmergencyLock();
            var lockedUsage = BonusPolicy.LatchRecordTamper(UsageSnapshot.Fresh(DateOnly.FromDateTime(DateTime.UtcNow)));
            return new RecoveryResult(locked, lockedUsage, null, true, false, "配置丢失或校验失败，已锁定游戏。请家长用管理员账户查看说明。");
        }

        if (!config.SetupComplete)
        {
            return new RecoveryResult(config, usage ?? UsageSnapshot.Fresh(DateOnly.FromDateTime(DateTime.UtcNow)), clock, false, false, "家长还没有完成初始化。");
        }

        if (usage is null)
        {
            var fresh = BonusPolicy.LatchRecordTamper(UsageSnapshot.Fresh(clock?.LogicalDate ?? DateOnly.FromDateTime(DateTime.UtcNow)));
            if (config.LastUsageSequence <= 0 && usageRead.Status == StoreStatus.Missing)
            {
                fresh = UsageSnapshot.Fresh(clock?.LogicalDate ?? DateOnly.FromDateTime(DateTime.UtcNow));
                return new RecoveryResult(config, fresh, clock, false, false, configRead.Restored ? "已从备份恢复配置。" : "正在开始记录今天的使用时间。");
            }

            return new RecoveryResult(config, fresh, clock, true, false, "使用记录丢失或校验失败，今天的额度按已用完处理。");
        }

        if (usage.Sequence < config.LastUsageSequence)
        {
            var rolled = BonusPolicy.LatchRecordTamper(usage with { UsedMilliseconds = 0, BonusSeconds = 0 });
            return new RecoveryResult(config, rolled, clock, true, false, "发现被回滚的旧使用记录，今天的额度按已用完处理。");
        }

        if (usage.Sequence > config.LastUsageSequence)
            config.LastUsageSequence = usage.Sequence;

        if (usage.TamperLatched)
            return new RecoveryResult(config, usage, clock, true, false, "之前已经检测到异常，锁定仍然有效。");

        var restored = configRead.Restored || usageRead.Restored;
        return new RecoveryResult(config, usage, clock, false, false, restored ? "已从备份恢复记录。" : "记录校验通过。");
    }
}
