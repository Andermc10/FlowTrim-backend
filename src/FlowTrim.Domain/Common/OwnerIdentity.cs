namespace FlowTrim.Domain.Common;

/// <summary>Validación del subject del propietario, común a las entidades de finanzas diarias (EP-12).</summary>
public static class OwnerIdentity
{
    /// <summary>Longitud máxima del subject externo usado como propietario (igual que en suscripciones).</summary>
    public const int MaxLength = 200;

    /// <summary>Devuelve el subject recortado o lanza si falta o es demasiado largo.</summary>
    public static string Require(string? ownerId)
    {
        var value = ownerId?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new DomainValidationException("ownerId", "Authenticated owner is required.");
        }

        if (value.Length > MaxLength)
        {
            throw new DomainValidationException("ownerId", $"Owner identifier must be at most {MaxLength} characters.");
        }

        return value;
    }
}
