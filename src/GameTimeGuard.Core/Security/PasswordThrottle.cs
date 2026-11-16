namespace GameTimeGuard.Core.Security;

public sealed class PasswordThrottle
{
    private readonly int _limit;
    private readonly long _windowMilliseconds;
    private readonly long _lockMilliseconds;
    private int _failures;
    private long _windowStart;
    private long _lockedUntil;
    private bool _hasWindow;

    public PasswordThrottle(int limit = 5, TimeSpan? window = null, TimeSpan? lockout = null)
    {
        _limit = limit;
        _windowMilliseconds = (long)(window ?? TimeSpan.FromMinutes(10)).TotalMilliseconds;
        _lockMilliseconds = (long)(lockout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds;
    }

    public bool IsLocked(long monotonicMilliseconds) => monotonicMilliseconds < _lockedUntil;

    public void Fail(long monotonicMilliseconds)
    {
        if (!_hasWindow || monotonicMilliseconds - _windowStart > _windowMilliseconds)
        {
            _failures = 0;
            _windowStart = monotonicMilliseconds;
            _hasWindow = true;
        }

        _failures++;
        if (_failures >= _limit)
        {
            _lockedUntil = monotonicMilliseconds + _lockMilliseconds;
            _failures = 0;
            _hasWindow = false;
        }
    }

    public void Success()
    {
        _failures = 0;
        _hasWindow = false;
    }
}
