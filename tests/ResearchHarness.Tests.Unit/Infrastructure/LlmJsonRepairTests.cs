using AwesomeAssertions;
using System.Text.Json;
using System.Text.Json.Nodes;
using ResearchHarness.Infrastructure.Llm;

namespace ResearchHarness.Tests.Unit.Infrastructure;

public class LlmJsonRepairTests
{
    private static string StringField(string key, string value)
    {
        var obj = new JsonObject { [key] = JsonValue.Create(value) };
        return obj.ToJsonString();
    }

    [Test]
    public async Task StringifiedArray_IsRepaired()
    {
        var arrayJson = new JsonArray(new JsonObject { ["sub_topic"] = "AI" }).ToJsonString();
        var input = StringField("findings", arrayJson);

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        await Task.CompletedTask;
    }

    [Test]
    public async Task StringifiedObject_IsRepaired()
    {
        var objJson = new JsonObject { ["key"] = "value" }.ToJsonString();
        var input = StringField("metadata", objJson);

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("metadata").ValueKind.Should().Be(JsonValueKind.Object);
        await Task.CompletedTask;
    }

    [Test]
    public async Task AlreadyCorrectArray_IsUnchanged()
    {
        var obj = new JsonObject
        {
            ["findings"] = new JsonArray(new JsonObject { ["sub_topic"] = "AI" })
        };
        var input = obj.ToJsonString();

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        await Task.CompletedTask;
    }

    [Test]
    public async Task PlainStringValue_IsUnchanged()
    {
        var input = StringField("summary", "This is a plain string.");

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("summary").GetString().Should().Be("This is a plain string.");
        await Task.CompletedTask;
    }

    [Test]
    public async Task EmptyString_ReturnsEmpty()
    {
        LlmJsonRepair.RepairStringifiedJsonFields(string.Empty).Should().Be(string.Empty);
        await Task.CompletedTask;
    }

    [Test]
    public async Task InvalidJson_ReturnsAsIs()
    {
        const string input = "not json at all";
        LlmJsonRepair.RepairStringifiedJsonFields(input).Should().Be(input);
        await Task.CompletedTask;
    }

    [Test]
    public async Task MultipleStringifiedFields_AllRepaired()
    {
        var arrayJson = new JsonArray(new JsonObject { ["url"] = "http://x.com" }).ToJsonString();
        var obj = new JsonObject
        {
            ["findings"] = JsonValue.Create(arrayJson),
            ["sources"] = JsonValue.Create(arrayJson)
        };
        var input = obj.ToJsonString();

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetProperty("sources").ValueKind.Should().Be(JsonValueKind.Array);
        await Task.CompletedTask;
    }

    [Test]
    public async Task NonObjectRoot_ReturnsAsIs()
    {
        const string input = "[1, 2, 3]";
        LlmJsonRepair.RepairStringifiedJsonFields(input).Should().Be(input);
        await Task.CompletedTask;
    }

    [Test]
    public async Task NestedStringifiedArray_IsRepaired()
    {
        var inner = new JsonArray(new JsonObject { ["sub_topic"] = "AI" }).ToJsonString();
        var obj = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["findings"] = JsonValue.Create(inner)
            }
        };
        var input = obj.ToJsonString();

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("metadata").GetProperty("findings").ValueKind.Should().Be(JsonValueKind.Array);
        await Task.CompletedTask;
    }

    [Test]
    public async Task DeeplyNestedStringifiedField_IsRepaired()
    {
        var innerJson = new JsonObject { ["key"] = "val" }.ToJsonString();
        var obj = new JsonObject
        {
            ["level1"] = new JsonObject
            {
                ["level2"] = new JsonObject
                {
                    ["data"] = JsonValue.Create(innerJson)
                }
            }
        };
        var input = obj.ToJsonString();

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("level1").GetProperty("level2").GetProperty("data").ValueKind.Should().Be(JsonValueKind.Object);
        await Task.CompletedTask;
    }

    [Test]
    public async Task StringifiedFieldInsideArray_IsRepaired()
    {
        var sourcesJson = new JsonArray(new JsonObject { ["url"] = "http://x.com" }).ToJsonString();
        var obj = new JsonObject
        {
            ["items"] = new JsonArray(
                new JsonObject
                {
                    ["sources"] = JsonValue.Create(sourcesJson)
                })
        };
        var input = obj.ToJsonString();

        var repaired = LlmJsonRepair.RepairStringifiedJsonFields(input);

        using var doc = JsonDocument.Parse(repaired);
        doc.RootElement.GetProperty("items")[0].GetProperty("sources").ValueKind.Should().Be(JsonValueKind.Array);
        await Task.CompletedTask;
    }
}
