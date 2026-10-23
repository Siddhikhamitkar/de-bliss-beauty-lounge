using DeBliss.Application.Common.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Api.Common;

/// <summary>
/// Turns the exceptions the Application layer throws into RFC 9457 problem
/// documents, so every module reports errors the same way and no controller has to
/// try/catch anything. Anything unrecognised is logged and handed back to the
/// framework, which produces a generic 500 without leaking internals.
/// </summary>
public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, problem) = exception switch
        {
            ValidationFailedException validation => (
                StatusCodes.Status400BadRequest,
                new ValidationProblemDetails(
                    validation.Errors.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal))
                {
                    Title = "One or more validation errors occurred.",
                    Status = StatusCodes.Status400BadRequest
                }),
            NotFoundException notFound => (
                StatusCodes.Status404NotFound,
                new ProblemDetails
                {
                    Title = "Not found.",
                    Detail = notFound.Message,
                    Status = StatusCodes.Status404NotFound
                }),
            ConflictException conflict => (
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Title = "The request conflicts with existing data.",
                    Detail = conflict.Message,
                    Status = StatusCodes.Status409Conflict
                }),
            // The optimistic-concurrency guard: another user changed the row
            // (typically product stock) between this request reading it and
            // saving. Nothing was written, so the caller can safely reload and
            // retry — which is exactly what the message asks for.
            Microsoft.Data.SqlClient.SqlException { Number: 1205 } or
            DbUpdateException { InnerException: Microsoft.Data.SqlClient.SqlException { Number: 1205 } } => (
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Title = "Checkout figures changed.",
                    Detail = "Another checkout changed the available figures. Reload and try again; this checkout was not completed.",
                    Status = StatusCodes.Status409Conflict
                }),
            DbUpdateConcurrencyException => (
                StatusCodes.Status409Conflict,
                new ProblemDetails
                {
                    Title = "The figures changed while you were working.",
                    Detail = "Someone else updated this record first. Reload the latest figures and try again.",
                    Status = StatusCodes.Status409Conflict
                }),
            _ => (0, null)
        };

        if (problem is null)
        {
            logger.LogError(exception, "Unhandled exception while processing {Path}.", httpContext.Request.Path);
            return false;
        }

        problem.Instance = httpContext.Request.Path;

        httpContext.Response.StatusCode = statusCode;

        // Written against the runtime type, not the ProblemDetails the tuple is
        // declared as: System.Text.Json serializes what the declared type has, so
        // passing the base type would silently drop ValidationProblemDetails.Errors
        // and hand the client a 400 that says which fields failed nowhere.
        await httpContext.Response.WriteAsJsonAsync(problem, problem.GetType(), cancellationToken);

        return true;
    }
}
