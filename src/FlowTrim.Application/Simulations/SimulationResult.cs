namespace FlowTrim.Application.Simulations;

/// <summary>Resultado del caso de uso: la simulación calculada o errores de validación por campo.</summary>
public sealed class SimulationResult
{
    public bool IsSuccess { get; }
    public SimulationResponse? Value { get; }
    public IDictionary<string, string[]> Errors { get; }

    private SimulationResult(bool isSuccess, SimulationResponse? value, IDictionary<string, string[]> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    public static SimulationResult Success(SimulationResponse value) =>
        new(true, value, new Dictionary<string, string[]>());

    public static SimulationResult Invalid(IDictionary<string, string[]> errors) =>
        new(false, null, errors);
}
