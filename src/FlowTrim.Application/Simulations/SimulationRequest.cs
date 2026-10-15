namespace FlowTrim.Application.Simulations;

/// <summary>
/// Petición de simulación: ids de suscripciones a cancelar y horizontes en meses.
/// Ambos opcionales: sin ids → ahorro cero; sin horizontes → [1, 12, 24].
/// </summary>
public sealed record SimulationRequest(
    IReadOnlyList<Guid>? SubscriptionIds,
    IReadOnlyList<int>? Horizons);
