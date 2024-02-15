using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

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

	public bool UseJbm { get; init; } = false;
	public UseDict UseDict { get; init; } = UseDict.Simple;
	public UseFloats UseFloats { get; init; } = UseFloats.Single | UseFloats.Double;


	public bool Compress { get; init; } = false;

	public bool UseAos { get; init; } = false;
	public int AosMinArraySize { get; init; } = 32;
	public int AosMinSparseFraction { get; init; } = 2;

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
	Simple
}

internal enum EncodeFlags : byte
{
	None = 0,
	Jbm = 1 << 0,
	Compressed = 1 << 1,
	Aos = 1 << 2,
}