using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;

namespace JsonBinMin.BinV1;

internal class DictBuilder(JBMOptions options)
{
	private readonly MemoryStream mem = new();
	private readonly Dictionary<string, DictEntry> buildDictNum = [];
	private readonly Dictionary<string, DictEntry> buildDictStr = [];

	public bool IsFinalized { get; private set; } = false;

	public byte[]? DictSerialized { get; private set; }

	public void BuildDictionary(JsonElement elem)
	{
		CheckNotFinalized();

		switch (elem.ValueKind)
		{
		case JsonValueKind.Object:
			var propCount = 0;
			foreach (var kvp in elem.EnumerateObject())
			{
				AddStringToDict(kvp.Name);
				BuildDictionary(kvp.Value);
				propCount++;
			}

			AddNumberToDict(propCount.ToString(CultureInfo.InvariantCulture));
			break;

		case JsonValueKind.Array:
			var arrCount = 0;
			foreach (var arrItem in elem.EnumerateArray())
			{
				BuildDictionary(arrItem);
				arrCount++;
			}

			AddNumberToDict(arrCount.ToString(CultureInfo.InvariantCulture));
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

	[Conditional("DEBUG")]
	private void CheckNotFinalized()
	{
		if (IsFinalized)
			throw new InvalidOperationException();
	}

	public void FinalizeDictionary()
	{
		if (IsFinalized)
			return;
		IsFinalized = true;

		var dictValues = Enumerable.Empty<(string Key, DictEntry Entry, DictElemKind Kind)>()
			.Concat(buildDictNum.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.Number)))
			.Concat(buildDictStr.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.String)))
			.Where(x => x.Entry.Count * x.Entry.Data.Length > x.Entry.Count + x.Entry.Data.Length)
			.OrderByDescending(x => x.Entry.Count * x.Entry.Data.Length)
			.Take(0x7F)
			.OrderBy(x => x.Kind)
			.Select(x => x.Entry)
			.ToArray();

		if (dictValues.Length == 0)
		{
			DictSerialized = [];
			return;
		}

		mem.SetLength(0);
		mem.WriteByte((byte)JBMType.MetaDictDef);
		Trace.Assert(dictValues.Length <= 0x7f);
		JBMEncoder.WriteNumberValue(dictValues.Length.ToString(CultureInfo.InvariantCulture), mem, options);

		for (var i = 0; i < dictValues.Length; i++)
		{
			var entry = dictValues[i];
			entry.Index = i;
			mem.Write(entry.Data);
		}

		DictSerialized = mem.ToArray();
	}

	[Conditional("DEBUG")]
	private void CheckFinalized()
	{
		if (!IsFinalized)
			throw new InvalidOperationException();
	}

	public bool TryGetString(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return buildDictStr.TryGetValue(key, out entry);
	}

	public bool TryGetNumber(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return buildDictNum.TryGetValue(key, out entry);
	}

	internal enum DictElemKind
	{
		Number, // Do not move! Number needs to lower than other values
		String,
	}
}

[DebuggerDisplay("{Count, nq} @{Index, nq}")]
internal class DictEntry(byte[] data)
{
	public byte[] Data { get; set; } = data;
	public int Count { get; set; } = 0;
	public int Index { get; set; } = -1;
	public bool IsIndexed => Index >= 0;
}
