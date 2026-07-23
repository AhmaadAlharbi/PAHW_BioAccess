using Terminals.Web.DTOs;

namespace Terminals.Web.Contracts;

public interface ILoginApi
{
    Task<LoginResponseDto> LoginAsync(string empId, string password, CancellationToken ct = default);
}
