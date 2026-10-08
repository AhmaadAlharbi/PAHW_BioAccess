using Microsoft.AspNetCore.Http;

namespace Terminals.Web.DTOs;

public class ValidateAttendanceRequestReplyDto
{
    public int RequestId { get; set; }
    public Dictionary<int, string?> Replies { get; set; } = new();
    public string? ITNote { get; set; }
}

public class SubmitAttendanceRequestReplyDto : ValidateAttendanceRequestReplyDto
{
    public int AnsweredByEmployeeId { get; set; }
    public string AnsweredByName { get; set; } = "";
    public List<IFormFile> Images { get; set; } = new();
}

public class AttendanceRequestReplyValidationResultDto
{
    public bool RequestExists { get; set; }
    public bool IsValid => RequestExists && !ValidationMessages.Any() && !ReplyErrors.Any();
    public Dictionary<int, string?> ReplyInputs { get; set; } = new();
    public Dictionary<int, string> ReplyErrors { get; set; } = new();
    public string? ITNoteInput { get; set; }
    public List<string> ValidationMessages { get; set; } = new();
}

public class AttendanceRequestAttachmentFileDto
{
    public string FilePath { get; set; } = "";
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
}
