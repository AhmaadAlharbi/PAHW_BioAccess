using BioAccess.Web.Contracts;
using BioAccess.Web.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using BioAccess.Web.Services.Activity;

public class AllowedUsersController : Controller
{
    private readonly IAllowedUsersAdmin _admin;
    private readonly IActivityLogService _activity;

    public AllowedUsersController(IAllowedUsersAdmin admin, IActivityLogService activity)
    {
        _admin = admin;
        _activity = activity;
    }
    
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";

        if (!isAdmin)
        {
            context.Result = Forbid();
            return;
        }

        base.OnActionExecuting(context);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Fetch(int employeeId, CancellationToken ct)
    {
        var dto = await _admin.FetchFromSoapAsync(employeeId, ct);
        if (dto == null)
        {
            TempData["ErrorMsg"] = "لم يتم العثور على الموظف في SOAP.";
            return RedirectToAction("Create");
        }

        return View("Create", dto);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(int employeeId, string fullName, string email, string department,
        DateTime? validUntil, bool isAdmin, CancellationToken ct)
    {
        var ok = await _admin.AddAsync(new AllowedUserDto(employeeId, fullName, email, department), validUntil, isAdmin, ct);

        TempData["SuccessMsg"] = ok ? "تمت الإضافة." : "الموظف موجود مسبقًا.";

        if (ok)
        {
            await _activity.LogAsync(
                action: "AllowedUser.Added",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: $"تمت إضافة مستخدم إلى قائمة الصلاحيات: {employeeId} ({fullName}).",
                details: new { employeeId, fullName, isAdmin, validUntil },
                ct: ct
            );
        }

        return RedirectToAction("Create");
    }
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var list = await _admin.ListAsync(ct);
        return View(list);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Toggle(int employeeId, bool makeActive, CancellationToken ct)
    {
        if (!int.TryParse(HttpContext.Session.GetString("EmpId"), out var currentEmpId))
        {
            TempData["ErrorMsg"] = "حدث خطأ في الجلسة.";
            return RedirectToAction("Index");
        }

        // ✅ لا تعطّل نفسك
        if (!makeActive && employeeId == currentEmpId)
        {
            TempData["ErrorMsg"] = "لا يمكنك تعطيل نفسك.";
            return RedirectToAction("Index");
        }

        var ok = await _admin.SetActiveAsync(employeeId, makeActive, ct);

        if (!ok)
            TempData["ErrorMsg"] = "لا يمكن تنفيذ العملية (قد يكون آخر مشرف).";
        else
            TempData["SuccessMsg"] = "تم تحديث الحالة بنجاح.";

        if (ok)
        {
            await _activity.LogAsync(
                action: makeActive ? "AllowedUser.Activated" : "AllowedUser.Deactivated",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: makeActive
                    ? $"تم تفعيل المستخدم في قائمة الصلاحيات: {employeeId}."
                    : $"تم تعطيل المستخدم في قائمة الصلاحيات: {employeeId}.",
                details: new { employeeId, makeActive },
                ct: ct
            );
        }

        return RedirectToAction("Index");
    }


    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int employeeId, CancellationToken ct)
    {
        if (!int.TryParse(HttpContext.Session.GetString("EmpId"), out var currentEmpId))
        {
            TempData["ErrorMsg"] = "حدث خطأ في الجلسة.";
            return RedirectToAction("Index");
        }

        // ✅ لا تسمح لنفسك تحذف نفسك
        if (employeeId == currentEmpId)
        {
            TempData["ErrorMsg"] = "لا يمكنك حذف نفسك.";
            return RedirectToAction("Index");
        }

        var ok = await _admin.DeleteAsync(employeeId, ct);

        TempData[ok ? "SuccessMsg" : "ErrorMsg"] = ok
            ? "تم حذف المستخدم."
            : "لا يمكن حذف هذا المستخدم (قد يكون آخر مشرف).";

        if (ok)
        {
            await _activity.LogAsync(
                action: "AllowedUser.Deleted",
                entityType: "AllowedUser",
                entityId: employeeId.ToString(),
                summary: $"تم حذف المستخدم من قائمة الصلاحيات: {employeeId}.",
                details: new { employeeId },
                ct: ct
            );
        }

        return RedirectToAction("Index");
    }



}
