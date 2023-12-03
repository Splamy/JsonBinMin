using System;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.Encodings.Web;

namespace JsonBinMin;

public class JBMOptions
{
	private static readonly JsonSerializerOptions DefaultJsonSerializerOptions = new()
	{
		AllowTrailingCommas = true,
		NumberHandling = JsonNumberHandling.Strict,
		PropertyNameCaseInsensitive = false,
		ReadCommentHandling = JsonCommentHandling.Skip,
		Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};

	public static readonly JBMOptions Default = new();

	// <1.0000> == <1.0>
	//public bool AllowSemanticallyEquivalentOpt { get; init; } = false;

	// <1.0> == <1>
	//public bool AllowReduceToIntegerOpt { get; init; } = false;

	public UseDict UseDict { get; init; } = UseDict.Simple;

	public UseFloats UseFloats { get; init; } = UseFloats.Single | UseFloats.Double;

	public bool Compress { get; init; } = false;

	public JsonSerializerOptions JsonSerializerOptions { get; init; } = DefaultJsonSerializerOptions;
}

[Flags]
public enum UseFloats
{
	None = 0,
	All = Half | Single | Double,
	Half = 1 << 0,
	Single = 1 << 1,
	Double = 1 << 2,
}

public enum UseDict
{
	Off,
	Simple,
	Deep,
}