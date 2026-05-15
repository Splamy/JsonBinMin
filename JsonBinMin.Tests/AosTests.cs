using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin.Aos;

namespace JsonBinMin.Tests;

[TestClass]
public class AosTests
{
	public static IEnumerable<object?[]> TestFiles()
	{
		yield return ["simple_01.json", null];
		yield return ["simple_02.json", null];
		yield return ["simple_03.json", null];
		yield return ["simple_04.json", null];
		yield return ["opts_01.json", null];
		yield return ["nums_01.json", null];
		yield return ["nums_02.json", null];
		yield return ["nums_03.json", null];
		yield return ["big_01.json", null];
		//yield return new object?[] { "big_02.json", null };

		yield return ["test.unicode.json", null];
		yield return ["unicode.json", null];

		yield return ["aos1.json", "aos1.expect.json"];
		yield return ["aos2.json", "aos2.expect.json"];

		yield return ["map_176df_EasyStandard.json", null];
		yield return ["map_1d3d2_ExpertPlusStandard.json", null];
		yield return ["map_2dad5_ExpertPlusStandard.json", null];
		yield return ["map_386ea_ExpertStandard.json", null];

		//foreach (var file in Directory.EnumerateFiles(@"E:\Downloads\386ea (Stop and Stare - RateGyro)", "*.dat"))
		//{
		//	yield return new object?[] { file, null };
		//}
	}

	[TestMethod, DynamicData(nameof(TestFiles), DynamicDataSourceType.Method)]
	public void AosRoundtrip(string file, string? aosFile)
	{
		var opt = new JBMOptions()
		{
			AosMinArraySize = 2,
			AosMinSparseFraction = 4,
		};

		var expectNode = Read(file);
		var expectText = expectNode.ToJsonString();

		var aosNode = AosConverter.Encode(expectNode, opt)!;
		var aosText = aosNode.ToJsonString();

		if (aosFile is not null)
		{
			var expectAosNode = Read(aosFile);
			var expectAosText = expectAosNode.ToJsonString();
			AssertStructuralEqual(expectAosText, aosText);
		}

		var aosDecodeNode = JsonSerializer.Deserialize<AosData<JsonElement>>(aosText, opt.JsonSerializerOptions)!;

		var actualNode = AosConverter.Decode(aosDecodeNode, opt);
		var actualText = actualNode.ToJsonString();

		AssertStructuralEqual(expectText, actualText);

		JsonNode Read(string file)
		{
			var fullPath = Path.IsPathRooted(file) ? file : Path.Combine("Assets", file);
			using var fileStream = File.OpenRead(fullPath);
			return JsonSerializer.Deserialize<JsonNode>(fileStream, opt.JsonSerializerOptions)!;
		}
	}
}
