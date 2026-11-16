using GameTimeGuard.Core;
using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;
using GameTimeGuard.Core.State;

namespace GameTimeGuard.Service;

public sealed class GuardRuntime
{
    private readonly object _gate = new();
    private readonly WindowsClock _clock = new();
    private readonly PublisherCache _publishers = new();
    private readonly PasswordThrottle _throttle = new();
    private readonly FileBlobStore _files = new(GuardPaths.Root);
    private readonly FileBlobStore _secrets = new(GuardPaths.Secrets);
    private readonly RegistryBlobStore _registry = new();
    private ILogger _logger = Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
    private EnforcementSession? _session;
    private byte[]? _key;
    private bool _canPersist;
    private bool _started;
    private DateTimeOffset? _ntpUtc;
    private DateTimeOffset _ntpFetchedUtc;

    public void UseLogger(ILogger logger) => _logger = logger;

    public void NoteNtp(DateTimeOffset? sample)
    {
        if (sample is null)
            return;
        lock (_gate)
        {
            _ntpUtc = sample;
            _ntpFetchedUtc = DateTimeOffset.UtcNow;
        }
    }

    public IReadOnlyList<IpcEvent> Poll()
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_session is null)
                return [];
            var result = _session.Poll(FreshNtp());
            if (result.StateChanged && _canPersist)
                PersistUsage();
            WriteStatusQuietly();
            return BuildEvents(result);
        }
    }

    public IpcResponse Handle(IpcRequest request)
    {
        lock (_gate)
        {
            EnsureStarted();
            if (_session is null)
                return Fail(request, "守护服务还没有准备好");

            try
            {
                return request.Op switch
                {
                    IpcOps.Status => Ok(request, _session.BuildStatus(), null),
                    IpcOps.GetSettings => Settings(request),
                    IpcOps.Setup => Setup(request),
                    IpcOps.UpdateSettings => Mutate(request, () => _session.UpdateSettings(request.Password ?? "", request.Settings ?? new SettingsUpdate())),
                    IpcOps.ChangePassword => Mutate(request, () => _session.ChangePassword(request.Password ?? "", request.NewPassword ?? "")),
                    IpcOps.GrantBonus => Mutate(request, () => _session.GrantBonus(request.Password ?? "", request.Minutes ?? 0)),
                    IpcOps.AcknowledgeTamper => Mutate(request, () => _session.AcknowledgeTamper(request.Password ?? "")),
                    IpcOps.AuthorizeUninstall => Authorize(request),
                    _ => Fail(request, "不认识这个操作")
                };
            }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or ArgumentException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(ex, "处理托盘请求失败");
                return Fail(request, "请求没有完成，请再试一次");
            }
        }
    }

    private void EnsureStarted()
    {
        if (_started)
            return;
        _started = true;
        try
        {
            StorageSecurity.PrepareDirectories(_logger);
            var keys = KeyRing.Resolve(_secrets, _registry, RecognizesKey);
            _key = keys.Key;
            if (_key is null)
            {
                _canPersist = false;
                _logger.LogError("读不到签名密钥，也写不出新密钥。先在内存里锁定，避免把记录覆盖坏。");
                _session = LockedSession("签名密钥不可用");
                return;
            }

            var keyExisted = !keys.Created;
            var configRead = RedundantStore.Read(_files, _registry, GuardPaths.ConfigName, _key, "config");
            var usageRead = RedundantStore.Read(_files, _registry, GuardPaths.UsageName, _key, "usage");
            var recovery = StartupRecovery.Resolve(configRead, usageRead, keyExisted);
            _canPersist = configRead.Status != StoreStatus.Unavailable && usageRead.Status != StoreStatus.Unavailable;
            _session = CreateSession(recovery.Config, recovery.Usage, recovery.Clock);
            _logger.LogInformation("{Message}", recovery.Message);
            if (_canPersist && (recovery.FirstRun || recovery.TamperLocked || configRead.Restored || usageRead.Restored || recovery.Config.LastUsageSequence != recovery.Usage.Sequence))
                PersistUsage();
        }
        catch (Exception ex)
        {
            _canPersist = false;
            _logger.LogError(ex, "启动时读取记录失败，先锁定游戏且不覆盖磁盘。");
            _session = LockedSession("启动失败");
        }
    }

    private EnforcementSession CreateSession(GuardConfig config, GameTimeGuard.Core.Quota.UsageSnapshot usage, GameTimeGuard.Core.Clock.ClockSample? clock)
    {
        var catalog = LoadCatalog();
        return new EnforcementSession(config, usage, clock, catalog, new LiveQuery(this), new VerifiedTerminator(CurrentRules, _publishers, _logger), _clock);
    }

    private EnforcementSession LockedSession(string reason)
    {
        var config = DefaultPolicy.EmergencyLock();
        var usage = GameTimeGuard.Core.Quota.BonusPolicy.LatchRecordTamper(GameTimeGuard.Core.Quota.UsageSnapshot.Fresh(DateOnly.FromDateTime(DateTime.UtcNow)));
        usage = usage with { TamperReason = reason };
        return CreateSession(config, usage, null);
    }

    private (IReadOnlyList<PlatformRule> Rules, IReadOnlyList<CustomGameRule> Custom) CurrentRules()
    {
        if (_session is null)
            return ([], []);
        return (_session.Catalog.Enabled(_session.Config.EnabledRuleIds).ToArray(), _session.Config.CustomGames.ToArray());
    }

    private PlatformCatalog LoadCatalog()
    {
        foreach (var path in new[] { GuardPaths.RuleOverride, GuardPaths.InstalledRules })
        {
            try
            {
                if (File.Exists(path))
                    return PlatformCatalog.LoadJson(File.ReadAllText(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException)
            {
                _logger.LogWarning(ex, "规则文件 {Path} 不能用，改用内置规则。", path);
            }
        }

        return PlatformCatalog.LoadEmbedded();
    }

    private bool RecognizesKey(byte[] key)
    {
        var config = RedundantStore.Read(_files, _registry, GuardPaths.ConfigName, key, "config");
        if (config.Status == StoreStatus.Authentic)
            return true;
        var usage = RedundantStore.Read(_files, _registry, GuardPaths.UsageName, key, "usage");
        return usage.Status == StoreStatus.Authentic;
    }

    private void PersistUsage()
    {
        if (!_canPersist || _key is null || _session is null)
            return;
        var next = Math.Max(_session.Usage.Sequence, _session.Config.LastUsageSequence) + 1;
        var payload = StateCodec.SerializeUsage(_session.Usage with { Sequence = next }, _session.Clock);
        var envelope = Integrity.Seal("usage", payload, _key);
        if (!RedundantStore.WriteBoth(_files, _registry, GuardPaths.UsageName, envelope, out var error))
        {
            _logger.LogWarning("使用记录没有写成功：{Error}", error);
            return;
        }

        _session.NoteUsagePersisted(next);
        SaveConfig();
    }

    private void SaveConfig()
    {
        if (!_canPersist || _key is null || _session is null)
            return;
        var envelope = Integrity.Seal("config", StateCodec.SerializeConfig(_session.Config), _key);
        if (!RedundantStore.WriteBoth(_files, _registry, GuardPaths.ConfigName, envelope, out var error))
            _logger.LogWarning("配置没有写成功：{Error}", error);
    }

    private void WriteStatusQuietly()
    {
        if (_session is null)
            return;
        try
        {
            Directory.CreateDirectory(GuardPaths.Root);
            File.WriteAllText(GuardPaths.StatusFile, GuardJson.Serialize(_session.BuildStatus()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogDebug(ex, "状态文件没有写成功");
        }
    }

    private DateTimeOffset? FreshNtp()
    {
        if (_ntpUtc is null || DateTimeOffset.UtcNow - _ntpFetchedUtc > TimeSpan.FromMinutes(10))
            return null;
        return _ntpUtc;
    }

    private static IReadOnlyList<IpcEvent> BuildEvents(EnforcementResult result)
    {
        var events = new List<IpcEvent>();
        var evaluation = result.Evaluation;
        if (evaluation.TamperNewlyLatched)
        {
            events.Add(new IpcEvent
            {
                Name = "tamper",
                Reason = evaluation.ReasonCode,
                Message = "检测到系统时间或游戏记录被改动，今天的额度已锁定。"
            });
        }

        if (evaluation.WarnMinutes is int minutes)
        {
            events.Add(new IpcEvent
            {
                Name = "warn",
                Minutes = minutes,
                Seconds = evaluation.RemainingSeconds,
                Reason = evaluation.ReasonCode,
                Message = evaluation.Message
            });
        }

        if (evaluation.GraceNewlyStarted)
        {
            events.Add(new IpcEvent
            {
                Name = "grace",
                Seconds = evaluation.GraceSecondsRemaining,
                Reason = evaluation.ReasonCode,
                Message = evaluation.Message
            });
        }

        if (evaluation.ShouldNotifyTimeUp)
        {
            events.Add(new IpcEvent
            {
                Name = "timeup",
                Reason = evaluation.ReasonCode,
                Message = evaluation.Message
            });
        }

        if (result.TerminatedPids.Count > 0)
        {
            events.Add(new IpcEvent
            {
                Name = "terminated",
                Seconds = result.TerminatedPids.Count,
                Message = "已关闭游戏。"
            });
        }

        return events;
    }

    private IpcResponse Settings(IpcRequest request)
    {
        if (_session!.Config.SetupComplete)
        {
            var denied = DenyIfLocked(request);
            if (denied is not null)
                return denied;
        }

        return Ok(request, _session.BuildStatus(), _session.BuildSettings());
    }

    private IpcResponse Setup(IpcRequest request)
    {
        if (request.Settings is null)
            return Fail(request, "缺少初始设置");
        var error = _session!.CompleteSetup(request.Password ?? "", request.Settings);
        if (error is not null)
            return Fail(request, error);
        PersistUsage();
        return Ok(request, _session.BuildStatus(), _session.BuildSettings());
    }

    private IpcResponse Mutate(IpcRequest request, Func<string?> action)
    {
        var denied = DenyIfLocked(request);
        if (denied is not null)
            return denied;
        var error = action();
        if (error is not null)
        {
            if (error == "密码不正确")
                _throttle.Fail(_clock.GetMonotonicMilliseconds());
            return Fail(request, error);
        }

        _throttle.Success();
        PersistUsage();
        return Ok(request, _session!.BuildStatus(), _session.BuildSettings());
    }

    private IpcResponse Authorize(IpcRequest request)
    {
        var denied = DenyIfLocked(request);
        if (denied is not null)
            return denied;
        var error = _session!.Authorize(request.Password ?? "");
        if (error is not null)
        {
            _throttle.Fail(_clock.GetMonotonicMilliseconds());
            return Fail(request, error);
        }

        _throttle.Success();
        _logger.LogInformation("家长密码已确认，允许继续卸载。");
        return Ok(request, _session.BuildStatus(), null);
    }

    private IpcResponse? DenyIfLocked(IpcRequest request)
    {
        var now = _clock.GetMonotonicMilliseconds();
        if (_throttle.IsLocked(now))
            return Fail(request, "密码试错太多次，请 5 分钟后再试");
        return null;
    }

    private static IpcResponse Ok(IpcRequest request, StatusReport status, SettingsSnapshot? settings) => new()
    {
        Id = request.Id,
        Ok = true,
        Status = status,
        Settings = settings
    };

    private static IpcResponse Fail(IpcRequest request, string error) => new()
    {
        Id = request.Id,
        Ok = false,
        Error = error
    };

    private sealed class LiveQuery : IProcessQuery
    {
        private readonly GuardRuntime _runtime;

        public LiveQuery(GuardRuntime runtime) => _runtime = runtime;

        public IReadOnlyList<ProcessObservation> Query()
        {
            var (rules, _) = _runtime.CurrentRules();
            return ProcessInspector.Snapshot(rules, _runtime._publishers);
        }
    }
}
