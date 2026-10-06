using Microsoft.AspNetCore.Mvc;

public class AttendanceRequestsController : Controller
{
    [HttpGet]
    public IActionResult Index()
    {
        return View();
    }
}
