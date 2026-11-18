using WoodFlow.Application.DTOs;

namespace WoodFlow.Application.Interfaces;

public interface ISetupService
{
    Task<string?> GenerateCodeAsync(CancellationToken cancellationToken);
    Task<bool> InitializeOwnerAsync(InitializeOwnerRequest request, CancellationToken cancellationToken);
}
