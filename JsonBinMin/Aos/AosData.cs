using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace JsonBinMin.Aos;

public sealed class AosData<TArr>
{
	public Dictionary<string, Dictionary<string, AosDef>> Aos { get; set; } = [];
	public JsonObject Data { get; set; }

	public record struct AosDef([property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] JsonValueKind N, TArr A);
}