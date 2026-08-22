namespace ModelCatalog.Service.Observability;

public sealed class JsonLogOptions
{
    /// <summary>The stack token, which is what architecture-guideline.md §7 wants in this field.</summary>
    public string Service { get; set; } = "modelcatalog";
}
