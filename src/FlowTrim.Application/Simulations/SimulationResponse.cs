namespace FlowTrim.Application.Simulations;

/// <summary>Resultado de la simulación: horizontes, totales por moneda y los supuestos aplicados.</summary>
public sealed record SimulationResponse(
    IReadOnlyList<int> Horizons,
    IReadOnlyList<CurrencySimulationResponse> Currencies,
    IReadOnlyList<string> Assumptions);

public sealed record CurrencySimulationResponse(
    string Currency,
    decimal CurrentMonthly,
    decimal SelectedMonthly,
    decimal ProjectedMonthly,
    IReadOnlyList<SavingsResponse> Savings);

public sealed record SavingsResponse(int Months, decimal Amount);
