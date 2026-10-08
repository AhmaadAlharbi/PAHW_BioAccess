using Microsoft.AspNetCore.Mvc;
using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.External;

public class AttendanceRequestsController : Controller
{
    private readonly EmployeeSoapClient _employeeSoap;
    private readonly IAttendanceRequestsService _attendanceRequests;
    private readonly ILogger<AttendanceRequestsController> _logger;

    public AttendanceRequestsController(
        EmployeeSoapClient employeeSoap,
        IAttendanceRequestsService attendanceRequests,
        ILogger<AttendanceRequestsController> logger)
    {
        _employeeSoap = employeeSoap;
        _attendanceRequests = attendanceRequests;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? month, string? employeeId, string? attendanceDate, string? status, CancellationToken ct)
    {
        var filter = new AttendanceRequestListFilterDto
        {
            Month = month,
            EmployeeId = employeeId,
            AttendanceDate = attendanceDate,
            Status = status
        };

        var requests = await _attendanceRequests.ListAsync(filter, ct);

        return View(new AttendanceRequestsIndexViewModel
        {
            Filter = filter,
            Requests = requests
        });
    }

    [HttpGet]
    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var request = await _attendanceRequests.FindDetailsAsync(id, ct);
        if (request == null)
        {
            return NotFound();
        }

        return View(request);
    }

    [HttpGet]
    public IActionResult Create()
    {
        if (!CanCreateRequest())
            return BlockCreateAccess();

        return View(new AttendanceRequestCreateViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LookupEmployee(int employeeId, CancellationToken ct)
    {
        if (!CanCreateRequest())
            return BlockCreateAccess();

        if (employeeId <= 0)
        {
            TempData["ErrorMsg"] = "يرجى إدخال رقم وظيفي صحيح.";
            return View("Create", new AttendanceRequestCreateViewModel());
        }

        try
        {
            var rawXml = await _employeeSoap.GetEmployeeByIdRawAsync(employeeId, ct);
            var (name, department, jobTitle) = _employeeSoap.ParseEmployeeSummary(rawXml);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(department) && string.IsNullOrWhiteSpace(jobTitle))
            {
                TempData["ErrorMsg"] = "لم يتم العثور على الموظف، تأكد من الرقم الوظيفي.";
                return View("Create", new AttendanceRequestCreateViewModel { EmployeeId = employeeId });
            }

            return View("Create", new AttendanceRequestCreateViewModel
            {
                EmployeeId = employeeId,
                EmployeeName = name ?? "",
                Department = department ?? "",
                JobTitle = jobTitle ?? "",
                LookupSucceeded = true
            });
        }
        catch
        {
            TempData["ErrorMsg"] = "تعذر جلب بيانات الموظف، حاول مرة أخرى لاحقًا.";
            return View("Create", new AttendanceRequestCreateViewModel { EmployeeId = employeeId });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ValidateCreate(int employeeId, string requestedDate, string[] types, string? hrNote, CancellationToken ct)
    {
        if (!CanCreateRequest())
            return BlockCreateAccess();

        var model = new AttendanceRequestCreateViewModel
        {
            EmployeeId = employeeId,
            RequestedDate = requestedDate ?? "",
            HrNote = hrNote ?? "",
            SelectedTypes = types?.ToList() ?? new List<string>()
        };

        var employeeFound = await FillEmployeeFromSoapAsync(model, employeeId, ct);
        if (!employeeFound)
        {
            model.ValidationMessages.Add("يجب جلب بيانات موظف صحيح من النظام قبل متابعة الطلب.");
        }

        DateTime attendanceDate = default;
        if (string.IsNullOrWhiteSpace(requestedDate))
        {
            model.ValidationMessages.Add("تاريخ الحضور المطلوب حقل إلزامي.");
        }
        else if (!DateTime.TryParse(requestedDate, out attendanceDate))
        {
            model.ValidationMessages.Add("تاريخ الحضور المطلوب غير صحيح.");
        }

        var validTypes = new[] { "mobileIn", "fingerprintIn", "mobileOut", "fingerprintOut" };
        model.SelectedTypes = model.SelectedTypes
            .Where(x => validTypes.Contains(x))
            .Distinct()
            .ToList();

        if (!model.SelectedTypes.Any())
        {
            model.ValidationMessages.Add("يرجى اختيار إجراء واحد على الأقل للاستفسار.");
        }

        if (model.ValidationMessages.Any())
        {
            return View("Create", model);
        }

        if (!int.TryParse(HttpContext.Session.GetString("EmpId"), out var currentEmployeeId))
        {
            model.ValidationMessages.Add("حدث خطأ في الجلسة، يرجى تسجيل الدخول مرة أخرى.");
            return View("Create", model);
        }

        try
        {
            await _attendanceRequests.CreateAsync(new CreateAttendanceRequestDto
            {
                EmployeeId = employeeId,
                EmployeeName = model.EmployeeName,
                AttendanceDate = attendanceDate,
                HrNote = hrNote,
                CreatedByEmployeeId = currentEmployeeId,
                CreatedByName = HttpContext.Session.GetString("EmpName") ?? "",
                Types = model.SelectedTypes
            }, ct);

            TempData["SuccessMsg"] = "تم إرسال الطلب إلى تقنية المعلومات بنجاح.";
            return RedirectToAction("Index");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create attendance request for employeeId={EmployeeId}", employeeId);
            model.ValidationMessages.Add("تعذر إرسال الطلب، حاول مرة أخرى لاحقًا.");
            return View("Create", model);
        }
    }

    private bool CanCreateRequest()
    {
        var isAdmin = HttpContext.Session.GetString("IsAdmin") == "1";
        var userType = HttpContext.Session.GetString("UserType") ?? "Attendance";

        return isAdmin || userType == "Attendance";
    }

    private IActionResult BlockCreateAccess()
    {
        TempData["ErrorMsg"] = "غير مصرح لك بإنشاء طلب جديد.";
        return RedirectToAction("Index");
    }

    private async Task<bool> FillEmployeeFromSoapAsync(AttendanceRequestCreateViewModel model, int employeeId, CancellationToken ct)
    {
        if (employeeId <= 0)
            return false;

        try
        {
            var rawXml = await _employeeSoap.GetEmployeeByIdRawAsync(employeeId, ct);
            var (name, department, jobTitle) = _employeeSoap.ParseEmployeeSummary(rawXml);

            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(department) && string.IsNullOrWhiteSpace(jobTitle))
                return false;

            model.EmployeeId = employeeId;
            model.EmployeeName = name ?? "";
            model.Department = department ?? "";
            model.JobTitle = jobTitle ?? "";
            model.LookupSucceeded = true;
            return true;
        }
        catch
        {
            return false;
        }
    }
}
