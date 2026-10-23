namespace FlowTrim.Application.Common;

/// <summary>Desenlace de un caso de uso que la API traduce a su código HTTP.</summary>
public enum HandlerResultKind
{
    Success,
    NotFound,
    Invalid,
    Conflict,
}

/// <summary>
/// Resultado común de los casos de uso: el valor, "no encontrado" (incluye datos de otro propietario), errores de
/// validación por campo (clave = campo en camelCase, valor = mensajes) o un conflicto de negocio con un código estable
/// (p. ej. borrar una cuenta con movimientos, EP-12 D5). Evita una clase de resultado por caso de uso.
/// </summary>
public sealed class HandlerResult<T>
    where T : class
{
    public HandlerResultKind Kind { get; }
    public T? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    /// <summary>Código estable del conflicto (snake_case), que el frontend usa para elegir su mensaje.</summary>
    public string? ConflictCode { get; }

    /// <summary>Mensaje en inglés del conflicto.</summary>
    public string? ConflictMessage { get; }

    public bool IsSuccess => Kind == HandlerResultKind.Success;

    private HandlerResult(
        HandlerResultKind kind,
        T? value,
        IDictionary<string, string[]> errors,
        string? conflictCode = null,
        string? conflictMessage = null)
    {
        Kind = kind;
        Value = value;
        Errors = errors;
        ConflictCode = conflictCode;
        ConflictMessage = conflictMessage;
    }

    public static HandlerResult<T> Success(T value) => new(HandlerResultKind.Success, value, new Dictionary<string, string[]>());

    public static HandlerResult<T> NotFound() => new(HandlerResultKind.NotFound, null, new Dictionary<string, string[]>());

    public static HandlerResult<T> Invalid(IDictionary<string, string[]> errors) => new(HandlerResultKind.Invalid, null, errors);

    public static HandlerResult<T> Conflict(string code, string message) =>
        new(HandlerResultKind.Conflict, null, new Dictionary<string, string[]>(), code, message);
}

/// <summary>Valor de éxito de los casos de uso que no devuelven contenido (p. ej. un borrado → 204).</summary>
public sealed class Done
{
    public static Done Value { get; } = new();

    private Done()
    {
    }
}
