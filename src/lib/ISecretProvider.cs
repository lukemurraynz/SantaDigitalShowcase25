using System.Threading.Tasks;

namespace Drasicrhsit.Infrastructure;

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string name, CancellationToken ct = default);
}