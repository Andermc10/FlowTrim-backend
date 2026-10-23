using FlowTrim.Application.Transactions;

namespace FlowTrim.Api.Transactions;

/// <summary>
/// Movimientos registrados por el usuario (HU-66). Todas las rutas exigen sesión y solo ven datos del propietario: un
/// movimiento ajeno responde 404, igual que uno inexistente.
/// </summary>
public static class TransactionEndpoints
{
    public static void MapTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transactions").RequireAuthorization();

        group.MapGet("", ListAsync).WithName("ListTransactions");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetTransaction");
        group.MapPost("", CreateAsync).WithName("CreateTransaction");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateTransaction");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteTransaction");
    }

    // Filtros: from, to (inclusivas), accountId, categoryId, type, search; page/pageSize (50, máx. 200). Los totales por
    // moneda cubren todo el filtro, no solo la página. 200 / 400.
    private static async Task<IResult> ListAsync(
        IListTransactionsHandler handler,
        CancellationToken cancellationToken,
        DateOnly? from,
        DateOnly? to,
        Guid? accountId,
        Guid? categoryId,
        string? type,
        string? search,
        int? page,
        int? pageSize) =>
        (await handler.HandleAsync(
            new ListTransactionsQuery(from, to, accountId, categoryId, type, search, page, pageSize), cancellationToken))
            .ToHttpResult(Results.Ok);

    private static async Task<IResult> GetAsync(Guid id, IGetTransactionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } transaction
            ? Results.Ok(transaction)
            : ProblemResults.NotFound();

    // 201 con el movimiento creado, o 400 con errores por campo.
    private static async Task<IResult> CreateAsync(
        TransactionRequest request, ICreateTransactionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/transactions/{created.Id}", created));

    // Reemplazo completo. 200 / 400 / 404.
    private static async Task<IResult> UpdateAsync(
        Guid id, TransactionRequest request, IUpdateTransactionHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // 204 / 404.
    private static async Task<IResult> DeleteAsync(Guid id, IDeleteTransactionHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken)
            ? Results.NoContent()
            : ProblemResults.NotFound();
}
