namespace PoFightJudge.Api.Common;

/// <summary>Result of a command that either succeeds or fails with a message. Exceptions are reserved for bugs and infrastructure faults.</summary>
public readonly record struct Outcome(string? Error)
{
    public static readonly Outcome Ok = new((string?)null);

    public bool IsSuccess => Error is null;

    public static Outcome Fail(string error) => new(error);

    public static Outcome<T> Success<T>(T value) => new(value, null);

    public static Outcome<T> Failure<T>(string error) => new(default, error);
}

/// <summary>A command result that carries a value on success. Create through <see cref="Outcome.Success{T}"/> / <see cref="Outcome.Failure{T}"/>.</summary>
public readonly record struct Outcome<T>(T? ValueOrDefault, string? Error)
{
    public bool IsSuccess => Error is null;

    /// <summary>The value; throws when the outcome is a failure so a missed check is loud, not silent.</summary>
    public T Value => IsSuccess ? ValueOrDefault! : throw new InvalidOperationException($"Outcome failed: {Error}");

    public TResult Match<TResult>(Func<T, TResult> onSuccess, Func<string, TResult> onFailure) =>
        IsSuccess ? onSuccess(ValueOrDefault!) : onFailure(Error!);
}
