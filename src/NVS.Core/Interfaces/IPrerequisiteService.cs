using NVS.Core.Enums;
using NVS.Core.Models;

namespace NVS.Core.Interfaces;

public interface IPrerequisiteService
{
    Task<IReadOnlyList<PrerequisiteInfo>> CheckPrerequisitesAsync(
        IEnumerable<Language> languages,
        CancellationToken cancellationToken = default);
}
