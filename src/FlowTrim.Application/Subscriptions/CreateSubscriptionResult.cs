namespace FlowTrim.Application.Subscriptions;

/// <summary>
/// Resultado del caso de uso de creación: o bien la suscripción creada, o bien errores de
/// validación por campo (clave = nombre del campo en camelCase, valor = mensajes).
/// </summary>
public sealed class CreateSubscriptionResult
{
    public bool IsSuccess { get; }
    public SubscriptionResponse? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    private CreateSubscriptionResult(bool isSuccess, SubscriptionResponse? value, IDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    public static CreateSubscriptionResult Success(SubscriptionResponse value) =>
        new(true, value, new Dictionary<string, string[]>());

    public static CreateSubscriptionResult Invalid(IDictionary<string, string[]> errors) =>
        new(false, null, errors);
}
