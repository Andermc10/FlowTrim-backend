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
    public static string GetOwnerId(this ICurrentUser currentUser) =>
        currentUser.Subject?.Trim() ?? string.Empty;
}
