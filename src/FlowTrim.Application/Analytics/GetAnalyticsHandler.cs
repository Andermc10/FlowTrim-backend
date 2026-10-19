using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;
using FlowTrim.Application.Subscriptions;
using FlowTrim.Domain.Analytics;

namespace FlowTrim.Application.Analytics;

/// <summary>Caso de uso: analítica por moneda de las suscripciones activas del propietario (HU-50).</summary>
public interface IGetAnalyticsHandler
{
    Task<AnalyticsResult> HandleAsync(int? months, CancellationToken cancellationToken);
}

public sealed class GetAnalyticsHandler : IGetAnalyticsHandler
{
    public const int DefaultMonths = 12;
    public const int MaxMonths = 24;

    private readonly ISubscriptionRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ICurrentUser _currentUser;

    public GetAnalyticsHandler(ISubscriptionRepository repository, TimeProvider timeProvider, ICurrentUser currentUser)
    {
        _repository = repository;
        _timeProvider = timeProvider;
        _currentUser = currentUser;
    }

    public async Task<AnalyticsResult> HandleAsync(int? months, CancellationToken cancellationToken)
    {
        int window = months ?? DefaultMonths;
        if (window is < 1 or > MaxMonths)
        {
            return AnalyticsResult.Invalid(new Dictionary<string, string[]>
            {
                ["months"] = [$"Months must be between 1 and {MaxMonths}."],
            });
        }

        var referenceDate = ProductCalendar.Today(_timeProvider);
        var active = await _repository.ListActiveAsync(_currentUser.GetOwnerId(), cancellationToken);

        var currencies = AnalyticsCalculator.Calculate(active, referenceDate, window)
            .Select(currency => new CurrencyAnalyticsResponse(
                currency.Currency,
                currency.MonthlyTotal,
                currency.ByCategory.Select(c => new CategorySpendResponse(c.Category, c.MonthlyEquivalent, c.Share, c.Count)).ToList(),
                currency.TopSubscriptions.Select(t => new TopSubscriptionResponse(t.Id, t.Name, t.MonthlyEquivalent)).ToList(),
                currency.ExpectedCharges.Select(m => new ExpectedChargeResponse(FormatMonth(m.Year, m.Month), m.Amount, m.Count)).ToList()))
            .ToList();

        return AnalyticsResult.Success(new AnalyticsResponse(FormatMonth(referenceDate.Year, referenceDate.Month), currencies));
    }

    private static string FormatMonth(int year, int month) => $"{year:D4}-{month:D2}";
}
