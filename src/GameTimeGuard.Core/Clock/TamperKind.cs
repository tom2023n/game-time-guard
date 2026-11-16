namespace GameTimeGuard.Core.Clock;

public enum TamperKind
{
    None = 0,
    ClockRollback = 1,
    NtpMismatch = 2,
    TimezoneShift = 3,
    DateRegression = 4,
    RecordTampered = 5
}
