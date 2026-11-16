using GameTimeGuard.Core.Rules;

namespace GameTimeGuard.Core.Engine;

public interface IProcessQuery
{
    IReadOnlyList<ProcessObservation> Query();
}

public interface IProcessTerminator
{
    void Terminate(int processId);
}

public interface IClockSource
{
    DateTimeOffset GetWallUtc();
    long GetMonotonicMilliseconds();
    TimeSpan GetUtcOffset();
}
