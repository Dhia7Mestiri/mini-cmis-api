using System.Net;
using System.Text.Json;

namespace CMIS_IyaSoft.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    // Maps to the CMIS 1.1 error model: { "exception": ..., "message": ... }
    // with standard CMIS exception names and matching HTTP status codes.
    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, cmisException) = exception switch
        {
            KeyNotFoundException => ((int)HttpStatusCode.NotFound, "objectNotFound"),
            InvalidOperationException ioe when ioe.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase)
                => ((int)HttpStatusCode.Conflict, "nameConstraintViolation"),
            InvalidOperationException ioe when ioe.Message.Contains("Unsupported cmisaction", StringComparison.OrdinalIgnoreCase)
                => ((int)HttpStatusCode.MethodNotAllowed, "notSupported"),
            InvalidOperationException => ((int)HttpStatusCode.BadRequest, "invalidArgument"),
            UnauthorizedAccessException => ((int)HttpStatusCode.Unauthorized, "permissionDenied"),
            _ => ((int)HttpStatusCode.InternalServerError, "runtime")
        };

        context.Response.StatusCode = statusCode;

        var response = new
        {
            exception = cmisException,
            message = exception.Message
        };

        return context.Response.WriteAsync(JsonSerializer.Serialize(response));
    }
}
