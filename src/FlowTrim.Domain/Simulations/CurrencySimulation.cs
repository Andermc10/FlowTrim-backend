namespace FlowTrim.Domain.Simulations;

/// <summary>Ahorro potencial de una moneda para un horizonte en meses.</summary>
public sealed record SavingsAtHorizon(int Months, decimal Amount);

/// <summary>
/// Resultado de la simulación para una moneda: gasto mensual actual, el de lo seleccionado, el
/// proyectado tras cancelar, y el ahorro por horizonte. Nunca mezcla monedas.
/// </summary>
public sealed record CurrencySimulation(
    string Currency,
    decimal CurrentMonthly,
    decimal SelectedMonthly,
    decimal ProjectedMonthly,
    IReadOnlyList<SavingsAtHorizon> Savings);
