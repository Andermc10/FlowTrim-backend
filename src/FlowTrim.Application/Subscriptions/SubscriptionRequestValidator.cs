using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Domain.Categories;
using FlowTrim.Domain.Subscriptions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Validación de borde compartida por crear y editar: presencia y parseo de lo que el dominio no expresa con tipos.
/// Las reglas de valor (nombre, importe, moneda, categoría) son del dominio.
/// </summary>
internal static class SubscriptionRequestValidator
{
    public static void RequireAmount(decimal? amount, IDictionary<string, string[]> errors)
    {
        if (amount is null)
        {
            errors["amount"] = ["Amount is required."];
        }
    }

    public const string NextBillingDateInPastMessage = "Next billing date cannot be in the past.";

    /// <summary>
    /// Fecha requerida y, si se indica <paramref name="notBefore"/> (hoy en el calendario del producto), no anterior a
    /// ella. Editar sin cambiar una fecha ya vencida no pasa límite: la suscripción vencida se puede seguir editando.
    /// </summary>
    public static void RequireNextBillingDate(DateOnly? nextBillingDate, DateOnly? notBefore, IDictionary<string, string[]> errors)
    {
        if (nextBillingDate is null)
        {
            errors["nextBillingDate"] = ["Next billing date is required."];
        }
        else if (notBefore is { } today && nextBillingDate < today)
        {
            errors["nextBillingDate"] = [NextBillingDateInPastMessage];
        }
    }

    /// <summary>
    /// Tipo (ausente = <paramref name="currentKind"/>), enlace y cuenta y categoría por defecto (EP-14 HU-72, D7). La
    /// cuenta y la categoría deben ser del propietario y la categoría de gastos; archivadas solo valen si no cambian
    /// (editar un pago cuya cuenta se archivó después no falla). El enlace lo valida el dominio.
    /// </summary>
    public static async Task<RecurringPaymentDetails> ParseDetailsAsync(
        string? kind,
        string? paymentUrl,
        Guid? defaultAccountId,
        Guid? categoryId,
        int? totalInstalments,
        int? paidInstalments,
        SubscriptionKind currentKind,
        Subscription? current,
        string ownerId,
        IAccountRepository accounts,
        ICategoryRepository categories,
        IDictionary<string, string[]> errors,
        CancellationToken cancellationToken)
    {
        var parsedKind = kind is null ? currentKind : EnumField.Parse<SubscriptionKind>(kind, "kind", "Kind", errors);

        var accountId = defaultAccountId == Guid.Empty ? null : defaultAccountId;
        if (accountId is { } wantedAccount)
        {
            var account = await accounts.GetByIdAsync(ownerId, wantedAccount, cancellationToken);
            if (account is null)
            {
                errors["defaultAccountId"] = ["Account not found."];
            }
            else if (account.IsArchived && wantedAccount != current?.DefaultAccountId)
            {
                errors["defaultAccountId"] = ["The account is archived. Restore it to use it as the default."];
            }
        }

        var wantedCategoryId = categoryId == Guid.Empty ? null : categoryId;
        if (wantedCategoryId is { } wantedCategory)
        {
            var category = await categories.GetByIdAsync(ownerId, wantedCategory, cancellationToken);
            if (category is null)
            {
                errors["categoryId"] = ["Category not found."];
            }
            else if (category.Kind != CategoryKind.Expense)
            {
                errors["categoryId"] = ["A recurring payment needs an expense category."];
            }
            else if (category.IsArchived && wantedCategory != current?.CategoryId)
            {
                errors["categoryId"] = ["The category is archived. Restore it to use it again."];
            }
        }

        return new RecurringPaymentDetails(
            parsedKind, paymentUrl, accountId, wantedCategoryId, totalInstalments, paidInstalments);
    }
}
