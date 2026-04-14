namespace Clarive.ModelRegistry.Client.Dtos;

public sealed record ModelQuery(
    string? Provider = null,
    Modality? Modality = null,
    bool? IsReasoning = null,
    bool? SupportsFunctionCalling = null);
