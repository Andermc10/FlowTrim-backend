using FlowTrim.Application.Categories;
using FlowTrim.Application.Transactions;
using FlowTrim.Domain.Budgets;

namespace FlowTrim.Application.Budgets;

/// <summary>
/// Avance de los presupuestos del propietario en un mes (EP-15 D2): lo gastado sale de los gastos registrados en ese mes,
/// sumados en SQL por categoría y moneda de la cuenta. Lo comparten la lista de presupuestos y el panel "Hoy".
/// </summary>
internal static class BudgetOverview
{
    public static async Task<IReadOnlyList<BudgetResponse>> BuildAsync(
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        string ownerId,
        DateOnly monthStart,
        CancellationToken cancellationToken)
    {
        var items = await budgets.ListAsync(ownerId, cancellationToken);
        if (items.Count == 0)
        {
            return [];
        }

        var byId = (await categories.ListAsync(ownerId, null, includeArchived: true, cancellationToken)).ToDictionary(c => c.Id);
        var spent = await transactions.GetExpensesByCategoryAsync(
            ownerId, monthStart, monthStart.AddMonths(1).AddDays(-1), cancellationToken);
        var spentByKey = spent.ToDictionary(row => (row.CategoryId, row.Currency), row => row.Amount);

        return items
            .Where(budget => byId.ContainsKey(budget.CategoryId))
            .Select(budget =>
            {
                var category = byId[budget.CategoryId];
                var progress = BudgetCalculator.Evaluate(budget.Limit, spentByKey.GetValueOrDefault((budget.CategoryId, budget.Currency)));
                return new BudgetResponse(
                    budget.Id, category.Id, category.Name, category.Color, category.Icon, category.IsArchived, budget.Currency,
                    budget.Limit, progress.Spent, progress.Remaining, progress.Over, progress.Percent, progress.State.ToString());
            })
            // Lo excedido primero, luego los avisos; dentro de cada estado, por nombre de categoría.
            .OrderByDescending(item => Enum.Parse<BudgetState>(item.State))
            .ThenBy(item => item.CategoryName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Currency, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Un presupuesto con su avance en el mes de hoy (respuesta de crear y editar).</summary>
    public static async Task<BudgetResponse> CurrentAsync(
        IBudgetRepository budgets,
        ICategoryRepository categories,
        ITransactionRepository transactions,
        TimeProvider timeProvider,
        string ownerId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var today = Common.ProductCalendar.Today(timeProvider);
        var items = await BuildAsync(budgets, categories, transactions, ownerId, new DateOnly(today.Year, today.Month, 1), cancellationToken);
        return items.Single(item => item.Id == id);
    }

    /// <summary>Los presupuestos en aviso o excedidos: la tarjeta "Presupuestos a vigilar" de Hoy (EP-15 D4).</summary>
    public static IReadOnlyList<BudgetResponse> ToWatch(IEnumerable<BudgetResponse> items) =>
        items.Where(item => item.State != nameof(BudgetState.OnTrack)).ToList();
}
