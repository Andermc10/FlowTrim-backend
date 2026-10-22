using FlowTrim.Application.Accounts;

namespace FlowTrim.Api.Accounts;

/// <summary>
/// Cuentas con saldo calculado (HU-64). Todas las rutas exigen sesión y solo ven datos del propietario: una cuenta
/// ajena responde 404, igual que una inexistente. Los conflictos de negocio responden 409 con <c>code</c>.
/// </summary>
public static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/accounts").RequireAuthorization();

        group.MapGet("", ListAsync).WithName("ListAccounts");
        group.MapGet("/{id:guid}", GetAsync).WithName("GetAccount");
        group.MapPost("", CreateAsync).WithName("CreateAccount");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("UpdateAccount");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("DeleteAccount");
    }

    // Sin archivadas salvo ?includeArchived=true.
    private static async Task<IResult> ListAsync(
        IListAccountsHandler handler, CancellationToken cancellationToken, bool? includeArchived) =>
        Results.Ok(await handler.HandleAsync(includeArchived ?? false, cancellationToken));

    private static async Task<IResult> GetAsync(Guid id, IGetAccountHandler handler, CancellationToken cancellationToken) =>
        await handler.HandleAsync(id, cancellationToken) is { } account
            ? Results.Ok(account)
            : ProblemResults.NotFound();

    // 201 con la cuenta creada, o 400 con errores por campo.
    private static async Task<IResult> CreateAsync(
        CreateAccountRequest request, ICreateAccountHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(request, cancellationToken))
            .ToHttpResult(created => Results.Created($"/api/accounts/{created.Id}", created));

    // 200 / 400 / 404 / 409 account_currency_locked.
    private static async Task<IResult> UpdateAsync(
        Guid id, UpdateAccountRequest request, IUpdateAccountHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, request, cancellationToken)).ToHttpResult(Results.Ok);

    // 204 / 404 / 409 account_has_transactions (se puede archivar en su lugar).
    private static async Task<IResult> DeleteAsync(Guid id, IDeleteAccountHandler handler, CancellationToken cancellationToken) =>
        (await handler.HandleAsync(id, cancellationToken)).ToHttpResult(_ => Results.NoContent());
}
