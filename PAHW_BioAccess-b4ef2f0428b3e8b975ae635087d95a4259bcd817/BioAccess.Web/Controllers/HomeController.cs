using Terminals.Web.Contracts;
using Terminals.Web.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace Terminals.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILoginApi _login;
        private readonly IAllowedUsersStore _allowedUsers;

        public HomeController(ILoginApi login, IAllowedUsersStore allowedUsers)
        {
            _login = login;
            _allowedUsers = allowedUsers;
        }
        public IActionResult Index()
        {
            if (HttpContext.Session.GetString("EmpName") != null)
            {
                return Redirect("/dashboard");
            }

            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string empId, string password, CancellationToken ct)
        {
            var result = await _login.LoginAsync(empId, password, ct);

            // ÙØ´Ù„
            if (result.ResultCode != 1)
            {
                TempData["ErrorMsg"] = result.Message;
                return RedirectToAction("Index");
            }

            // âœ… Ø­ÙˆÙ‘Ù„ Ø§Ù„Ø±Ù‚Ù… Ø§Ù„ÙˆØ¸ÙŠÙÙŠ Ø¨Ø£Ù…Ø§Ù†
            if (!int.TryParse(empId, out var empIdInt))
            {
                TempData["ErrorMsg"] = "Ø§Ù„Ø±Ù‚Ù… Ø§Ù„ÙˆØ¸ÙŠÙÙŠ ØºÙŠØ± ØµØ­ÙŠØ­.";
                return RedirectToAction("Index");
            }

            // âœ… ØªØ­Ù‚Ù‚ Ù…Ù† Ø§Ù„Ø³Ù…Ø§Ø­ÙŠØ© Ù‚Ø¨Ù„ Ø£ÙŠ Session
            var allowed = await _allowedUsers.IsAllowedAsync(empIdInt, ct);
            if (!allowed)
            {
                // Ø§Ø­ØªÙŠØ§Ø·: Ø§Ù…Ø³Ø­ Ø£ÙŠ Ø´ÙŠ Ù‚Ø¯ÙŠÙ…
                HttpContext.Session.Clear();

                TempData["ErrorMsg"] = "ØºÙŠØ± Ù…ØµØ±Ø­ Ù„Ùƒ Ø¨Ø§Ù„Ø¯Ø®ÙˆÙ„. Ø±Ø§Ø¬Ø¹ Ù‚Ø³Ù… Ø§Ù„Ø¥Ø¬Ø§Ø²Ø§Øª ÙˆØ§Ù„Ø¯ÙˆØ§Ù….";
                return RedirectToAction("Index");
            }
            
// âœ… Ø®Ø²Ù‘Ù† IsAdmin               
            var isAdmin = await _allowedUsers.IsAdminAsync(empIdInt, ct);
            HttpContext.Session.SetString("IsAdmin", isAdmin ? "1" : "0");


            // âœ… Ø§Ù„Ø¢Ù† ÙÙ‚Ø· Ø®Ø²Ù‘Ù† Session
            HttpContext.Session.SetString("SessionKey", result.SessionKey);
            HttpContext.Session.SetString("EmpName", result.EmployeeName);
            HttpContext.Session.SetString("EmpId", empId);
            // TempData["SuccessMsg"] = $"Welcome {result.EmployeeName}";

            return Redirect("/dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            TempData["SuccessMsg"] = "ØªÙ… ØªØ³Ø¬ÙŠÙ„ Ø§Ù„Ø®Ø±ÙˆØ¬ Ø¨Ù†Ø¬Ø§Ø­";
            return RedirectToAction("Index", "Home");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
