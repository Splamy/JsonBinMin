// See https://aka.ms/new-console-template for more information
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin;
using ZstdNet;

var csv = new StringBuilder();

var opt = new JsonSerializerOptions { WriteIndented = false };

var file1 = File.ReadAllBytes(@"E:\maps\1E394\ExpertPlusStandard.dat");


//var aosNode = AosEncoder.EncodeAosObject(json);
//File.WriteAllText("split.json", aosNode.ToJsonString());
//var aosNodeRt = AosEncoder.DecodeAosObject(aosNode);
//AssertUtil.AssertStructuralEqual(json.ToJsonString(), aosNodeRt.ToJsonString());

using var compressor = new Compressor(new CompressionOptions(CompressionOptions.MaxCompressionLevel));

//var json = JsonSerializer.Deserialize<JsonNode>(file1);

//var basic = JsonSerializer.SerializeToUtf8Bytes(json, opt);
//var basicJBM = JBMConverter.Encode(basic);
//var basicBrotli = CompressBrotli(basic);
//var basicJBMbrotli = CompressBrotli(basicJBM);
//var basicZstd = compressor.Wrap(basic);
//var basicJBMZstd = compressor.Wrap(basicJBM);

//var aos = AosEncoder.EncodeAosObject(json);

//var split = JsonSerializer.SerializeToUtf8Bytes(aos, opt);
//var splitJBM = JBMConverter.Encode(split);
//var splitBrotli = CompressBrotli(split);
//var splitJBMbrotli = CompressBrotli(splitJBM);
//var splitZstd = compressor.Wrap(split);
//var splitJBMZstd = compressor.Wrap(splitJBM);

//Console.WriteLine("Basic           : {0}", basic.Length);
//Console.WriteLine("Basic JBM       : {0}", basicJBM.Length);
//Console.WriteLine("Basic Brotli    : {0}", basicBrotli.Length);
//Console.WriteLine("Basic JBM Brotli: {0}", basicJBMbrotli.Length);
//Console.WriteLine("Basic Zstd      : {0}", basicZstd.Length);
//Console.WriteLine("Basic JBM Zstd  : {0}", basicJBMZstd.Length);
//Console.WriteLine("Split           : {0}", split.Length);
//Console.WriteLine("Split JBM       : {0}", splitJBM.Length);
//Console.WriteLine("Split Brotli    : {0}", splitBrotli.Length);
//Console.WriteLine("Split JBM Brotli: {0}", splitJBMbrotli.Length);
//Console.WriteLine("Split Zstd      : {0}", splitZstd.Length);
//Console.WriteLine("Split JBM Zstd  : {0}", splitJBMZstd.Length);

csv.AppendLine(
	"Id," +
	"Basic," +
	"Basic JBM," +
	"Basic Brotli," +
	"Basic JBM Brotli," +
	"Basic Zstd," +
	"Basic JBM Zstd," +
	"Split," +
	"Split JBM," +
	"Split Brotli," +
	"Split JBM Brotli," +
	"Split Zstd," +
	"Split JBM Zstd");

foreach(var file in Directory.EnumerateFiles(@"E:\maps\3918e", "ExpertPlusStandard.dat", SearchOption.AllDirectories).Take(50))
{
	Console.WriteLine("Gen {0}", file);

	using var fs = File.OpenRead(file);

	var json = JsonSerializer.Deserialize<JsonNode>(fs);

	var sw = Stopwatch.StartNew();
	void Track(string text) { Console.WriteLine("{0}: {1}", text, sw.ElapsedMilliseconds); sw.Restart();  }

	var basic = JsonSerializer.SerializeToUtf8Bytes(json, opt);
	Track("Basic");
	var basicJBM = JBMConverter.Encode(basic, new JBMOptions() { UseFloats = UseFloats.None });
	Track("Basic JBM");
	var basicBrotli = CompressBrotli(basic);
	Track("Basic Brotli");
	var basicJBMbrotli = CompressBrotli(basicJBM);
	Track("Basic JBM Brotli");
	var basicZstd = compressor.Wrap(basic);
	Track("Basic Zstd");
	var basicJBMZstd = compressor.Wrap(basicJBM);
	Track("Basic JBM Zstd");

	var aos = AosEncoder.EncodeAosObject(json);

	var split = JsonSerializer.SerializeToUtf8Bytes(aos, opt);
	Track("Split");
	var splitJBM = JBMConverter.Encode(split, new JBMOptions() { UseFloats = UseFloats.None });
	Track("Split JBM");
	var splitBrotli = CompressBrotli(split);
	Track("Split Brotli");
	var splitJBMbrotli = CompressBrotli(splitJBM);
	Track("Split JBM Brotli");
	var splitZstd = compressor.Wrap(split);
	Track("Split Zstd");
	var splitJBMZstd = compressor.Wrap(splitJBM);
	Track("Split JBM Zstd");

	csv.AppendLine(CultureInfo.InvariantCulture,
		$"{new FileInfo(file).Directory.Name}," +
		$"{basic.Length}," +
		$"{basicJBM.Length}," +
		$"{basicBrotli.Length}," +
		$"{basicJBMbrotli.Length}," +
		$"{basicZstd.Length}," +
		$"{basicJBMZstd.Length}," +
		$"{split.Length}," +
		$"{splitJBM.Length}," +
		$"{splitBrotli.Length}," +
		$"{splitJBMbrotli.Length}," +
		$"{splitZstd.Length}," +
		$"{splitJBMZstd.Length}");
}

File.WriteAllText("results.csv", csv.ToString());


static byte[] CompressBrotli(byte[] input)
{
	var maxOutputSize = BrotliEncoder.GetMaxCompressedLength(input.Length);
	var output = new byte[maxOutputSize];

	try
	{
		BrotliEncoder.TryCompress(input, output, out var written, 11, 24);
		return output[..written];
	}
	catch
	{
		return [];
	}
}


public class AosEncoder
{
	public static void FindCompressibleArrays(JsonNode node, JsonObject? parent, string key)
	{
		if (node is JsonArray arr)
		{
			// Can't opmize arrays which are not on a object field
			if (parent is null)
			{
				return;
			}

			// Ignore small arrays
			if (arr.Count < 32)
			{
				return;
			}

			// Must be an array of objects
			if (arr is not [JsonObject first, ..])
			{
				return;
			}

			var aosPossible = first
				.Select(kvp => kvp.Key)
				.Where(key => arr.All(arrElem => arrElem is JsonObject jo && jo.ContainsKey(key)))
				.ToArray();

			if (aosPossible.Length > 0)
			{
				EncodeArray(parent, key, arr, aosPossible);
			}
		}
		else if (node is JsonObject obj)
		{
			foreach (var kvp in obj.ToArray())
			{
				if (kvp.Value is null)
				{
					continue;
				}

				FindCompressibleArrays(kvp.Value, obj, kvp.Key);
			}
		}
	}

	public static void EncodeArray(JsonObject parent, string key, JsonArray array, string[] optKeys)
	{
		var aos = new JsonObject();

		foreach (JsonObject noteObj in array)
		{
			foreach (var noteKvp in noteObj!)
			{
				if (!optKeys.Contains(noteKvp.Key))
				{
					continue;
				}

				if (!aos.TryGetPropertyValue(noteKvp.Key, out var arr))
				{
					aos.Add(noteKvp.Key, arr = new JsonArray());
				}

				arr.AsArray().Add(noteKvp.Value!.DeepClone());
			}

			foreach (var keyRem in optKeys)
			{
				noteObj.Remove(keyRem);
			}
		}

		if (array.All(x => x.AsObject().Count == 0))
		{
			parent.Remove(key);
		}

		parent[key + "$aos$"] = aos;
	}

	public static JsonNode EncodeAosObject(JsonNode node)
	{
		var clone = node.DeepClone();
		FindCompressibleArrays(clone, null, "");
		return clone;
	}

	public static JsonNode DecodeAosObject(JsonNode node)
	{
		JsonNode clone = node.DeepClone();
		DecodeAosObjectInternal(clone);
		return clone;
	}

	private static void DecodeAosObjectInternal(JsonNode node)
	{
		if (node is JsonArray arr)
		{
			//foreach (var elem in arr)
			//{
			//	DecodeAosObject(elem!);
			//}
		}
		else if (node is JsonObject obj)
		{
			foreach (var kvp in obj.ToArray())
			{
				if (kvp.Value is null)
				{
					continue;
				}

				if (kvp.Key.EndsWith("$aos$", StringComparison.OrdinalIgnoreCase))
				{
					var origKey = kvp.Key[..^5];

					if (!obj.TryGetPropertyValue(origKey, out var child) || child is not JsonArray arrChild)
					{
						arrChild = new JsonArray();
						obj[origKey] = arrChild;
					}

					foreach (var (aosKey, aosArrNode) in kvp.Value!.AsObject())
					{
						var aosArr = aosArrNode!.AsArray();

						while (arrChild.Count <= aosArr.Count)
						{
							arrChild.Add(new JsonObject());
						}

						for (int i = 0; i < aosArr.Count; i++)
						{
							arrChild[i]![aosKey] = aosArr[i]!.DeepClone();
						}
					}

					obj.Remove(kvp.Key);
				}
				else
				{
					DecodeAosObjectInternal(kvp.Value);
				}
			}
		}
	}
}


public static class AssertUtil
{
	public static void AssertStructuralEqual(string jsonExprected, string jsonActual)
	{
		var expected = JsonSerializer.Deserialize<JsonElement>(jsonExprected);
		var actual = JsonSerializer.Deserialize<JsonElement>(jsonActual);
		AssertStructuralEqual(expected, actual);
	}

	public static void AssertStructuralEqual(JsonElement jsonExpected, JsonElement jsonActual)
	{
		Trace.Assert(jsonExpected.ValueKind == jsonActual.ValueKind);
		switch (jsonExpected.ValueKind)
		{
		case JsonValueKind.Object:
			foreach (var (expected, actual) in jsonExpected.EnumerateObject().OrderBy(j => j.Name).Zip(jsonActual.EnumerateObject().OrderBy(j => j.Name)))
			{
				Trace.Assert(expected.Name == actual.Name);
				AssertStructuralEqual(expected.Value, actual.Value);
			}
			break;
		case JsonValueKind.Array:
			foreach (var (expected, actual) in jsonExpected.EnumerateArray().Zip(jsonActual.EnumerateArray()))
				AssertStructuralEqual(expected, actual);
			break;
		case JsonValueKind.String:
			Trace.Assert(jsonExpected.GetString() == jsonActual.GetString());
			break;
		case JsonValueKind.Number:
			Trace.Assert(jsonExpected.GetRawText() == jsonActual.GetRawText());
			break;
		case JsonValueKind.Undefined:
		case JsonValueKind.True:
		case JsonValueKind.False:
		case JsonValueKind.Null:
			return;
		case var unhandled:
			throw new MissingMemberException("Missing case:" + unhandled.ToString());
		}
	}
}