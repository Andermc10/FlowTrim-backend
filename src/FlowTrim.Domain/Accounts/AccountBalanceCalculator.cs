using FlowTrim.Domain.Transactions;

namespace FlowTrim.Domain.Accounts;

/// <summary>Dinero que entró y salió de una cuenta según sus movimientos (solo cuentas con al menos un movimiento).</summary>
public sealed record AccountActivity(Guid AccountId, decimal Incoming, decimal Outgoing);

/// <summary>
/// Saldo calculado de una cuenta (EP-12 D2): nunca se guarda, así no puede desviarse de los movimientos.
/// <c>saldo = saldo inicial + Σ ingresos a ella + Σ transferencias a ella − Σ gastos de ella − Σ transferencias de ella</c>.
/// Funciones puras: el repositorio hace la misma suma agrupada en SQL y las pruebas de integración comparan ambas.
/// </summary>
public static class AccountBalanceCalculator
{
    /// <summary>Agrupa los movimientos por cuenta afectada; una transferencia afecta a dos cuentas.</summary>
    public static IReadOnlyDictionary<Guid, AccountActivity> Summarize(IEnumerable<Transaction> transactions)
    {
        var incoming = new Dictionary<Guid, decimal>();
        var outgoing = new Dictionary<Guid, decimal>();

        foreach (var transaction in transactions)
        {
            switch (transaction.Type)
            {
                case TransactionType.Income:
                    Add(incoming, transaction.AccountId, transaction.Amount);
                    Add(outgoing, transaction.AccountId, 0m);
                    break;
                case TransactionType.Expense:
                    Add(outgoing, transaction.AccountId, transaction.Amount);
                    Add(incoming, transaction.AccountId, 0m);
                    break;
                case TransactionType.Transfer:
                    Add(outgoing, transaction.AccountId, transaction.Amount);
                    Add(incoming, transaction.AccountId, 0m);
                    Add(incoming, transaction.ToAccountId!.Value, transaction.Amount);
                    Add(outgoing, transaction.ToAccountId!.Value, 0m);
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported transaction type: {transaction.Type}");
            }
        }

        return incoming.Keys.ToDictionary(id => id, id => new AccountActivity(id, incoming[id], outgoing[id]));
    }

    /// <summary>Saldo de la cuenta; sin actividad es el saldo inicial.</summary>
    public static decimal GetBalance(decimal openingBalance, AccountActivity? activity) =>
        activity is null ? openingBalance : openingBalance + activity.Incoming - activity.Outgoing;

    private static void Add(Dictionary<Guid, decimal> totals, Guid accountId, decimal amount) =>
        totals[accountId] = totals.GetValueOrDefault(accountId) + amount;
}
