namespace DisplayManager.Core;

sealed class DisplayApplyGate
{
    readonly object _syncRoot = new();
    readonly TimeSpan _cooldown;
    readonly Func<TimeSpan> _getElapsed;
    readonly Action<string> _log;
    bool _applyInFlight;
    TimeSpan _cooldownEndsAt;

    public DisplayApplyGate(TimeSpan cooldown, Func<TimeSpan> getElapsed, Action<string> log)
    {
        _cooldown = cooldown;
        _getElapsed = getElapsed;
        _log = log;
    }

    public ApplyResult Apply(Func<ApplyResult> apply)
    {
        var dropMessage = TryStartApply();
        if (dropMessage is not null)
        {
            _log(dropMessage);
            return new ApplyResult
            {
                Success = false,
                Errors = [dropMessage]
            };
        }

        try
        {
            return apply();
        }
        finally
        {
            lock (_syncRoot)
            {
                _cooldownEndsAt = _getElapsed() + _cooldown;
                _applyInFlight = false;
            }
        }
    }

    string? TryStartApply()
    {
        lock (_syncRoot)
        {
            if (_applyInFlight)
            {
                return "Display apply dropped: another apply is already in flight.";
            }

            var remaining = _cooldownEndsAt - _getElapsed();
            if (remaining > TimeSpan.Zero)
            {
                return $"Display apply dropped: cooldown active for another {Math.Ceiling(remaining.TotalMilliseconds)} ms.";
            }

            _applyInFlight = true;
            return null;
        }
    }
}
