namespace FlowTrim.Application.Common;

/// <summary>Página de resultados con metadata de paginación.</summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);
