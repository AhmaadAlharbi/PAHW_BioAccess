using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Terminals.Web.Filters;

public class SessionGuardFilter : IActionFilter
{
    public void OnActionExecuting(ActionExecutingContext context)
    {
        var controller = context.ActionDescriptor.RouteValues["controller"];
        var action = context.ActionDescriptor.RouteValues["action"];

        // Ù„Ø§ Ù†ÙØ­Øµ ØµÙØ­Ø§Øª Ø§Ù„Ø¯Ø®ÙˆÙ„ Ù„ØªØ¬Ù†Ø¨ Ø­Ù„Ù‚Ø§Øª Ù„Ø§ Ù†Ù‡Ø§Ø¦ÙŠØ©
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
