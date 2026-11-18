using WoodFlow.Domain;

namespace WoodFlow.Application.Interfaces;

public interface IPasswordService
{
    string Hash(User user, string password);
    bool IsUsableHash(string hash);
    bool Verify(User user, string password, out bool needsRehash);
}
