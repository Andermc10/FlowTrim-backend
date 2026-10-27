using FlowTrim.Application.Statements;

namespace FlowTrim.Api.Statements;

/// <summary>Estado de cuenta mensual (EP-16 HU-78). Exige sesión y solo usa datos del propietario.</summary>
public static class StatementEndpoints
{
    public static void MapStatementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/statements").RequireAuthorization();

        group.MapGet("", GetAsync).WithName("GetStatement");
    }

    // Ingresos, gastos, neto, tasa de ahorro y categorías del mes contra el anterior. 200 / 400 si el mes no es yyyy-MM.
    private static async Task<IResult> GetAsync(IGetStatementHandler handler, CancellationToken cancellationToken, string? month) =>
        (await handler.HandleAsync(month, cancellationToken)).ToHttpResult(Results.Ok);
}
