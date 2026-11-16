using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace WoodFlow.Api.Security;

// API-only CSRF validation without requiring Razor/view services.
public sealed class CsrfFilter(IAntiforgery antiforgery) : IAsyncAuthorizationFilter, IOrderedFilter
{
    public int Order => 1000;
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var method = context.HttpContext.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
            || HttpMethods.IsOptions(method) || HttpMethods.IsTrace(method))
            return;
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            context.Result = new BadRequestObjectResult(new { message = "Invalid request." });
        }
    }
}
