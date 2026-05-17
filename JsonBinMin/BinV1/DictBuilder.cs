using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace JsonBinMin.BinV1;

internal class DictBuilder(JbmOptions options)
{
	private readonly MemoryStream _mem = new();
	private readonly Dictionary<string, DictEntry> _buildDictNum = [];
	private readonly Dictionary<string, DictEntry> _buildDictStr = [];

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
		}
	}

	private void AddNumberToDict(string num)
	{
		if (num.Length <= 3 && byte.TryParse(num, out var byteValue) && byteValue is >= 0 and <= 0x1F)
		{
			return;
		}

		ref var de = ref CollectionsMarshal.GetValueRefOrAddDefault(_buildDictNum, num, out var exists);
		
		if (exists)
		{
			de.Count++;
		}
		else
		{
			_mem.SetLength(0);
			JbmEncoder.WriteNumberValue(num, _mem, options);
			de.Count = 1;
			de.Index = byte.MaxValue;
			de.Data = _mem.ToArray();
		}
	}

	private void AddStringToDict(string? str)
	{
		if (string.IsNullOrEmpty(str))
		{
			return;
		}

		AddNumberToDict(str.Length.ToString(CultureInfo.InvariantCulture));

		
		ref var de = ref CollectionsMarshal.GetValueRefOrAddDefault(_buildDictNum, str, out var exists);
		if (exists)
		{
			de.Count++;
		}
		else
		{
			_mem.SetLength(0);
			JbmEncoder.WriteStringValue(str, _mem, options);
			de.Count = 1;
			de.Index = byte.MaxValue;
			de.Data = _mem.ToArray();
		}
	}

	[Conditional("DEBUG")]
	private void CheckNotFinalized()
	{
		if (IsFinalized)
		{
			throw new InvalidOperationException();
		}
	}

	public void FinalizeDictionary()
	{
		if (IsFinalized)
		{
			return;
		}

		IsFinalized = true;

		var dictValues = Enumerable.Empty<(string Key, DictEntry Entry, DictElemKind Kind)>()
			.Concat(_buildDictNum.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.Number)))
			.Concat(_buildDictStr.Select(x => (x.Key, Entry: x.Value, Kind: DictElemKind.String)))
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

		_mem.SetLength(0);
		_mem.WriteByte((byte)JBMType.MetaDictDef);
		Trace.Assert(dictValues.Length <= 0x7f);
		JbmEncoder.WriteNumberValue(dictValues.Length.ToString(CultureInfo.InvariantCulture), _mem, options);

		for (var i = 0; i < dictValues.Length; i++)
		{
			ref var entry = ref dictValues[i];
			entry.Index = (byte)i;
			_mem.Write(entry.Data);
		}

		DictSerialized = _mem.ToArray();
	}

	[Conditional("DEBUG")]
	private void CheckFinalized()
	{
		if (!IsFinalized)
		{
			throw new InvalidOperationException();
		}
	}

	public bool TryGetString(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return _buildDictStr.TryGetValue(key, out entry);
	}

	public bool TryGetNumber(string key, [MaybeNullWhen(false)] out DictEntry entry)
	{
		CheckFinalized();
		return _buildDictNum.TryGetValue(key, out entry);
	}

	internal enum DictElemKind
	{
		Number, // Do not move! Number needs to lower than other values
		String,
	}
}

[DebuggerDisplay("{Count, nq} @{Index, nq}")]
[StructLayout(LayoutKind.Auto)]
internal struct DictEntry
{
	public byte[] Data;
	public int Count;
	public byte Index;
	public bool IsIndexed => Index != byte.MaxValue;
}