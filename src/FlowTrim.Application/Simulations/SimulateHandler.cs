using FlowTrim.Application.Subscriptions;
using FlowTrim.Application.Abstractions;
using FlowTrim.Domain.Simulations;

namespace FlowTrim.Application.Simulations;

/// <summary>Caso de uso: simular la cancelación de suscripciones y el ahorro potencial (sin persistir).</summary>
public interface ISimulateHandler
{
    Task<SimulationResult> HandleAsync(SimulationRequest request, CancellationToken cancellationToken);
}

public sealed class SimulateHandler : ISimulateHandler
{
    private static readonly int[] DefaultHorizons = [1, 12, 24];

    // Supuestos mostrados junto al resultado: nunca se presenta como ahorro garantizado.
    private static readonly string[] Assumptions =
    [
        "Assumes constant prices.",
        "Excludes taxes, penalties and price changes.",
        "Not guaranteed."
    ];

    private readonly ISubscriptionRepository _repository;
    private readonly ICurrentUser _currentUser;

    public SimulateHandler(ISubscriptionRepository repository, ICurrentUser currentUser)
    {
        _repository = repository;
        _currentUser = currentUser;
    }

    public async Task<SimulationResult> HandleAsync(SimulationRequest request, CancellationToken cancellationToken)
    {
        var horizons = request.Horizons is { Count: > 0 } ? request.Horizons : DefaultHorizons;

        if (horizons.Any(months => months <= 0))
        {
            return SimulationResult.Invalid(new Dictionary<string, string[]>
            {
                ["horizons"] = ["Each horizon must be greater than zero."]
            });
        }

        var selectedIds = request.SubscriptionIds is null
            ? new HashSet<Guid>()
            : new HashSet<Guid>(request.SubscriptionIds);

        var active = await _repository.ListActiveAsync(_currentUser.GetOwnerId(), cancellationToken);

        var currencies = SimulationCalculator.Calculate(active, selectedIds, horizons)
            .Select(c => new CurrencySimulationResponse(
                c.Currency,
                c.CurrentMonthly,
                c.SelectedMonthly,
                c.ProjectedMonthly,
                c.Savings.Select(s => new SavingsResponse(s.Months, s.Amount)).ToList()))
            .ToList();

        return SimulationResult.Success(new SimulationResponse(horizons, currencies, Assumptions));
    }
}
