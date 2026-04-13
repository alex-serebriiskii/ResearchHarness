using System.Text.Json;
using System.Text.Json.Serialization;

namespace ResearchHarness.Cli.Rendering;

public static class CliJsonOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
