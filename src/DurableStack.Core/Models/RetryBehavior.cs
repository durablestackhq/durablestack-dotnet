namespace DurableStack.Core.Models;

/// <summary>
/// How the delay before each retry of a failed run is computed. Either way, the delay is
/// capped by <c>DurableStackOptions.RetryMaxDelay</c> and optionally randomized by retry
/// jitter.
/// </summary>
public enum RetryBehavior
{
    /// <summary>
    /// Every retry waits the same base delay (the job's initial retry delay, or the global
    /// <c>DurableStackOptions.RetryDelay</c> of 5 seconds when unset).
    /// </summary>
    FixedDelay = 0,

    /// <summary>
    /// Exponential backoff: the base delay doubles with each failed attempt (base, 2x, 4x,
    /// ...), up to <c>DurableStackOptions.RetryMaxDelay</c>.
    /// </summary>
    Backoff = 1,
}
