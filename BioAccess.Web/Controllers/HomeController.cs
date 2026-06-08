using BioAccess.Web.Contracts;
using BioAccess.Web.Models;
using BioAccess.Web.Services.Activity;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace BioAccess.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILoginApi _login;
        private readonly IAllowedUsersStore _allowedUsers;
        private readonly IActivityLogService _activityLog;

        public HomeController(ILoginApi login, IAllowedUsersStore allowedUsers, IActivityLogService activityLog)
        {
            _login = login;
            _allowedUsers = allowedUsers;
            _activityLog = activityLog;
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

            // فشل
            if (result.ResultCode != 1)
            {
                TempData["ErrorMsg"] = result.Message;
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
            await _activityLog.LogAsync(
                "Auth.LoginSuccess",
                "Authentication",
                empId,
                $"تم تسجيل دخول المستخدم {result.EmployeeName} ({empId}).");
            // TempData["SuccessMsg"] = $"Welcome {result.EmployeeName}";

            return Redirect("/dashboard");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await _activityLog.LogAsync(
                "Auth.Logout",
                "Authentication",
                HttpContext.Session.GetString("EmpId"),
                $"تم تسجيل خروج المستخدم {HttpContext.Session.GetString("EmpName") ?? HttpContext.Session.GetString("EmpId") ?? "unknown"}.");
            HttpContext.Session.Clear();
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
