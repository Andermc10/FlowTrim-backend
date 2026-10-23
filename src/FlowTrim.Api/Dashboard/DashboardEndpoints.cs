using FlowTrim.Application.Dashboard;

namespace FlowTrim.Api.Dashboard;

/// <summary>Panel "Hoy" (HU-68). Exige sesión y solo usa datos del propietario.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").RequireAuthorization();

        group.MapGet("/today", GetTodayAsync).WithName("GetTodayDashboard");
    }

    // "Puedes gastar hoy" por moneda con todas sus entradas, cuentas, últimos movimientos y cobros hasta fin de mes. 200.
    private static async Task<IResult> GetTodayAsync(IGetTodayDashboardHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken));
}
