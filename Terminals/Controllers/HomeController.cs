using Terminals.Web.Contracts;
using Terminals.Web.Models;
using Microsoft.AspNetCore.Http;
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
                return RedirectToAction("Index", "Dashboard");
            }

            return View();
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string empId, string password, CancellationToken ct)
        {
            var result = await _login.LoginAsync(empId, password, ct);

            // فشل
            if (result.ResultCode != 1)
            {
                string userMessage =
                    result.Message.StartsWith("Error:") ||
                    result.Message.StartsWith("SOAP Error:")
                        ? "حدث خطأ في النظام، الرجاء المحاولة لاحقًا."
                        : result.Message;

                TempData["ErrorMsg"] = userMessage;
                return RedirectToAction("Index");
            }

            // ✅ حوّل الرقم الوظيفي بأمان
            if (!int.TryParse(empId, out var empIdInt))
            {
                TempData["ErrorMsg"] = "الرقم الوظيفي غير صحيح.";
                return RedirectToAction("Index");
            }

            // ✅ تحقق من السماحية قبل أي Session
            var allowed = await _allowedUsers.IsAllowedAsync(empIdInt, ct);
            if (!allowed)
            {
                // احتياط: امسح أي شي قديم
                HttpContext.Session.Clear();

                TempData["ErrorMsg"] = "غير مصرح لك بالدخول. راجع قسم الإجازات والدوام.";
                return RedirectToAction("Index");
            }
            
// ✅ خزّن IsAdmin               
            var isAdmin = await _allowedUsers.IsAdminAsync(empIdInt, ct);
            HttpContext.Session.SetString("IsAdmin", isAdmin ? "1" : "0");


            // ✅ الآن فقط خزّن Session
            HttpContext.Session.SetString("SessionKey", result.SessionKey);
            HttpContext.Session.SetString("EmpName", result.EmployeeName);
            HttpContext.Session.SetString("EmpId", empId);
            // TempData["SuccessMsg"] = $"Welcome {result.EmployeeName}";

            return RedirectToAction("Index", "Dashboard");

        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            Response.Cookies.Delete(".AspNetCore.Session", new CookieOptions
            {
                Path = "/",
                Secure = HttpContext.Request.IsHttps,
                HttpOnly = true,
                SameSite = SameSiteMode.Strict
            });

            Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            Response.Headers.Pragma = "no-cache";
            Response.Headers.Expires = "0";
            TempData["SuccessMsg"] = "تم تسجيل الخروج بنجاح";
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
