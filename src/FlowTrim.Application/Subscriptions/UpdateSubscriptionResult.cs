namespace FlowTrim.Application.Subscriptions;

/// <summary>Resultado de actualizar: éxito con el recurso, no encontrada, o errores de validación.</summary>
public sealed class UpdateSubscriptionResult
{
    public enum ResultKind
    {
        Success,
        NotFound,
        Invalid
    }

    public ResultKind Kind { get; }
    public SubscriptionResponse? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    private UpdateSubscriptionResult(ResultKind kind, SubscriptionResponse? value, IDictionary<string, string[]> errors)
    {
        Kind = kind;
        Value = value;
        Errors = errors;
    }

    public static UpdateSubscriptionResult Success(SubscriptionResponse value) =>
        new(ResultKind.Success, value, new Dictionary<string, string[]>());

    public static UpdateSubscriptionResult NotFound() =>
        new(ResultKind.NotFound, null, new Dictionary<string, string[]>());

    public static UpdateSubscriptionResult Invalid(IDictionary<string, string[]> errors) =>
        new(ResultKind.Invalid, null, errors);
}
