using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Terminals.Web.Filters;

public class SessionGuardFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        // API routes handle their own authentication — do not intercept them here.
        var path = context.HttpContext.Request.Path;
        if (path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase))
            return;

        var controller = context.ActionDescriptor.RouteValues["controller"];
        var action = context.ActionDescriptor.RouteValues["action"];

        // لا نفحص صفحات الدخول لتجنب حلقات لا نهائية
        if (string.Equals(controller, "Home", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(action, "Index", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "Login", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "Logout", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "Error", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var empName = context.HttpContext.Session.GetString("EmpName");
        if (string.IsNullOrWhiteSpace(empName))
        {
            context.Result = new RedirectToActionResult("Index", "Home", null);
        }
    }

    public void OnActionExecuted(ActionExecutedContext context)
    {
    }
}
