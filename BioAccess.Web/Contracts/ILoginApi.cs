using BioAccess.Web.DTOs;

namespace BioAccess.Web.Contracts;

public interface ILoginApi
{
    Task<LoginResponseDto> LoginAsync(string empId, string password, CancellationToken ct = default);
}
