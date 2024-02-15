﻿using System.Collections.Generic;
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
		yield return new object?[] { "simple_01.json", null };
		yield return new object?[] { "simple_02.json", null };
		yield return new object?[] { "simple_03.json", null };
		yield return new object?[] { "simple_04.json", null };
		yield return new object?[] { "opts_01.json", null };
		yield return new object?[] { "nums_01.json", null };
		yield return new object?[] { "nums_02.json", null };
		yield return new object?[] { "nums_03.json", null };
		yield return new object?[] { "big_01.json", null };
		yield return new object?[] { "big_02.json", null };

		yield return new object?[] { "test.unicode.json", null };
		yield return new object?[] { "unicode.json", null };

		yield return new object?[] { "aos1.json", "aos1.expect.json" };
		yield return new object?[] { "aos2.json", "aos2.expect.json" };

		yield return new object?[] { "map_176df_EasyStandard.json", null };
		yield return new object?[] { "map_1d3d2_ExpertPlusStandard.json", null };
	}

	[Test, TestCaseSource(nameof(TestFiles))]
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
			AssertUtil.AssertStructuralEqual(expectAosText, aosText);
		}

		var aosDecodeNode = JsonSerializer.Deserialize<AosData<JsonElement>>(aosText, opt.JsonSerializerOptions)!;

		var actualNode = AosConverter.Decode(aosDecodeNode, opt);
		var actualText = actualNode.ToJsonString();

		AssertUtil.AssertStructuralEqual(expectText, actualText);

		JsonNode Read(string file)
		{
			using var fileStream = File.OpenRead(Path.Combine("Assets", file));
			return JsonSerializer.Deserialize<JsonNode>(fileStream, opt.JsonSerializerOptions)!;
		}
	}
}
