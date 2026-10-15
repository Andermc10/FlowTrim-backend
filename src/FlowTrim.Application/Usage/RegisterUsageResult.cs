namespace FlowTrim.Application.Usage;

/// <summary>Resultado de registrar uso: creado, suscripción no encontrada, o errores de validación.</summary>
public sealed class RegisterUsageResult
{
    public enum ResultKind
    {
        Created,
        NotFound,
        Invalid
    }

    public ResultKind Kind { get; }
    public UsageRecordResponse? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    private RegisterUsageResult(ResultKind kind, UsageRecordResponse? value, IDictionary<string, string[]> errors)
    {
        Kind = kind;
        Value = value;
        Errors = errors;
    }

    public static RegisterUsageResult Created(UsageRecordResponse value) =>
        new(ResultKind.Created, value, new Dictionary<string, string[]>());

    public static RegisterUsageResult NotFound() =>
        new(ResultKind.NotFound, null, new Dictionary<string, string[]>());

    public static RegisterUsageResult Invalid(IDictionary<string, string[]> errors) =>
        new(ResultKind.Invalid, null, errors);
}
