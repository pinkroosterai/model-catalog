using System.Collections.ObjectModel;

namespace Clarive.ModelRegistry.Service.Auth;

public sealed class ApiKeyOptions
{
#pragma warning disable MA0016
    public Collection<ApiKeyEntry> ApiKeys { get; } = [];
#pragma warning restore MA0016
}
