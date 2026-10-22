using FlowTrim.Application.Analytics;
using FlowTrim.Application.Reports;
using FlowTrim.Application.Simulations;
using FlowTrim.Application.Summaries;

namespace FlowTrim.Api.Spending;

/// <summary>
/// Vistas del gasto del propietario: resumen (HU-03), simulador (HU-07), analítica (HU-50) y reporte CSV (HU-52).
/// Ninguna persiste nada y todas agrupan por moneda, sin convertir.
/// </summary>
public static class SpendingEndpoints
{
    public static void MapSpendingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").RequireAuthorization();

        group.MapGet("/summary", GetSummaryAsync).WithName("GetSummary");
        group.MapPost("/simulations", SimulateAsync).WithName("Simulate");
        group.MapGet("/analytics", GetAnalyticsAsync).WithName("GetAnalytics");
        group.MapGet("/reports/subscriptions.csv", ExportCsvAsync).WithName("ExportSubscriptionsCsv");
    }

    // Equivalente mensual y proyección anual por moneda de las suscripciones activas.
    private static async Task<IResult> GetSummaryAsync(IGetSummaryHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken));

    // Ahorro potencial por horizonte; nunca cancela nada. 200 / 400 si un horizonte no es válido.
    private static async Task<IResult> SimulateAsync(
        SimulationRequest request, ISimulateHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken)).ToHttpResult(Results.Ok);

    // Gasto por categoría, más caras y cobros esperados por mes. 200 / 400 si months está fuera de rango.
    private static async Task<IResult> GetAnalyticsAsync(
        IGetAnalyticsHandler handler, CancellationToken cancellationToken, int? months) =>
        (await handler.HandleAsync(months, cancellationToken)).ToHttpResult(Results.Ok);

    // Todas las suscripciones del propietario como adjunto CSV.
    private static async Task<IResult> ExportCsvAsync(IExportSubscriptionsHandler handler, CancellationToken cancellationToken)
    {
        ExportFile file = await handler.HandleAsync(cancellationToken);
        return Results.File(file.Content, file.ContentType, file.FileName);
    }
}
