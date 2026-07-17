using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AeroBus.Core.Data
{
    /// <summary>
    /// One-shot startup task: make sure the named control database exists before
    /// the outbox pump, tenant resolution or auth-time reads touch it. Idempotent
    /// (EnsureDatabaseAsync treats already-attached as success) and best-effort
    /// with a couple of retries — a DocumentForge that's still coming up must not
    /// crash AeroBus; callers fail loudly per-request until it's reachable anyway.
    /// </summary>
    public sealed class ControlDatabaseInitializer(
        IServiceScopeFactory scopes,
        IOptions<DocumentForgeOptions> options,
        ILogger<ControlDatabaseInitializer> log) : IHostedService
    {
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var name = options.Value.ControlDatabase;
            if (string.IsNullOrWhiteSpace(name)) return;

            for (var attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var control = scope.ServiceProvider
                        .GetRequiredKeyedService<IDocumentForgeClient>(ServiceCollectionExtensions.ControlClientKey);
                    if (await control.EnsureDatabaseAsync(name, cancellationToken))
                    {
                        log.LogInformation("Control database '{Name}' is ready.", name);
                        return;
                    }
                }
                catch (Exception ex) when (attempt < 3)
                {
                    log.LogWarning(ex, "Ensuring control database '{Name}' failed (attempt {Attempt}/3); retrying.", name, attempt);
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt), cancellationToken);
                    continue;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Could not ensure control database '{Name}' at startup; control-plane calls will fail until DocumentForge is reachable.", name);
                    return;
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
