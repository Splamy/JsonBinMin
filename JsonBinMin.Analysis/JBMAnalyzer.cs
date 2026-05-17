using JsonBinMin.BinV1;
using static JsonBinMin.BinV1.JbmDecoder;

namespace JsonBinMin.Analysis;

internal class JbmAnalyzer
{
	private readonly JbmDecoder decoder = new();

	public static AnalysisReport Analyze(ReadOnlySpan<byte> data)
	{
		var analyzer = new JbmAnalyzer();
		var report = new AnalysisReport();
		while (analyzer.Analyze(report, data, out data)) ;
		return report;
	}

	public bool Analyze(AnalysisReport report, ReadOnlySpan<byte> data, out ReadOnlySpan<byte> rest)
	{
		report.TotalTokenCount++;

		var pick = data[0];
		if (pick > 0x7F) // (pick & 0x80) != 0
		{
			report.DictTokenCount++;
			rest = data[1..];
			return false;
		}

		switch (DecodeMap[pick])
		{
		case DecodePoint.IntInline:
			{
				report.TrackType(JbmType.IntInline);
				report.TrackInteger(JbmType.IntInline, 1);
				decoder.Parse(data, out rest);
				return false;
			}
		case DecodePoint.DObject:
			report.TrackType(JbmType.Object);

			AnalyzeSqueezedNumber(report, data);
			var objElemCount = decoder.ReadNumberToInt(data, out data);
			for (int i = 0; i < objElemCount; i++)
			{
				Analyze(report, data, out data);
				Analyze(report, data, out data);
			}
			rest = data;
			return false;

		case DecodePoint.DArray:
			report.TrackType(JbmType.Array);

			AnalyzeSqueezedNumber(report, data);
			var arrElemCount = decoder.ReadNumberToInt(data, out data);
			for (int i = 0; i < arrElemCount; i++)
			{
				Analyze(report, data, out data);
			}
			rest = data;
			return false;

		case DecodePoint.DString:
			report.TrackType(JbmType.String);

			AnalyzeSqueezedNumber(report, data);
			decoder.ReadString(Stream.Null, data, out rest);
			return false;

		case DecodePoint.Block101:
			report.TrackType((JbmType)(pick & 0b1_111_11_0_0));

			switch ((JbmType)(pick & 0b1_111_11_0_0))
			{
			case JbmType.Float16:
				report.TrackInteger(JbmType.Float16, 2);
				break;
			case JbmType.Float32:
				report.TrackInteger(JbmType.Float32, 4);
				break;
			case JbmType.Float64:
				report.TrackInteger(JbmType.Float64, 8);
				break;
			}
			decoder.Parse(data, out rest);
			return false;

		case DecodePoint.Block110:
			switch ((JbmType)(pick & 0b1_111_111_0))
			{
			case JbmType.Int8:
				report.TrackType(JbmType.Int8);
				report.TrackInteger(JbmType.Int8, 1);
				break;
			case JbmType.Int16:
				report.TrackType(JbmType.Int16);
				report.TrackInteger(JbmType.Int16, 2);
				break;
			case JbmType.Int24:
				report.TrackType(JbmType.Int24);
				report.TrackInteger(JbmType.Int24, 2);
				break;
			case JbmType.Int32:
				report.TrackType(JbmType.Int32);
				report.TrackInteger(JbmType.Int32, 2);
				break;
			case JbmType.Int48:
				report.TrackType(JbmType.Int48);
				report.TrackInteger(JbmType.Int48, 2);
				break;
			case JbmType.Int64:
				report.TrackType(JbmType.Int64);
				report.TrackInteger(JbmType.Int64, 2);
				break;
			case JbmType.IntRle:
				ReadBlock110(Stream.Null, data, out var read);
				var len = data.Length - read.Length;
				report.TrackType(JbmType.IntRle);
				report.TrackInteger(JbmType.IntRle, len);
				break;
			}
			decoder.Parse(data, out rest);
			return false;

		case DecodePoint.NumStr:
			{
				ReadNumStr(Stream.Null, data, out rest);

				var len = data.Length - rest.Length;
				report.TrackType(JbmType.NumStr);
				report.TrackInteger(JbmType.NumStr, len);
				return false;
			}

		case DecodePoint.False:
			report.TrackType(JbmType.False);
			rest = data[1..];
			return false;

		case DecodePoint.True:
			report.TrackType(JbmType.True);
			rest = data[1..];
			return false;

		case DecodePoint.Null:
			report.TrackType(JbmType.Null);
			rest = data[1..];
			return false;

		case DecodePoint.MetaDictDef:
			{
				report.TrackType(JbmType.MetaDictDef);

				decoder.Parse(data, out rest);
				report.DictElementsCount = decoder.Dict.Length;
				return true;
			}

		default:
			throw new InvalidDataException();
		}
	}


	public void AnalyzeSqueezedNumber(AnalysisReport report, ReadOnlySpan<byte> data)
	{
		var pick = data[0];

		switch ((JbmType)(pick & 0b1_111_0000))
		{
		case JbmType.Object:
		case JbmType.Array:
		case JbmType.String:
			var hVal = (data[0] & 0xF);
			if (hVal < 0xF)
			{
				report.TrackInteger((JbmType)(pick & 0b1_111_0000), 1);
				return;
			}
			Analyze(report, data[1..], out _);
			return;
		}
	}
}
