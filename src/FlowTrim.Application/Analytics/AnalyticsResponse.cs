namespace FlowTrim.Application.Analytics;

/// <summary>Analítica del propietario por moneda. <c>ReferenceMonth</c> en formato <c>yyyy-MM</c>.</summary>
public sealed record AnalyticsResponse(string ReferenceMonth, IReadOnlyList<CurrencyAnalyticsResponse> Currencies);

public sealed record CurrencyAnalyticsResponse(
    string Currency,
    decimal MonthlyTotal,
    IReadOnlyList<CategorySpendResponse> ByCategory,
    IReadOnlyList<TopSubscriptionResponse> TopSubscriptions,
    IReadOnlyList<ExpectedChargeResponse> ExpectedCharges);

/// <summary><c>Share</c> es un porcentaje con un decimal.</summary>
public sealed record CategorySpendResponse(string Category, decimal MonthlyEquivalent, decimal Share, int Count);

public sealed record TopSubscriptionResponse(Guid Id, string Name, decimal MonthlyEquivalent);

/// <summary>Cobros esperados (no confirmados) de un mes; <c>Month</c> en formato <c>yyyy-MM</c>.</summary>
public sealed record ExpectedChargeResponse(string Month, decimal Amount, int Count);
