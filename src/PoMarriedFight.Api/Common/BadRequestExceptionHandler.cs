using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace PoMarriedFight.Api.Common;

/// <summary>
/// Answers a malformed request with the status it deserves. Minimal APIs throw <see cref="BadHttpRequestException"/>
/// when a body will not bind — unparseable JSON, a value the converter rejects — and the exception handler would
/// otherwise report that as a 500. A client that sent nonsense would then read it as our fault and retry, and a
/// genuine server fault would be indistinguishable from a typo in a payload.
/// </summary>
public sealed partial class BadRequestExceptionHandler(ILogger<BadRequestExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException bad)
        {
            return false;
        }

        // Both are trivially cheap property reads, but the conversion happens here rather than inside the log call.
        string path = httpContext.Request.Path;
        var reason = bad.Message;
        LogRejected(logger, path, reason);
        httpContext.Response.StatusCode = bad.StatusCode;
        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = bad.StatusCode,
                Title = "The request could not be read.",
                // The exception message names the parameter and the offending path, which is what a client needs to
                // fix it. It describes their own payload, so it discloses nothing of ours.
                Detail = bad.InnerException?.Message ?? bad.Message,
            },
            cancellationToken);
        return true;
    }

    [LoggerMessage(EventId = 1201, Level = LogLevel.Information, Message = "Rejected a malformed request to {Path}: {Reason}")]
    private static partial void LogRejected(ILogger logger, string path, string reason);
}
