using FlowTrim.Application.Calendar;

namespace FlowTrim.Api.Calendar;

/// <summary>Calendario financiero (EP-14 HU-75). Exige sesión y solo usa datos del propietario.</summary>
public static class CalendarEndpoints
{
    public static void MapCalendarEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/calendar").RequireAuthorization();

        group.MapGet("", GetAsync).WithName("GetCalendar");
    }

    // Días del mes con cobros esperados y lo registrado, más totales por moneda. 200 / 400 si el mes no es yyyy-MM.
    private static async Task<IResult> GetAsync(IGetCalendarHandler handler, CancellationToken cancellationToken, string? month) =>
        (await handler.HandleAsync(month, cancellationToken)).ToHttpResult(Results.Ok);
}
