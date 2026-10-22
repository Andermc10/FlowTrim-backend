using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Usage;

namespace FlowTrim.Api.Subscriptions;

/// <summary>
/// Suscripciones y sus usos declarados (HU-01, HU-02, HU-04, HU-05, HU-06). Todas las rutas exigen sesión y solo ven
/// datos del propietario: una suscripción ajena responde 404, igual que una inexistente.
/// </summary>
public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/subscriptions").RequireAuthorization();

        group.MapPost("", CreateAsync).WithName("CreateSubscription");
        group.MapGet("", ListAsync).WithName("ListSubscriptions");
        group.MapGet("/upcoming", GetUpcomingAsync).WithName("GetUpcomingCharges");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetSubscription");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateSubscription");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteSubscription");
        group.MapPost("/{id:guid}/usage-records", RegisterUsageAsync).WithName("RegisterUsage");
        group.MapGet("/{id:guid}/usage-records", ListUsageAsync).WithName("ListUsage");
        group.MapGet("/{id:guid}/cost-per-use", GetCostPerUseAsync).WithName("GetCostPerUse");
    }

    // 201 con el recurso creado, o 400 con errores por campo.
    private static async Task<IResult> CreateAsync(
        CreateSubscriptionRequest request, ICreateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/subscriptions/{created.Id}", created));

    // Lista paginada (orden: próxima fecha de cobro, luego nombre).
    private static async Task<IResult> ListAsync(
        IListSubscriptionsHandler handler, CancellationToken cancellationToken, int? page, int? pageSize) =>
        Results.Ok(await handler.HandleAsync(page ?? 1, pageSize ?? ListSubscriptionsHandler.DefaultPageSize, cancellationToken));

    // Activas ordenadas por fecha, con cuenta regresiva y ventana de alerta calculadas en el servidor.
    private static async Task<IResult> GetUpcomingAsync(IGetUpcomingChargesHandler handler, CancellationToken cancellationToken) =>
        Results.Ok(await handler.HandleAsync(cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IGetSubscriptionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } subscription
            ? Results.Ok(subscription)
            : ProblemResults.NotFound();

    // Reemplazo completo de los campos editables. 200 / 400 / 404.
    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateSubscriptionRequest request, IUpdateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    private static async Task<IResult> DeleteAsync(Guid id, IDeleteSubscriptionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken)
            ? Results.NoContent()
            : ProblemResults.NotFound();

    // Uso declarado (no verificado). 201 / 400 / 404 si la suscripción no existe.
    private static async Task<IResult> RegisterUsageAsync(
        Guid id, RegisterUsageRequest request, IRegisterUsageHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken))
            .ToHttpResult(record => Results.Created($"/api/subscriptions/{id}/usage-records/{record.Id}", record));

    // Usos de una suscripción, filtrables por periodo. 200 / 404.
    private static async Task<IResult> ListUsageAsync(
        Guid id, IListUsageHandler handler, CancellationToken cancellationToken, DateOnly? from, DateOnly? to) =>
        await handler.HandleAsync(id, from, to, cancellationToken) is { } records
            ? Results.Ok(records)
            : ProblemResults.NotFound();

    // Costo declarado por uso; nunca divide por cero (calculable: false). 200 / 404.
    private static async Task<IResult> GetCostPerUseAsync(
        Guid id, IGetCostPerUseHandler handler, CancellationToken cancellationToken, DateOnly? from, DateOnly? to) =>
        await handler.HandleAsync(id, from, to, cancellationToken) is { } result
            ? Results.Ok(result)
            : ProblemResults.NotFound();
}
