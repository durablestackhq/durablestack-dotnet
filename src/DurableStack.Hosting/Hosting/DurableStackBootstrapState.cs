using System;
using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Hosting.Hosting;

internal sealed class DurableStackBootstrapState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _initialized;

    public async Task InitializeOnceAsync(Func<CancellationToken, Task> initialize, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _initialized) == 1)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized == 1)
            {
                return;
            }

            await initialize(cancellationToken);
            Volatile.Write(ref _initialized, 1);
        }
        finally
        {
            _gate.Release();
        }
    }
}
