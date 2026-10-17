using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Abstractions;
using FlowTrim.Domain.Summaries;

namespace FlowTrim.Application.Summaries;

/// <summary>Caso de uso: calcular el resumen financiero por moneda de las suscripciones activas.</summary>
public interface IGetSummaryHandler
{
    Task<SummaryResponse> HandleAsync(CancellationToken cancellationToken);
}

public sealed class GetSummaryHandler : IGetSummaryHandler
{
    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public GetSummaryHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SummaryResponse> HandleAsync(CancellationToken cancellationToken)
    {
        var active = await _repository.ListActiveAsync(_currentUser.GetOwnerId(), cancellationToken);

        var currencies = SummaryCalculator.Calculate(active)
            .Select(c => new CurrencySummaryResponse(c.Currency, c.MonthlyEquivalent, c.AnnualProjection, c.ActiveCount))
            .ToList();

        return new SummaryResponse(currencies);
    }
}
