using FluentValidation;

namespace altinnendata_api.Infrastructure
{
    /// <summary>
    /// Runs the registered FluentValidation validator for the request argument, returning a 400
    /// ValidationProblem (ProblemDetails) when invalid.
    /// </summary>
    public class ValidationFilter<T> : IEndpointFilter
    {
        private readonly IValidator<T> _validator;

        public ValidationFilter(IValidator<T> validator)
        {
            _validator = validator;
        }

        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            var argument = context.Arguments.OfType<T>().FirstOrDefault();
            if (argument is not null)
            {
                var result = await _validator.ValidateAsync(argument);
                if (!result.IsValid)
                    return Results.ValidationProblem(result.ToDictionary());
            }

            return await next(context);
        }
    }

    public static class ValidationExtensions
    {
        public static RouteHandlerBuilder WithValidation<T>(this RouteHandlerBuilder builder) =>
            builder.AddEndpointFilter<ValidationFilter<T>>();
    }
}
