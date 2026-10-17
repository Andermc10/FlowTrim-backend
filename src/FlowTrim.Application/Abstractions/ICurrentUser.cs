namespace FlowTrim.Application.Abstractions;

/// <summary>Identidad mínima que la capa de aplicación necesita del usuario autenticado.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    string? Subject { get; }
}

/// <summary>Obtiene el identificador estable del propietario para las operaciones de aplicación.</summary>
public static class CurrentUserExtensions
{
    /// <summary>
    /// Devuelve el subject del usuario autenticado. Si falta, falla en lugar de devolver un valor vacío:
    /// la política de autorización ya exige el claim <c>sub</c>, así que llegar aquí sin él es un error
    /// de configuración y nunca debe traducirse en una consulta sin filtro de propietario.
    /// </summary>
    public static string GetOwnerId(this ICurrentUser currentUser)
    {
        var subject = currentUser.Subject?.Trim();
        return string.IsNullOrEmpty(subject)
            ? throw new InvalidOperationException("The authenticated user has no subject claim.")
            : subject;
    }
}
