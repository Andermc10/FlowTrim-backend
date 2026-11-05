using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Transactions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
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
        var group = app.MapGroup("/api/subscriptions").RequireAuthorization().ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("", CreateAsync).WithName("CreateSubscription").Produces<SubscriptionResponse>(StatusCodes.Status201Created).ProducesValidationProblem();
        group.MapGet("", ListAsync).WithName("ListSubscriptions")
            .Produces<PagedResponse<SubscriptionResponse>>().ProducesValidationProblem();
        group.MapGet("/upcoming", GetUpcomingAsync).WithName("GetUpcomingCharges").Produces<UpcomingChargesResponse>();
        group.MapGet("/{id:guid}", GetAsync).WithName("GetSubscription").Produces<SubscriptionResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateSubscription")
            .Produces<SubscriptionResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteSubscription").Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/mark-paid", MarkPaidAsync).WithName("MarkSubscriptionPaid")
            .Produces<MarkPaidResponse>().ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/payments", ListPaymentsAsync).WithName("ListSubscriptionPayments")
            .Produces<IReadOnlyList<TransactionResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/usage-records", RegisterUsageAsync).WithName("RegisterUsage")
            .Produces<UsageRecordResponse>(StatusCodes.Status201Created).ProducesValidationProblem().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/usage-records", ListUsageAsync).WithName("ListUsage").Produces<IReadOnlyList<UsageRecordResponse>>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapGet("/{id:guid}/cost-per-use", GetCostPerUseAsync).WithName("GetCostPerUse").Produces<CostPerUseResponse>().ProducesProblem(StatusCodes.Status404NotFound);
    }

    // 201 con el recurso creado, o 400 con errores por campo.
    private static async Task<IResult> CreateAsync(
        CreateSubscriptionRequest request, ICreateSubscriptionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/subscriptions/{created.Id}", created));

    // Lista paginada (orden: próxima fecha de cobro, luego nombre), filtrable por tipo. 200 / 400 si el tipo no existe.
    private static async Task<IResult> ListAsync(
        IListSubscriptionsHandler handler, CancellationToken cancellationToken, int? page, int? pageSize, string? kind) =>
        (await handler.HandleAsync(page ?? 1, pageSize ?? ListSubscriptionsHandler.DefaultPageSize, kind, cancellationToken))
            .ToHttpResult(Results.Ok);

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

    // Avanza la próxima fecha un ciclo (HU-57) y, con cuerpo y cuenta, registra el gasto en la misma operación (HU-73).
    // El cuerpo es opcional: sin él solo se mueve la fecha. 200 / 400 (campos, inactiva) / 404.
    private static async Task<IResult> MarkPaidAsync(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MarkPaidRequest? request,
        IMarkSubscriptionPaidHandler handler,
        CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // Últimos 10 gastos registrados al marcar el pago como pagado (los más recientes primero). 200 / 404.
    private static async Task<IResult> ListPaymentsAsync(
        Guid id, IListSubscriptionPaymentsHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } payments
            ? Results.Ok(payments)
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
