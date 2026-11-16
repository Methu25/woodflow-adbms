namespace WoodFlow.Api.Security;

public sealed class RequestSecurityMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        // No redirect of submitted credentials. Plain HTTP remains available for health only.
        if (!context.Request.IsHttps && context.Request.Path != "/api/health")
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { message = "HTTPS is required." });
            return;
        }

        try
        {
            await next(context);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected; do not log request data or database exception details.
        }
        catch (Exception)
        {
            if (context.Response.HasStarted)
            {
                context.Abort();
                return;
            }
            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(new { message = "Service unavailable." });
        }
    }
}
