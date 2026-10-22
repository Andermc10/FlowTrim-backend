namespace FlowTrim.Application.Common;

/// <summary>Desenlace de un caso de uso que la API traduce a su código HTTP.</summary>
public enum HandlerResultKind
{
    Success,
    NotFound,
    Invalid,
}

/// <summary>
/// Resultado común de los casos de uso: el valor, "no encontrado" (incluye datos de otro propietario) o errores de
/// validación por campo (clave = campo en camelCase, valor = mensajes). Evita una clase de resultado por caso de uso.
/// </summary>
public sealed class HandlerResult<T>
    where T : class
{
    public HandlerResultKind Kind { get; }
    public T? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    public bool IsSuccess => Kind == HandlerResultKind.Success;

    private HandlerResult(HandlerResultKind kind, T? value, IDictionary<string, string[]> errors)
    {
        Kind = kind;
        Value = value;
        Errors = errors;
    }

    public static HandlerResult<T> Success(T value) => new(HandlerResultKind.Success, value, new Dictionary<string, string[]>());

    public static HandlerResult<T> NotFound() => new(HandlerResultKind.NotFound, null, new Dictionary<string, string[]>());

    public static HandlerResult<T> Invalid(IDictionary<string, string[]> errors) => new(HandlerResultKind.Invalid, null, errors);
}
