using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Transactions;

/// <summary>
/// Validación de borde de crear y editar un movimiento (HU-66 FR-2, EP-12 §8): presencia, fecha ≤ hoy y las reglas que
/// necesitan consultar cuentas y categorías del propietario. La forma (transferencia ⇔ destino y sin categoría, importe)
/// es del dominio. Una cuenta o categoría ajena responde igual que una inexistente: nunca revela datos de otro usuario.
/// </summary>
internal static class TransactionRequestValidator
{
    public const string FutureDateMessage = "Transaction date cannot be in the future.";
    public const string CurrencyMismatchMessage = "Both accounts of a transfer must use the same currency.";

    /// <summary>Carga las cuentas y categorías del propietario (volúmenes pequeños por usuario).</summary>
    public static async Task<TransactionReferences> LoadReferencesAsync(
        IAccountRepository accounts,
        ICategoryRepository categories,
        string ownerId,
        CancellationToken cancellationToken) =>
        new(
            (await accounts.ListAsync(ownerId, includeArchived: true, cancellationToken)).ToDictionary(a => a.Id),
            (await categories.ListAsync(ownerId, null, includeArchived: true, cancellationToken)).ToDictionary(c => c.Id));

    /// <summary>
    /// Valida la petición y devuelve el tipo. <paramref name="previous"/> es el movimiento que se edita: una cuenta o
    /// categoría archivada que ya tenía puede conservarse, pero no se puede elegir una archivada nueva.
    /// </summary>
    public static TransactionType Validate(
        TransactionRequest request,
        TransactionReferences references,
        DateOnly today,
        Transaction? previous,
        IDictionary<string, string[]> errors)
    {
        var type = EnumField.Parse<TransactionType>(request.Type, "type", "Transaction type", errors);

        if (request.Amount is null)
        {
            errors["amount"] = ["Amount is required."];
        }

        if (request.Date is null)
        {
            errors["date"] = ["Date is required."];
        }
        else if (request.Date > today)
        {
            errors["date"] = [FutureDateMessage];
        }

        var account = CheckAccount(request.AccountId, "accountId", references, previous?.AccountId, errors);

        if (errors.ContainsKey("type"))
        {
            return type;
        }

        if (type == TransactionType.Transfer)
        {
            // La forma la valida el dominio; aquí solo lo que necesita las cuentas.
            if (request.ToAccountId is { } toId && toId != request.AccountId)
            {
                var toAccount = CheckAccount(toId, "toAccountId", references, previous?.ToAccountId, errors);
                if (account is not null && toAccount is not null && account.Currency != toAccount.Currency)
                {
                    errors["toAccountId"] = [CurrencyMismatchMessage];
                }
            }
        }
        else if (request.CategoryId is { } categoryId)
        {
            CheckCategory(categoryId, type, references, previous?.CategoryId, errors);
        }

        return type;
    }

    private static Domain.Accounts.Account? CheckAccount(
        Guid? accountId,
        string field,
        TransactionReferences references,
        Guid? keptId,
        IDictionary<string, string[]> errors)
    {
        if (accountId is null || accountId == Guid.Empty)
        {
            if (field == "accountId")
            {
                errors[field] = ["Account is required."];
            }

            return null;
        }

        if (!references.Accounts.TryGetValue(accountId.Value, out var account))
        {
            errors[field] = ["Account not found."];
            return null;
        }

        if (account.IsArchived && accountId != keptId)
        {
            errors[field] = ["The account is archived. Restore it to record new transactions."];
            return null;
        }

        return account;
    }

    private static void CheckCategory(
        Guid categoryId,
        TransactionType type,
        TransactionReferences references,
        Guid? keptId,
        IDictionary<string, string[]> errors)
    {
        if (!references.Categories.TryGetValue(categoryId, out var category))
        {
            errors["categoryId"] = ["Category not found."];
            return;
        }

        var expectedKind = type == TransactionType.Income ? CategoryKind.Income : CategoryKind.Expense;
        if (category.Kind != expectedKind)
        {
            errors["categoryId"] = [type == TransactionType.Income
                ? "An income needs an income category."
                : "An expense needs an expense category."];
            return;
        }

        if (category.IsArchived && categoryId != keptId)
        {
            errors["categoryId"] = ["The category is archived. Restore it to use it again."];
        }
    }
}
