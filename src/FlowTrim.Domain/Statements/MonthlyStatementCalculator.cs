using FlowTrim.Domain.Dashboard;

namespace FlowTrim.Domain.Statements;

/// <summary>Gasto de una categoría en una moneda durante un mes.</summary>
public sealed record CategorySpending(Guid CategoryId, string Currency, decimal Amount);

/// <summary>Categoría del estado de cuenta: lo gastado, su peso en el gasto del mes y lo del mes anterior.</summary>
public sealed record StatementCategory(Guid CategoryId, decimal Amount, decimal Share, decimal PreviousAmount);

/// <summary>Estado de cuenta de una moneda en un mes, con la comparación contra el mes anterior.</summary>
public sealed record CurrencyStatement(
    string Currency,
    decimal Income,
    decimal Expense,
    decimal Net,
    decimal? SavingsRate,
    decimal PreviousIncome,
    decimal PreviousExpense,
    decimal PreviousNet,
    decimal? ExpenseChange,
    IReadOnlyList<StatementCategory> TopCategories);

/// <summary>
/// Estado de cuenta mensual (EP-16 HU-78, §5). Función pura sobre totales ya sumados en SQL (transferencias excluidas):
/// neto = ingresos − gastos; tasa de ahorro = neto / ingresos × 100 (nula sin ingresos); cambio del gasto contra el mes
/// anterior (nulo si el mes anterior no tuvo gastos); las 5 categorías de mayor gasto con su peso. Porcentajes a 1
/// decimal AwayFromZero. Solo aparecen las monedas con movimientos en el mes; nunca se mezclan monedas.
/// </summary>
public static class MonthlyStatementCalculator
{
    public const int TopCategoriesLimit = 5;

    public static IReadOnlyList<CurrencyStatement> Build(
        IEnumerable<PeriodTotals> current,
        IEnumerable<PeriodTotals> previous,
        IEnumerable<CategorySpending> currentCategories,
        IEnumerable<CategorySpending> previousCategories)
    {
        var previousByCurrency = previous.ToDictionary(t => t.Currency);
        var categories = currentCategories.ToList();
        var previousAmounts = previousCategories.ToDictionary(c => (c.CategoryId, c.Currency), c => c.Amount);

        return current
            .Where(t => t.Income != 0m || t.Expense != 0m)
            .OrderBy(t => t.Currency, StringComparer.Ordinal)
            .Select(totals =>
            {
                var before = previousByCurrency.GetValueOrDefault(totals.Currency);
                var previousIncome = before?.Income ?? 0m;
                var previousExpense = before?.Expense ?? 0m;
                var net = totals.Income - totals.Expense;
                var top = categories
                    .Where(c => c.Currency == totals.Currency && c.Amount > 0m)
                    .OrderByDescending(c => c.Amount)
                    .ThenBy(c => c.CategoryId)
                    .Take(TopCategoriesLimit)
                    .Select(c => new StatementCategory(
                        c.CategoryId,
                        c.Amount,
                        Percent(c.Amount, totals.Expense) ?? 0m,
                        previousAmounts.GetValueOrDefault((c.CategoryId, c.Currency))))
                    .ToList();

                return new CurrencyStatement(
                    totals.Currency,
                    totals.Income,
                    totals.Expense,
                    net,
                    Percent(net, totals.Income),
                    previousIncome,
                    previousExpense,
                    previousIncome - previousExpense,
                    Percent(totals.Expense - previousExpense, previousExpense),
                    top);
            })
            .ToList();
    }

    // Porcentaje a 1 decimal; nulo cuando la base es cero (nunca se divide por cero).
    private static decimal? Percent(decimal part, decimal whole) =>
        whole == 0m ? null : Math.Round(part / whole * 100m, 1, MidpointRounding.AwayFromZero);
}
