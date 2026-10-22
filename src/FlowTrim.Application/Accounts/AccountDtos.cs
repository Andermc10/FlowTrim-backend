using FlowTrim.Domain.Accounts;

namespace FlowTrim.Application.Accounts;

/// <summary>
/// Petición de creación de una cuenta. Campos anulables para distinguir "ausente" de inválido; el tipo llega como
/// texto ("Bank") para reportar valores no admitidos con un 400 por campo.
/// </summary>
public sealed record CreateAccountRequest(string? Name, string? Type, string? Currency, decimal? OpeningBalance);

/// <summary>Reemplazo completo de los campos editables de una cuenta (PUT).</summary>
public sealed record UpdateAccountRequest(
    string? Name,
    string? Type,
    string? Currency,
    decimal? OpeningBalance,
    bool? IsArchived);

/// <summary>Cuenta con su saldo calculado. <c>HasTransactions</c> indica que no se puede borrar ni cambiar de moneda.</summary>
public sealed record AccountResponse(
    Guid Id,
    string Name,
    string Type,
    string Currency,
    decimal OpeningBalance,
    decimal Balance,
    bool IsArchived,
    bool HasTransactions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Mapeo explícito de la entidad y su actividad al DTO público.</summary>
internal static class AccountMapper
{
    public static AccountResponse ToResponse(Account account, AccountActivity? activity) => new(
        account.Id,
        account.Name,
        account.Type.ToString(),
        account.Currency,
        account.OpeningBalance,
        AccountBalanceCalculator.GetBalance(account.OpeningBalance, activity),
        account.IsArchived,
        activity is not null,
        account.CreatedAt,
        account.UpdatedAt);
}

/// <summary>Validación de borde compartida por crear y editar una cuenta.</summary>
internal static class AccountRequestValidator
{
    public static void RequireOpeningBalance(decimal? openingBalance, IDictionary<string, string[]> errors)
    {
        if (openingBalance is null)
        {
            errors["openingBalance"] = ["Opening balance is required."];
        }
    }

    /// <summary>Añade el error de nombre repetido; el índice único lo vuelve a comprobar ante peticiones simultáneas.</summary>
    public static async Task CheckUniqueNameAsync(
        IAccountRepository repository,
        string ownerId,
        string? name,
        Guid? excludingId,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var trimmed = name?.Trim();
        if (!string.IsNullOrEmpty(trimmed) && !errors.ContainsKey("name")
            && await repository.NameExistsAsync(ownerId, trimmed, excludingId, cancellationToken))
        {
            errors["name"] = [Account.DuplicateNameMessage];
        }
    }
}
