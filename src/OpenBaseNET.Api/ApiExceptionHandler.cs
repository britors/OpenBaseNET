using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using OpenBaseNET.Application;
using OpenBaseNET.Application.Customers;
using OpenBaseNET.Domain;

namespace OpenBaseNET.Api;

internal sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Abort();
            return true;
        }

        var (status, title, code, field) = exception switch
        {
            DomainValidationException error => (422, error.Message, error.Code, error.Field),
            InputValidationException error => (422, error.Message, error.Code, error.Field),
            CustomerNotFoundException error => (404, error.Message, error.Code, (string?)null),
            _ => (500, "An unexpected error occurred.", "INTERNAL_ERROR", (string?)null)
        };
        if (status == 500) logger.LogError(exception, "Unhandled request failure.");

        var problem = new ProblemDetails { Status = status, Title = title };
        problem.Extensions["code"] = code;
        if (field is not null) problem.Extensions["field"] = field;
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
