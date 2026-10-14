namespace FlowTrim.Application.Summaries;

/// <summary>Resumen financiero: totales por moneda (nunca mezclados).</summary>
public sealed record SummaryResponse(IReadOnlyList<CurrencySummaryResponse> Currencies);

/// <summary>Totales de una moneda.</summary>
public sealed record CurrencySummaryResponse(
    string Currency,
    decimal MonthlyEquivalent,
    decimal AnnualProjection,
    int ActiveCount);
