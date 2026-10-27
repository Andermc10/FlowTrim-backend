using FlowTrim.Application.Budgets;

namespace FlowTrim.Api.Budgets;

/// <summary>
/// Presupuestos mensuales por categoría (EP-15 HU-76). Exigen sesión y solo ven datos del propietario: uno ajeno responde
/// 404. Un presupuesto repetido responde 409 <c>budget_exists</c>.
/// </summary>
public static class BudgetEndpoints
{
    public static void MapBudgetEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/budgets").RequireAuthorization();

        group.MapGet("", ListAsync).WithName("ListBudgets");
        group.MapPost("", CreateAsync).WithName("CreateBudget");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateBudget");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteBudget");
    }

    // Avance de cada presupuesto en el mes (?month=yyyy-MM; por defecto, el actual). 200 / 400.
    private static async Task<IResult> ListAsync(IListBudgetsHandler handler, CancellationToken cancellationToken, string? month) =>
        (await handler.HandleAsync(month, cancellationToken)).ToHttpResult(Results.Ok);

    // 201 / 400 / 409 budget_exists.
    private static async Task<IResult> CreateAsync(
        CreateBudgetRequest request, ICreateBudgetHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/budgets/{created.Id}", created));

    // Solo cambia el límite. 200 / 400 / 404.
    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateBudgetRequest request, IUpdateBudgetHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // 204 / 404.
    private static async Task<IResult> DeleteAsync(Guid id, IDeleteBudgetHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());
}
