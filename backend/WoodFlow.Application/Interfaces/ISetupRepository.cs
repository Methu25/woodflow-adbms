namespace WoodFlow.Application.Interfaces;

public interface ISetupRepository
{
    // Both operations serialize against one persistent installation state row.
    Task<bool> IssueCodeAsync(byte[] codeHash, CancellationToken cancellationToken);
    Task<bool> InitializeOwnerAsync(string fullName, string username, string passwordHash,
        byte[] codeHash, CancellationToken cancellationToken);
}
