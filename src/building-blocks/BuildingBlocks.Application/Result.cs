namespace BuildingBlocks.Application;

/// <summary>Resultado de um caso de uso: um valor ou uma lista de erros esperados (nunca os dois).</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        Errors = [];
    }

    private Result(IReadOnlyList<Error> errors) => Errors = errors;

    public bool IsSuccess => Errors.Count == 0;

    public IReadOnlyList<Error> Errors { get; }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result: {Errors[0].Code}.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(IReadOnlyList<Error> errors) =>
        errors.Count > 0
            ? new Result<T>(errors)
            : throw new ArgumentException("A failure needs at least one error.", nameof(errors));

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure([error]);
}
