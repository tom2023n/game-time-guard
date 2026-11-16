using System.Diagnostics;
using GameTimeGuard.Core;
using GameTimeGuard.Core.Clock;
using GameTimeGuard.Core.Engine;
using GameTimeGuard.Core.Ipc;
using GameTimeGuard.Core.Rules;
using GameTimeGuard.Core.Schedule;
using GameTimeGuard.Core.Security;
using GameTimeGuard.Core.Quota;
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
    private readonly FileBlobStore _shadow = new(GuardPaths.ShadowRoot);
    private readonly RegistryBlobStore _registry = new();
    private readonly DpapiSecretProtector _secretsProtector = new();
    private TamperLogDocument _tamperLog = new();
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
                    IpcOps.Status => Ok(request, Decorate(_session.BuildStatus()), null),
                    IpcOps.GetSettings => Settings(request),
                    IpcOps.Setup => Setup(request),
                    IpcOps.UpdateSettings => Mutate(request, () => _session.UpdateSettings(request.Password ?? "", request.Settings ?? new SettingsUpdate(), _secretsProtector)),
                    IpcOps.ChangePassword => Mutate(request, () => _session.ChangePassword(request.Password ?? "", request.NewPassword ?? "")),
                    IpcOps.GrantBonus => Mutate(request, () => _session.GrantBonus(request.Password ?? "", request.Minutes ?? 0)),
                    IpcOps.AcknowledgeTamper => Mutate(request, () => _session.AcknowledgeTamper(request.Password ?? "")),
                    IpcOps.PauseProtection => Mutate(request, () => _session.Pause(request.Password ?? "", request.Minutes ?? 0)),
                    IpcOps.StopProtection => Mutate(request, () => StopFromParent(request.Password ?? "", suppressTray: false)),
                    IpcOps.ResumeProtection => Mutate(request, () => _session.ResumeProtection(request.Password ?? "")),
                    IpcOps.ExitProgram => Mutate(request, () => StopFromParent(request.Password ?? "", suppressTray: true)),
                    IpcOps.AcknowledgeNotices => Mutate(request, () => AcknowledgeNotices(request.Password ?? "")),
                    IpcOps.TestEmail => TestEmail(request),
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
            var configRead = RedundantStore.ReadMany(Stores(), GuardPaths.ConfigName, _key, "config");
            var usageRead = RedundantStore.ReadMany(Stores(), GuardPaths.UsageName, _key, "usage");
            var recovery = StartupRecovery.Resolve(configRead, usageRead, keyExisted);
            _canPersist = configRead.Status != StoreStatus.Unavailable && usageRead.Status != StoreStatus.Unavailable;
            LoadTamperLog();
            var usage = ApplyGap(recovery.Config, recovery.Usage, recovery.Clock);
            if (recovery.TamperLocked)
                AppendTamper(TamperKinds.RecordTamper, recovery.Message, alert: true);
            _session = CreateSession(recovery.Config, usage, recovery.Clock);
            AccessGuard.ApplyProcessDacl(_logger);
            AccessGuard.ApplyServiceDacl(GuardPaths.ServiceName, ProtectionControl.UninstallArmed(recovery.Config, DateTimeOffset.UtcNow), _logger);
            WriteTrayFlag(recovery.Config);
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
        var config = RedundantStore.ReadMany(Stores(), GuardPaths.ConfigName, key, "config");
        if (config.Status == StoreStatus.Authentic)
            return true;
        var usage = RedundantStore.ReadMany(Stores(), GuardPaths.UsageName, key, "usage");
        return usage.Status == StoreStatus.Authentic;
    }

    private void PersistUsage()
    {
        if (!_canPersist || _key is null || _session is null)
            return;
        var next = Math.Max(_session.Usage.Sequence, _session.Config.LastUsageSequence) + 1;
        var payload = StateCodec.SerializeUsage(_session.Usage with { Sequence = next }, _session.Clock);
        var envelope = Integrity.Seal("usage", payload, _key);
        if (!RedundantStore.WriteMany(Stores(), GuardPaths.UsageName, envelope, out var error))
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
        if (!RedundantStore.WriteMany(Stores(), GuardPaths.ConfigName, envelope, out var error))
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

    private IReadOnlyList<IpcEvent> BuildEvents(EnforcementResult result)
    {
        var events = new List<IpcEvent>();
        var evaluation = result.Evaluation;
        if (evaluation.TamperNewlyLatched)
        {
            var clock = evaluation.Usage.TamperReason?.Contains("Clock", StringComparison.OrdinalIgnoreCase) == true
                || evaluation.Usage.TamperReason?.Contains("Ntp", StringComparison.OrdinalIgnoreCase) == true
                || evaluation.Usage.TamperReason?.Contains("Timezone", StringComparison.OrdinalIgnoreCase) == true;
            AppendTamper(clock ? TamperKinds.ClockChange : TamperKinds.RecordTamper, "检测到系统时间或游戏记录被改动，今天的额度已锁定。", alert: true);
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
            TryEmail(MailNotices.ForTimeUp(_session?.Config ?? new GuardConfig(), evaluation.Message));
            events.Add(new IpcEvent
            {
                Name = "timeup",
                Reason = evaluation.ReasonCode,
                Message = evaluation.Message
            });
        }

        if (result.TerminatedPids.Count > 0)
        {
            AppendTamper(TamperKinds.ProcessKill, $"已关闭 {result.TerminatedPids.Count} 个游戏进程。", alert: false);
            events.Add(new IpcEvent
            {
                Name = "terminated",
                Seconds = result.TerminatedPids.Count,
                Message = "已关闭游戏。"
            });
        }

        MaybeSendDailySummary();

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

        return Ok(request, Decorate(_session.BuildStatus()), _session.BuildSettings());
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
        WriteTrayFlag(_session!.Config);
        return Ok(request, Decorate(_session.BuildStatus()), _session.BuildSettings());
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
            AppendTamper(TamperKinds.UninstallAttempt, "有人尝试卸载，但家长密码不对", alert: true);
            return Fail(request, error);
        }

        var trusted = _session.Clock?.TrustedUtc ?? DateTimeOffset.UtcNow;
        var armed = _session.ArmUninstall(request.Password ?? "", trusted);
        if (armed is not null)
        {
            _throttle.Fail(_clock.GetMonotonicMilliseconds());
            AppendTamper(TamperKinds.UninstallAttempt, "有人尝试卸载，但家长密码不对", alert: true);
            return Fail(request, armed);
        }

        _throttle.Success();
        PersistUsage();
        AccessGuard.ApplyServiceDacl(GuardPaths.ServiceName, allowAdminStop: true, _logger);
        AccessGuard.ApplyServiceDacl(GuardPaths.WatchServiceName, allowAdminStop: true, _logger);
        AppendTamper(TamperKinds.UninstallAttempt, "家长密码已确认，十分钟内允许卸载", alert: true);
        _logger.LogInformation("家长密码已确认，允许继续卸载。");
        return Ok(request, Decorate(_session.BuildStatus()), null);
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

    public bool ShouldLaunchTray()
    {
        lock (_gate)
        {
            EnsureStarted();
            return _session is not { Config.TraySuppressed: true };
        }
    }

    private IReadOnlyList<IBlobStore> Stores() => [_files, _registry, _shadow];

    private void LoadTamperLog()
    {
        if (_key is null)
            return;
        var read = RedundantStore.ReadMany(Stores(), "tamper-log.json", _key, "tamper-log");
        if (read.Status == StoreStatus.Authentic && read.Payload is not null)
            _tamperLog = GuardJson.Deserialize<TamperLogDocument>(read.Payload) ?? new TamperLogDocument();
    }

    private UsageSnapshot ApplyGap(GuardConfig config, UsageSnapshot usage, ClockSample? clock)
    {
        if (clock is not { HasSample: true })
            return usage;
        DateTimeOffset? idleUntil = null;
        if (config.ProtectionState.Equals(ProtectionControl.Stopped, StringComparison.OrdinalIgnoreCase))
            idleUntil = DateTimeOffset.MaxValue;
        else if (ProtectionControl.TryParse(config.PauseUntilUtc, out var pauseUntil))
            idleUntil = pauseUntil;
        var decision = GapBackfill.Estimate(new GapRequest(
            clock.TrustedUtc,
            DateTimeOffset.UtcNow,
            TrayLauncher.AnyInteractiveSession(),
            idleUntil,
            CurrentSightings(config),
            config.GapPolicy,
            config.GapPenaltyMinutes));
        if (decision.ChargeSeconds <= 0)
            return usage;
        AppendTamper(TamperKinds.ServiceGap, decision.Message + $"（{decision.ChargeSeconds / 60} 分钟）", alert: true);
        var used = (long)usage.UsedMilliseconds + decision.ChargeSeconds * 1000L;
        if (used > int.MaxValue)
            used = int.MaxValue;
        return usage with { UsedMilliseconds = (int)used };
    }

    private List<ProcessSighting> CurrentSightings(GuardConfig config)
    {
        var catalog = LoadCatalog();
        var matched = RuleMatcher.MatchAll(ProcessInspector.Snapshot(catalog.Enabled(config.EnabledRuleIds), _publishers), catalog.Enabled(config.EnabledRuleIds), config.CustomGames);
        var sightings = new List<ProcessSighting>();
        foreach (var match in matched)
        {
            DateTimeOffset? started = null;
            try
            {
                using var process = Process.GetProcessById(match.Process.ProcessId);
                started = new DateTimeOffset(process.StartTime).ToUniversalTime();
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or ArgumentException)
            {
            }

            sightings.Add(new ProcessSighting(match.DisplayName, started, true));
        }

        return sightings;
    }

    private string? StopFromParent(string password, bool suppressTray)
    {
        var error = _session!.StopProtection(password, suppressTray);
        if (error is null)
            AppendTamper(TamperKinds.ProtectionChanged, suppressTray ? "家长退出了程序，保护已停止。" : "家长停止了保护。", alert: false);
        return error;
    }

    private string? AcknowledgeNotices(string password)
    {
        var denied = _session!.Authorize(password);
        if (denied is not null)
            return denied;
        _tamperLog = TamperLog.AcknowledgeAll(_tamperLog);
        PersistTamperLog();
        return null;
    }

    private IpcResponse TestEmail(IpcRequest request)
    {
        var denied = DenyIfLocked(request);
        if (denied is not null)
            return denied;
        var error = _session!.UpdateSettings(request.Password ?? "", request.Settings ?? DefaultPolicy.FromConfig(_session.Config), _secretsProtector);
        if (error is not null && request.Settings is not null)
            return Fail(request, error);
        if (request.Settings is null)
        {
            var passwordError = _session.Authorize(request.Password ?? "");
            if (passwordError is not null)
                return Fail(request, passwordError);
        }

        var draft = MailNotices.Test(_session.Config);
        if (draft is null)
            return Fail(request, "邮件通知还没填完整");
        var sendError = MailSender.Send(_session.Config, draft, _secretsProtector, _logger);
        return sendError is null ? Ok(request, Decorate(_session.BuildStatus()), _session.BuildSettings()) : Fail(request, sendError);
    }

    private void AppendTamper(string kind, string message, bool alert)
    {
        var utc = _session?.Clock?.TrustedUtc ?? DateTimeOffset.UtcNow;
        _tamperLog = TamperLog.Append(_tamperLog, kind, message, utc, alert);
        PersistTamperLog();
        if (alert && _session is not null)
            TryEmail(MailNotices.ForTamper(_session.Config, message));
    }

    private void PersistTamperLog()
    {
        if (!_canPersist || _key is null)
            return;
        var envelope = Integrity.Seal("tamper-log", GuardJson.Serialize(_tamperLog), _key);
        RedundantStore.WriteMany(Stores(), "tamper-log.json", envelope, out _);
    }

    private void MaybeSendDailySummary()
    {
        if (_session is null || !MailNotices.IsConfigured(_session.Config) || !_session.Config.NotifyDailySummary)
            return;
        var today = _session.Usage.LogicalDate.ToString("yyyy-MM-dd");
        if (_session.Config.LastSummaryDate == today)
            return;
        var local = _session.Clock?.TrustedUtc.ToOffset(_session.Clock.StoredOffset) ?? DateTimeOffset.Now;
        if (local.Hour < 20)
            return;
        var status = _session.BuildStatus();
        var body = $"日期 {status.LogicalDate}，已用 {status.UsedSeconds / 60} 分钟，剩余 {status.RemainingSeconds / 60} 分钟。";
        TryEmail(MailNotices.ForDailySummary(_session.Config, body));
        _session.Config.LastSummaryDate = today;
        SaveConfig();
    }

    private void TryEmail(MailDraft? draft)
    {
        if (draft is null || _session is null)
            return;
        var config = _session.Config;
        _ = Task.Run(() =>
        {
            try
            {
                MailSender.Send(config, draft, _secretsProtector, _logger);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException)
            {
                _logger.LogDebug(ex, "发送通知时出错");
            }
        });
    }

    private StatusReport Decorate(StatusReport status)
    {
        status.PendingNotices = TamperLog.Pending(_tamperLog).Select(item => new TamperNotice
        {
            Id = item.Id,
            Utc = item.Utc,
            Kind = item.Kind,
            Message = item.Message
        }).ToList();
        return status;
    }

    private static void WriteTrayFlag(GuardConfig config)
    {
        try
        {
            File.WriteAllText(GuardPaths.TrayFlag, config.TraySuppressed ? "hide" : "show");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

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
