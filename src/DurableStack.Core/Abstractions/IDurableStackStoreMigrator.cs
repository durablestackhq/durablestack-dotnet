using System.Threading;
using System.Threading.Tasks;

namespace DurableStack.Core.Abstractions;

public interface IDurableStackStoreMigrator
{
    Task MigrateAsync(CancellationToken cancellationToken = default);
}
