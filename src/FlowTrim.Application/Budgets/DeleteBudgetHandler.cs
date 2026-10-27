using FlowTrim.Application.Abstractions;
using FlowTrim.Application.Common;

namespace FlowTrim.Application.Budgets;

/// <summary>Caso de uso: borrar un presupuesto (EP-15 HU-76). No toca ningún movimiento.</summary>
public interface IDeleteBudgetHandler
{
    Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken);
}

public sealed class DeleteBudgetHandler : IDeleteBudgetHandler
{
    private readonly IBudgetRepository _budgets;
    private readonly ICurrentUser _currentUser;

    public DeleteBudgetHandler(IBudgetRepository budgets, ICurrentUser currentUser)
    {
        _budgets = budgets;
        _currentUser = currentUser;
    }

    public async Task<HandlerResult<Done>> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        await _budgets.DeleteAsync(_currentUser.GetOwnerId(), id, cancellationToken)
            ? HandlerResult<Done>.Success(Done.Value)
            : HandlerResult<Done>.NotFound();
}
