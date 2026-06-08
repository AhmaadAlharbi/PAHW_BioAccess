using System;
using System.Collections.Generic;
using System.Text;

namespace BioAccess.Web.Contracts
{
    public interface IRegionService
    {
        Task<IReadOnlyList<RegionDto>> GetRegionsAsync(CancellationToken ct = default);
        Task CreateRegionAsync(string name, CancellationToken ct = default);
        Task RenameRegionAsync(int regionId, string newName, CancellationToken ct = default);
        Task DeleteRegionAsync(int regionId, CancellationToken ct = default);

        Task AssignTerminalToRegionAsync(int terminalId, int regionId, CancellationToken ct = default);
        Task<int?> GetTerminalRegionAsync(int terminalId, CancellationToken ct = default);
        Task<IReadOnlyList<TerminalRegionDto>> GetRegionTerminalsAsync(int regionId, CancellationToken ct = default);
    }

    public record RegionDto(int Id, string Name);
    public record TerminalRegionDto(int TerminalId, int RegionId);

}
