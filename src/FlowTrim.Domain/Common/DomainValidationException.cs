namespace FlowTrim.Domain.Common;

/// <summary>
/// Se lanza cuando se viola una invariante de dominio al construir o mutar una entidad.
/// Incluye el nombre de la propiedad ofensora para que las capas superiores puedan mapearla
/// a un error de validación por campo (p. ej. un 400 en la API).
/// </summary>
public sealed class DomainValidationException : Exception
{
    /// <summary>Nombre de la propiedad que incumple la invariante (en camelCase, como la expone la API).</summary>
    public string PropertyName { get; }

    public DomainValidationException(string propertyName, string message) : base(message)
    {
        PropertyName = propertyName;
    }
}
