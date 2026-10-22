using FlowTrim.Domain.Accounts;

namespace FlowTrim.Application.Accounts;

/// <summary>Resultado de borrar una cuenta: una cuenta con movimientos nunca se borra (EP-12 D5).</summary>
public enum AccountDeleteOutcome
{
    Deleted,
    NotFound,
    HasTransactions,
}

/// <summary>Acceso a persistencia para cuentas. La implementación vive en Infrastructure.</summary>
public interface IAccountRepository
{
    /// <summary>Agrega una cuenta nueva y confirma los cambios.</summary>
    Task AddAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Devuelve la cuenta del propietario por id, o <c>null</c> si no existe.</summary>
    Task<Account?> GetByIdAsync(string ownerId, Guid id, CancellationToken cancellationToken);

    /// <summary>Cuentas del propietario ordenadas por nombre; las archivadas solo si se piden.</summary>
    Task<IReadOnlyList<Account>> ListAsync(string ownerId, bool includeArchived, CancellationToken cancellationToken);

    /// <summary>Verdadero si el propietario ya tiene otra cuenta con ese nombre (sin distinguir mayúsculas, archivadas incluidas).</summary>
    Task<bool> NameExistsAsync(string ownerId, string name, Guid? excludingId, CancellationToken cancellationToken);

    /// <summary>
    /// Entradas y salidas por cuenta, sumadas en la base de datos. Solo incluye cuentas con movimientos; con
    /// <paramref name="accountId"/> se limita a esa cuenta.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, AccountActivity>> GetActivityAsync(
        string ownerId, Guid? accountId, CancellationToken cancellationToken);

    /// <summary>Persiste los cambios de una cuenta existente.</summary>
    Task UpdateAsync(Account account, CancellationToken cancellationToken);

    /// <summary>Borra la cuenta si existe y no tiene movimientos.</summary>
    Task<AccountDeleteOutcome> DeleteAsync(string ownerId, Guid id, CancellationToken cancellationToken);
}
