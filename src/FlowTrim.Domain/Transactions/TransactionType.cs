namespace FlowTrim.Domain.Transactions;

/// <summary>Tipo de movimiento. Una transferencia nunca cuenta como ingreso ni como gasto.</summary>
public enum TransactionType
{
    Income,
    Expense,
    Transfer,
}
