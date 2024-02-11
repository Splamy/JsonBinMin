using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using JsonBinMin.Aos;
using NUnit.Framework;
using NUnit.Framework.Internal;

namespace JsonBinMin.Tests;

[TestFixture]
public class AosTests
{
	public static IEnumerable<object> TestFiles()
	{
		yield return new object[] { "simple_01.json" };
		yield return new object[] { "simple_02.json" };
		yield return new object[] { "simple_03.json" };
		yield return new object[] { "simple_04.json" };
		yield return new object[] { "opts_01.json" };
		yield return new object[] { "nums_01.json" };
		yield return new object[] { "nums_02.json" };
		yield return new object[] { "nums_03.json" };
		yield return new object[] { "big_01.json" };
		yield return new object[] { "big_02.json" };

		yield return new object[] { "test.unicode.json" };
		yield return new object[] { "unicode.json" };

		yield return new object[] { "map_176df_EasyStandard.json" };
	}

	[Test, TestCaseSource(nameof(TestFiles))]
	public void AosRoundtrip(string file)
	{
		var jsonText = File.ReadAllBytes(Path.Combine("Assets", file));
		var json = JsonSerializer.Deserialize<JsonNode>(jsonText);
		var aos = AosConverter.Encode(json);
		var serText = aos.ToJsonString();
		var tmpSer = JsonSerializer.Deserialize<AosData<JsonElement>>(serText)!;
		var aosNodeRt = AosConverter.Decode(tmpSer);
		AssertUtil.AssertStructuralEqual(json.ToJsonString(), aosNodeRt.ToJsonString());
	}
}
