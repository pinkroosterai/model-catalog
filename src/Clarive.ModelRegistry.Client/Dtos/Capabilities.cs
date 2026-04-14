namespace Clarive.ModelRegistry.Client.Dtos;

public sealed record Capabilities(
    bool? IsReasoning,
    bool? SupportsFunctionCalling,
    bool? SupportsResponseSchema,
    bool? SupportsVision,
    bool? SupportsAudioInput);
