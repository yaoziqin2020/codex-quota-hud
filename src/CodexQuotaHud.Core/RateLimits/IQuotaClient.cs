using CodexQuotaHud.Core.Models;

namespace CodexQuotaHud.Core.RateLimits;

public interface IQuotaClient
{
    Task<QuotaSnapshot> ReadAsync(CancellationToken cancellationToken);
}
