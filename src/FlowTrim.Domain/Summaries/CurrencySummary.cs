namespace FlowTrim.Domain.Summaries;

/// <summary>Totales financieros de una moneda (nunca se mezclan monedas distintas).</summary>
public sealed record CurrencySummary(
    string Currency,
    decimal MonthlyEquivalent,
    decimal AnnualProjection,
    int ActiveCount);
