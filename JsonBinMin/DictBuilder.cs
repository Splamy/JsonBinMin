using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace JsonBinMin;

internal class DictBuilder
{
	private readonly MemoryStream mem = new();
	private readonly Dictionary<string, DictEntry> buildDictNum = new();
	private readonly Dictionary<string, DictEntry> buildDictStr = new();
	private readonly JBMOptions options;

	public bool IsFinalized => Dict != null;

	public DictBuilder(JBMOptions options)
	{
		this.options = options;
	}

	public Dictionary<(string, DictElemKind), DictEntry>? Dict { get; private set; }
	public byte[]? DictSerialized { get; private set; }

	public void BuildDictionary(JsonElement elem)
	{
		CheckNotFinalized();

		switch (elem.ValueKind)
		{
		case JsonValueKind.Object:
			var objElemems = elem.EnumerateObject().ToArray();

			AddNumberToDict(objElemems.Length.ToString(CultureInfo.InvariantCulture));

			foreach (var kvp in objElemems)
			{
				AddStringToDict(kvp.Name);
				BuildDictionary(kvp.Value);
			}
			break;

		case JsonValueKind.Array:
			var arrElemems = elem.EnumerateArray().ToArray();

			AddNumberToDict(arrElemems.Length.ToString(CultureInfo.InvariantCulture));

			foreach (var arrItem in arrElemems)
			{
				BuildDictionary(arrItem);
			}
			break;

		case JsonValueKind.String:
			AddStringToDict(elem.GetString());
			break;

		case JsonValueKind.Number:
			AddNumberToDict(elem.GetRawText());
			break;

		default:
			break;
		}
	}

	private void AddNumberToDict(string num)
	{
		if (byte.TryParse(num, out var byteValue) && byteValue is >= 0 and <= 0x1F)
			return;

		if (!buildDictNum.TryGetValue(num, out var de))
		{
			mem.SetLength(0);
			JBMEncoder.WriteNumberValue(num, mem, options);
			var binary = mem.ToArray();

			buildDictNum[num] = de = new DictEntry(binary);
		}
		de.Count++;
	}

	private void AddStringToDict(string? str)
	{
		if (string.IsNullOrEmpty(str)) return;

		AddNumberToDict(str.Length.ToString(CultureInfo.InvariantCulture));

		if (!buildDictStr.TryGetValue(str, out var de))
		{
			mem.SetLength(0);
			JBMEncoder.WriteStringValue(str, mem, options);
			var binary = mem.ToArray();

			buildDictStr[str] = de = new DictEntry(binary);
		}
		de.Count++;
	}

	private void CheckNotFinalized()
	{
		if (IsFinalized)
			throw new InvalidOperationException();
	}

	public void FinalizeDictionary()
	{
		if (IsFinalized)
			return;

		Dict = buildDictNum.Select(x => ((x.Key, DictElemKind.Number), x.Value))
			.Concat(buildDictStr.Select(x => ((x.Key, DictElemKind.String), x.Value)))
			.ToDictionary(x => x.Item1, x => x.Item2);

		if (Dict.Values.All(x => x.Count <= 1))
		{
			DictSerialized = Array.Empty<byte>();
			return;
		}

		var dictValues = Dict
			.Where(x => x.Value.Count > 1)
			.OrderByDescending(x => x.Value.Count * x.Value.Data.Length)
			.Take(0x7F)
			.OrderBy(x => x.Key.Item2)
			.ToArray();

		mem.SetLength(0);
		mem.WriteByte((byte)JBMType.MetaDictDef);
		Trace.Assert(dictValues.Length <= 0x7f);
		JBMEncoder.WriteNumberValue(dictValues.Length.ToString(CultureInfo.InvariantCulture), mem, options);

		for (int i = 0; i < dictValues.Length; i++)
		{
			var (k, v) = dictValues[i];
			v.Index = i;
			mem.Write(v.Data);
		}

		DictSerialized = mem.ToArray();
	}
}

internal enum DictElemKind
{
	Number, // Do not move! Number needs to lower than other values
	String,
}

[DebuggerDisplay("{Count, nq} @{Index, nq}")]
internal class DictEntry
{
	public byte[] Data { get; set; }
	public int Count { get; set; } = 0;
	public int Index { get; set; } = -1;
	public bool IsIndexed => Index >= 0;

	public DictEntry(byte[] data)
	{
		Data = data;
	}
}
