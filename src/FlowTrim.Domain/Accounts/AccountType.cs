namespace FlowTrim.Domain.Accounts;

/// <summary>Tipo de cuenta. Las tarjetas de crédito no cuentan como dinero disponible (es dinero prestado).</summary>
public enum AccountType
{
    Cash,
    Bank,
    CreditCard,
    Savings,
}
