using FlowTrim.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace FlowTrim.Infrastructure.Health;

/// <summary>
/// Implementación de <see cref="IDatabaseHealthChecker"/> que comprueba la conectividad con
/// PostgreSQL abriendo una conexión del pool y ejecutando una consulta trivial.
/// </summary>
internal sealed class NpgsqlDatabaseHealthChecker : IDatabaseHealthChecker
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<NpgsqlDatabaseHealthChecker> _logger;

    public NpgsqlDatabaseHealthChecker(
        NpgsqlDataSource dataSource,
        ILogger<NpgsqlDatabaseHealthChecker> logger)
    {
        _dataSource = dataSource;
        _logger = logger;
    }

    public async Task<bool> CanConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            // "SELECT 1" es la comprobación mínima: valida conexión, autenticación y respuesta del servidor.
            await using NpgsqlCommand command = _dataSource.CreateCommand("SELECT 1");
            await command.ExecuteScalarAsync(cancellationToken);
            return true;
        }
        catch (Exception exception)
        {
            // No exponemos el detalle al cliente; solo lo registramos en el servidor para diagnóstico.
            _logger.LogWarning(exception, "Database health check failed");
            return false;
        }
    }
}
