namespace OttoRice.Common;

/// <summary>Resultado tipado das operações do pipeline (sem exceções para fluxo de controle).</summary>
public sealed record Result(bool IsSuccess, string? Error = null)
{
    public static Result Ok() => new(true);
    public static Result Fail(string error) => new(false, error);
}

public sealed record Result<T>(bool IsSuccess, T? Value, string? Error = null)
{
    public static Result<T> Ok(T value) => new(true, value);
    public static Result<T> Fail(string error) => new(false, default, error);
}
