using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Accounts;
using FlowTrim.Application.Categories;
using FlowTrim.Application.Common;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Transactions;

namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Cuerpo opcional de "marcar como pagado" (EP-14 D5). Sin <c>AccountId</c> solo se mueve la próxima fecha (pagado fuera
/// de FlowTrim, HU-57). Con cuenta se registra un gasto: importe (por defecto el del pago; las facturas varían), fecha
/// (por defecto hoy) y categoría de gastos (por defecto la del pago).
/// </summary>
public sealed record MarkPaidRequest(Guid? AccountId, decimal? Amount, DateOnly? Date, Guid? CategoryId);

/// <summary>
/// Resultado de marcar como pagado: el pago con su nueva fecha, el gasto registrado (nulo si solo se movió la fecha) y si
/// el pago quedó completado (planes de cuotas, HU-74; hasta entonces siempre falso).
/// </summary>
public sealed record MarkPaidResponse(SubscriptionResponse Subscription, TransactionResponse? Transaction, bool Completed);

/// <summary>Caso de uso: marcar como pagado el cobro actual (HU-57) y, si se indica una cuenta, registrar el gasto (HU-73).</summary>
public interface IMarkSubscriptionPaidHandler
{
    Task<HandlerResult<MarkPaidResponse>> HandleAsync(Guid id, MarkPaidRequest? request, CancellationToken cancellationToken);
}

public sealed class MarkSubscriptionPaidHandler : IMarkSubscriptionPaidHandler
{
    public const string CategoryRequiredMessage = "Choose an expense category for this payment.";

    private readonly ISubscriptionRepository _repository;
    private readonly IAccountRepository _accounts;
    private readonly ICategoryRepository _categories;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public MarkSubscriptionPaidHandler(
        ISubscriptionRepository repository,
        IAccountRepository accounts,
        ICategoryRepository categories,
        TimeProvider timeProvider,
        ICurrentUser currentUser)
    {
        _repository = repository;
        _accounts = accounts;
        _categories = categories;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<MarkPaidResponse>> HandleAsync(
        Guid id, MarkPaidRequest? request, CancellationToken cancellationToken)
    {
        var ownerId = _currentUser.GetOwnerId();
        var subscription = await _repository.GetByIdAsync(ownerId, id, cancellationToken);
        if (subscription is null)
        {
            return HandlerResult<MarkPaidResponse>.NotFound();
        }

        if (request?.AccountId is not { } accountId)
        {
            // Solo mover la fecha. Una suscripción inactiva lanza DomainValidationException → 400 (manejador global).
            subscription.AdvanceBillingDate(_timeProvider);
            await _repository.UpdateAsync(subscription, cancellationToken);
            return HandlerResult<MarkPaidResponse>.Success(
                new MarkPaidResponse(SubscriptionMapper.ToResponse(subscription), null, false));
        }

        // El gasto pasa por las mismas reglas que un movimiento creado a mano (cuenta y categoría del propietario y no
        // archivadas, categoría de gastos, fecha ≤ hoy); además la cuenta debe tener la moneda del pago.
        var references = await TransactionRequestValidator.LoadReferencesAsync(_accounts, _categories, ownerId, cancellationToken);
        var expense = new TransactionRequest(
            nameof(TransactionType.Expense),
            accountId,
            null,
            request.CategoryId ?? subscription.CategoryId,
            request.Amount ?? subscription.Amount,
            request.Date ?? ProductCalendar.Today(_timeProvider),
            subscription.Name,
            null);
        var errors = new Dictionary<string, string[]>();
        TransactionRequestValidator.Validate(expense, references, ProductCalendar.Today(_timeProvider), null, errors);
        if (expense.CategoryId is null)
        {
            errors["categoryId"] = [CategoryRequiredMessage];
        }

        if (!errors.ContainsKey("accountId")
            && references.Accounts.TryGetValue(accountId, out var account)
            && account.Currency != subscription.Currency)
        {
            errors["accountId"] = [$"Use an account in {subscription.Currency}, the currency of this payment."];
        }

        if (errors.Count > 0)
        {
            return HandlerResult<MarkPaidResponse>.Invalid(errors);
        }

        // Primero el pago (lanza 400 si está inactivo), luego el gasto; ambos se guardan juntos o ninguno (AC-2).
        subscription.AdvanceBillingDate(_timeProvider);
        var payment = Transaction.Create(
            ownerId,
            TransactionType.Expense,
            accountId,
            null,
            expense.CategoryId,
            expense.Amount!.Value,
            expense.Date!.Value,
            expense.Description,
            null,
            _timeProvider,
            subscription.Id);
        await _repository.RecordPaymentAsync(subscription, payment, cancellationToken);

        return HandlerResult<MarkPaidResponse>.Success(new MarkPaidResponse(
            SubscriptionMapper.ToResponse(subscription), TransactionMapper.ToResponse(payment, references), false));
    }
}
