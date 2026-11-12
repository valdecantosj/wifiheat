namespace WifiHeat.Core.Workflow;

/// <summary>Resultado de un paso del flujo: valor o lista de errores, más advertencias.</summary>
public sealed record OperationResult<T>(T? Value, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Succeeded => Errors.Count == 0;

    public static OperationResult<T> Ok(T value, IReadOnlyList<string>? warnings = null) => new(value, [], warnings ?? []);

    public static OperationResult<T> Fail(params string[] errors) => new(default, errors, []);
}
