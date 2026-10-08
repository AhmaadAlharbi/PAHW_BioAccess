using Terminals.Web.Contracts;
using Terminals.Web.DTOs;
using Terminals.Web.Persistence;
using Terminals.Web.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;

namespace Terminals.Web.Services.AttendanceRequests;

public class AttendanceRequestsService : IAttendanceRequestsService
{
    private const int MaxImageCount = 5;
    private const long MaxImageBytes = 5 * 1024 * 1024;
    private const long MaxTotalImageBytes = 20 * 1024 * 1024;

    private readonly LocalAppDbContext _db;
    private readonly IWebHostEnvironment _environment;

    public AttendanceRequestsService(LocalAppDbContext db, IWebHostEnvironment environment)
    {
        _db = db;
        _environment = environment;
    }

    public async Task<int> CreateAsync(CreateAttendanceRequestDto dto, CancellationToken ct)
    {
        var request = new AttendanceRequest
        {
            EmployeeId = dto.EmployeeId,
            EmployeeName = dto.EmployeeName,
            AttendanceDate = dto.AttendanceDate,
            HrNote = string.IsNullOrWhiteSpace(dto.HrNote) ? null : dto.HrNote.Trim(),
            Status = "PendingIT",
            CreatedAt = DateTime.Now,
            CreatedByEmployeeId = dto.CreatedByEmployeeId,
            CreatedByName = dto.CreatedByName,
            Items = dto.Types.Select(type => new AttendanceRequestItem
            {
                Type = type
            }).ToList()
        };

        _db.AttendanceRequests.Add(request);
        await _db.SaveChangesAsync(ct);

        return request.Id;
    }

    public async Task<List<AttendanceRequestListItemDto>> ListAsync(AttendanceRequestListFilterDto filter, CancellationToken ct)
    {
        var query = _db.AttendanceRequests.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(filter.Month) &&
            DateTime.TryParse($"{filter.Month}-01", out var monthStart))
        {
            var monthEnd = monthStart.AddMonths(1);
            query = query.Where(x => x.AttendanceDate >= monthStart && x.AttendanceDate < monthEnd);
        }

        if (!string.IsNullOrWhiteSpace(filter.EmployeeId))
        {
            if (int.TryParse(filter.EmployeeId.Trim(), out var employeeId))
            {
                query = query.Where(x => x.EmployeeId == employeeId);
            }
            else
            {
                query = query.Where(x => false);
            }
        }

        if (!string.IsNullOrWhiteSpace(filter.AttendanceDate) &&
            DateTime.TryParse(filter.AttendanceDate, out var attendanceDate))
        {
            query = query.Where(x => x.AttendanceDate.Date == attendanceDate.Date);
        }

        if (filter.Status is "PendingIT" or "Answered")
        {
            query = query.Where(x => x.Status == filter.Status);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new AttendanceRequestListItemDto(
                x.Id,
                x.EmployeeId,
                x.EmployeeName,
                x.AttendanceDate,
                x.CreatedByEmployeeId,
                x.CreatedByName,
                x.Status,
                x.AnsweredByName,
                x.CreatedAt
            ))
            .ToListAsync(ct);
    }

    public async Task<AttendanceRequestDetailsDto?> FindDetailsAsync(int id, CancellationToken ct)
    {
        return await _db.AttendanceRequests
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new AttendanceRequestDetailsDto
            {
                Id = x.Id,
                EmployeeId = x.EmployeeId,
                EmployeeName = x.EmployeeName,
                AttendanceDate = x.AttendanceDate,
                CreatedByEmployeeId = x.CreatedByEmployeeId,
                CreatedByName = x.CreatedByName,
                HrNote = x.HrNote,
                ITNote = x.ITNote,
                AnsweredByEmployeeId = x.AnsweredByEmployeeId,
                AnsweredByName = x.AnsweredByName,
                AnsweredAt = x.AnsweredAt,
                Status = x.Status,
                CreatedAt = x.CreatedAt,
                Items = x.Items
                    .OrderBy(item => item.Id)
                    .Select(item => new AttendanceRequestItemDetailsDto(
                        item.Id,
                        item.Type,
                        item.Reply
                    ))
                    .ToList(),
                Attachments = x.Attachments
                    .OrderBy(attachment => attachment.Id)
                    .Select(attachment => new AttendanceRequestAttachmentDetailsDto(
                        attachment.Id,
                        attachment.FileName,
                        attachment.ContentType,
                        attachment.UploadedAt
                    ))
                    .ToList()
            })
            .FirstOrDefaultAsync(ct);
    }

    public async Task<AttendanceRequestReplyValidationResultDto> SubmitReplyAsync(SubmitAttendanceRequestReplyDto dto, CancellationToken ct)
    {
        var request = await _db.AttendanceRequests
            .Include(x => x.Items)
            .Include(x => x.Attachments)
            .Where(x => x.Id == dto.RequestId)
            .FirstOrDefaultAsync(ct);

        var result = ValidateReply(request, dto);
        ValidateImages(dto.Images, result);
        if (!result.IsValid || request == null)
        {
            return result;
        }

        var createdFiles = new List<string>();
        try
        {
            var storageFolder = GetAttachmentStorageFolder();
            Directory.CreateDirectory(storageFolder);

            foreach (var upload in dto.Images.Where(file => file.Length > 0))
            {
                var imageType = DetectImageType(upload);
                if (imageType == null)
                {
                    throw new InvalidOperationException("IMAGE_VALIDATION_STATE_INVALID");
                }

                var storedFileName = $"{Guid.NewGuid():N}{imageType.Extension}";
                var storedPath = Path.Combine(storageFolder, storedFileName);

                await using (var source = upload.OpenReadStream())
                await using (var target = new FileStream(storedPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    await source.CopyToAsync(target, ct);
                }

                createdFiles.Add(storedPath);
                request.Attachments.Add(new AttendanceRequestAttachment
                {
                    FileName = ToSafeDisplayFileName(upload.FileName),
                    StoredFileName = storedFileName,
                    ContentType = imageType.ContentType,
                    UploadedAt = DateTime.Now
                });
            }

            foreach (var item in request.Items)
            {
                item.Reply = dto.Replies[item.Id]?.Trim();
            }

            request.ITNote = string.IsNullOrWhiteSpace(dto.ITNote) ? null : dto.ITNote.Trim();
            request.Status = "Answered";
            request.AnsweredByEmployeeId = dto.AnsweredByEmployeeId;
            request.AnsweredByName = dto.AnsweredByName;
            request.AnsweredAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            foreach (var path in createdFiles)
            {
                TryDeleteFile(path);
            }

            throw;
        }

        return result;
    }

    public async Task<AttendanceRequestAttachmentFileDto?> FindAttachmentFileAsync(int attachmentId, CancellationToken ct)
    {
        var attachment = await _db.AttendanceRequestAttachments
            .AsNoTracking()
            .Where(x => x.Id == attachmentId)
            .Select(x => new
            {
                x.FileName,
                x.StoredFileName,
                x.ContentType
            })
            .FirstOrDefaultAsync(ct);

        if (attachment == null)
        {
            return null;
        }

        var filePath = Path.Combine(GetAttachmentStorageFolder(), attachment.StoredFileName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        return new AttendanceRequestAttachmentFileDto
        {
            FilePath = filePath,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType
        };
    }

    private static AttendanceRequestReplyValidationResultDto ValidateReply(AttendanceRequest? request, ValidateAttendanceRequestReplyDto dto)
    {
        var result = new AttendanceRequestReplyValidationResultDto
        {
            ITNoteInput = dto.ITNote,
            ReplyInputs = dto.Replies.ToDictionary(x => x.Key, x => x.Value)
        };

        if (request == null)
        {
            result.RequestExists = false;
            result.ValidationMessages.Add("لم يتم العثور على الطلب.");
            return result;
        }

        result.RequestExists = true;

        if (request.Status != "PendingIT")
        {
            result.ValidationMessages.Add("لا يمكن إرسال الرد إلا للطلبات التي بانتظار رد تقنية المعلومات.");
        }

        if (!request.Items.Any())
        {
            result.ValidationMessages.Add("لا توجد استفسارات مرتبطة بهذا الطلب.");
        }

        var selectedItemIds = request.Items.Select(item => item.Id).ToHashSet();
        foreach (var postedItemId in dto.Replies.Keys.Where(itemId => !selectedItemIds.Contains(itemId)))
        {
            result.ReplyErrors[postedItemId] = "لا يمكن إرسال رد لاستفسار غير مرتبط بهذا الطلب.";
        }

        foreach (var item in request.Items.OrderBy(item => item.Id))
        {
            if (!dto.Replies.TryGetValue(item.Id, out var reply) || string.IsNullOrWhiteSpace(reply))
            {
                result.ReplyErrors[item.Id] = "يرجى إدخال رد تقنية المعلومات لهذا الاستفسار.";
            }
        }

        return result;
    }

    private static void ValidateImages(List<IFormFile> files, AttendanceRequestReplyValidationResultDto result)
    {
        var uploads = files.Where(file => file.Length > 0).ToList();
        if (uploads.Count > MaxImageCount)
        {
            result.ValidationMessages.Add($"يمكن إرفاق {MaxImageCount} صور كحد أقصى.");
            return;
        }

        var totalBytes = uploads.Sum(file => file.Length);
        if (totalBytes > MaxTotalImageBytes)
        {
            result.ValidationMessages.Add("إجمالي حجم الصور يجب ألا يتجاوز 20 ميجابايت.");
        }

        foreach (var file in uploads)
        {
            if (file.Length > MaxImageBytes)
            {
                result.ValidationMessages.Add("حجم كل صورة يجب ألا يتجاوز 5 ميجابايت.");
            }

            var detectedType = DetectImageType(file);
            if (detectedType == null)
            {
                result.ValidationMessages.Add("يجب إرفاق ملفات صور صحيحة فقط.");
                continue;
            }

            if (!IsAllowedDeclaredContentType(file.ContentType, detectedType.ContentType))
            {
                result.ValidationMessages.Add("نوع ملف الصورة غير مسموح.");
            }
        }
    }

    private static bool IsAllowedDeclaredContentType(string? declaredContentType, string detectedContentType)
    {
        if (string.IsNullOrWhiteSpace(declaredContentType))
        {
            return false;
        }

        if (declaredContentType.Equals(detectedContentType, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return detectedContentType == "image/jpeg" &&
               declaredContentType.Equals("image/pjpeg", StringComparison.OrdinalIgnoreCase);
    }

    private static ImageUploadType? DetectImageType(IFormFile file)
    {
        Span<byte> header = stackalloc byte[12];
        using var stream = file.OpenReadStream();
        var read = stream.Read(header);

        if (read >= 8 &&
            header[0] == 0x89 &&
            header[1] == 0x50 &&
            header[2] == 0x4E &&
            header[3] == 0x47 &&
            header[4] == 0x0D &&
            header[5] == 0x0A &&
            header[6] == 0x1A &&
            header[7] == 0x0A)
        {
            return new ImageUploadType(".png", "image/png");
        }

        if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return new ImageUploadType(".jpg", "image/jpeg");
        }

        if (read >= 6 &&
            header[0] == 0x47 &&
            header[1] == 0x49 &&
            header[2] == 0x46 &&
            header[3] == 0x38 &&
            (header[4] == 0x37 || header[4] == 0x39) &&
            header[5] == 0x61)
        {
            return new ImageUploadType(".gif", "image/gif");
        }

        if (read >= 12 &&
            header[0] == 0x52 &&
            header[1] == 0x49 &&
            header[2] == 0x46 &&
            header[3] == 0x46 &&
            header[8] == 0x57 &&
            header[9] == 0x45 &&
            header[10] == 0x42 &&
            header[11] == 0x50)
        {
            return new ImageUploadType(".webp", "image/webp");
        }

        return null;
    }

    private string GetAttachmentStorageFolder()
        => Path.Combine(_environment.ContentRootPath, "App_Data", "AttendanceRequestAttachments");

    private static string ToSafeDisplayFileName(string fileName)
    {
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            return "صورة مرفقة";
        }

        return safeName.Length > 255 ? safeName[..255] : safeName;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup after a failed submit.
        }
    }

    private sealed record ImageUploadType(string Extension, string ContentType);
}
