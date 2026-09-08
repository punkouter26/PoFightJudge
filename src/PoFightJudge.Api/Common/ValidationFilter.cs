using FluentValidation;

namespace PoFightJudge.Api.Common;

/// <summary>
/// Runs the registered FluentValidation validator over the first handler argument of type <typeparamref name="T"/>
/// and turns failures into a 400 validation problem keyed by field — the same messages the editor form shows, since
/// both sides run the same validator from the Shared project.
/// </summary>
public sealed class ValidationFilter<T>(IValidator<T> validator) : IEndpointFilter
    where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is not { } body)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "A request body is required.");
        }

        var result = await validator.ValidateAsync(body, context.HttpContext.RequestAborted);
        return result.IsValid ? await next(context) : Results.ValidationProblem(result.ToDictionary());
    }
}

public static class ValidationFilterExtensions
{
    /// <summary>Validate the <typeparamref name="T"/> body before the handler runs; documents the 400 in OpenAPI.</summary>
    public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder)
        where T : class =>
        builder.AddEndpointFilter<ValidationFilter<T>>().ProducesValidationProblem();
}
