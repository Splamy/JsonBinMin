﻿using Microsoft.VisualBasic;
using static JsonBinMin.JBMDecoder;

namespace JsonBinMin.Analysis;

internal class JBMAnalyzer
{
	private readonly JBMDecoder decoder = new();

	public static AnalysisReport Analyze(ReadOnlySpan<byte> data)
	{
		var analyzer = new JBMAnalyzer();
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
				report.TrackType(JBMType.IntInline);
				report.TrackInteger(JBMType.IntInline, 1);
				decoder.Parse(data, out rest);
				return false;
			}
		case DecodePoint.DObject:
			report.TrackType(JBMType.Object);

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
			report.TrackType(JBMType.Array);

			AnalyzeSqueezedNumber(report, data);
			var arrElemCount = decoder.ReadNumberToInt(data, out data);
			for (int i = 0; i < arrElemCount; i++)
			{
				Analyze(report, data, out data);
			}
			rest = data;
			return false;

		case DecodePoint.DString:
			report.TrackType(JBMType.String);

			AnalyzeSqueezedNumber(report, data);
			decoder.ReadString(Stream.Null, data, out rest);
			return false;

		case DecodePoint.Block101:
			report.TrackType((JBMType)(pick & 0b1_111_11_0_0));

			switch ((JBMType)(pick & 0b1_111_11_0_0))
			{
			case JBMType.Float16:
				report.TrackInteger(JBMType.Float16, 2);
				break;
			case JBMType.Float32:
				report.TrackInteger(JBMType.Float32, 4);
				break;
			case JBMType.Float64:
				report.TrackInteger(JBMType.Float64, 8);
				break;
			}
			decoder.Parse(data, out rest);
			return false;

		case DecodePoint.Block110:
			switch ((JBMType)(pick & 0b1_111_111_0))
			{
			case JBMType.Int8:
				report.TrackType(JBMType.Int8);
				report.TrackInteger(JBMType.Int8, 1);
				break;
			case JBMType.Int16:
				report.TrackType(JBMType.Int16);
				report.TrackInteger(JBMType.Int16, 2);
				break;
			case JBMType.Int24:
				report.TrackType(JBMType.Int24);
				report.TrackInteger(JBMType.Int24, 2);
				break;
			case JBMType.Int32:
				report.TrackType(JBMType.Int32);
				report.TrackInteger(JBMType.Int32, 2);
				break;
			case JBMType.Int48:
				report.TrackType(JBMType.Int48);
				report.TrackInteger(JBMType.Int48, 2);
				break;
			case JBMType.Int64:
				report.TrackType(JBMType.Int64);
				report.TrackInteger(JBMType.Int64, 2);
				break;
			case JBMType.IntRle:
				int rleOff = 1;
				do { } while ((data[rleOff++] & 0x80) != 0);

				report.TrackType(JBMType.IntRle);
				report.TrackInteger(JBMType.IntRle, rleOff);
				break;
			}
			decoder.Parse(data, out rest);
			return false;

		case DecodePoint.NumStr:
			{
				int nsOff = 1;

				while (true)
				{
					var b = data[nsOff++];

					if (!(Get((byte)(b >> 4)) is { } bFirst)) break;
					if (!(Get((byte)(b & 0xF)) is { } bSecond)) break;

					static byte? Get(byte val) => val switch
					{
						>= 0 and <= 9 => (byte)('0' + val),
						0xA => (byte)'+',
						0xB => (byte)'-',
						0xC => (byte)'.',
						0xD => (byte)'e',
						0xE => (byte)'E',
						0xF => null,
						_ => throw new InvalidDataException(),
					};
				}

				report.TrackType(JBMType.NumStr);
				report.TrackInteger(JBMType.NumStr, nsOff);
				decoder.Parse(data, out rest);
				return false;
			}

		case DecodePoint.False:
			report.TrackType(JBMType.False);
			rest = data[1..];
			return false;

		case DecodePoint.True:
			report.TrackType(JBMType.True);
			rest = data[1..];
			return false;

		case DecodePoint.Null:
			report.TrackType(JBMType.Null);
			rest = data[1..];
			return false;

		case DecodePoint.MetaDictDef:
			{
				report.TrackType(JBMType.MetaDictDef);
				Analyze(data[1..]);
				var dictSize = (int)decoder.ReadNumberToInt(data[1..], out data);
				report.DictElementsCount = dictSize;
				var dict = new byte[dictSize][];
				var dictCtx = new JBMDecoder
				{
					Dict = dict // allow in-self referential dict entries
				};
				for (int i = 0; i < dictSize; i++)
				{
					dictCtx.Output.SetLength(0);
					dictCtx.Parse(data, out data);
				}
				rest = data;
				return true;
			}

		case DecodePoint.Compressed:
			throw new Exception("Mid jbm compression is not supported");

		default:
			throw new InvalidDataException();
		}
	}


	public void AnalyzeSqueezedNumber(AnalysisReport report, ReadOnlySpan<byte> data)
	{
		var pick = data[0];

		switch ((JBMType)(pick & 0b1_111_0000))
		{
		case JBMType.Object:
		case JBMType.Array:
		case JBMType.String:
			var hVal = (data[0] & 0xF);
			if (hVal < 0xF)
			{
				report.TrackInteger((JBMType)(pick & 0b1_111_0000), 1);
				return;
			}
			Analyze(report, data[1..], out _);
			return;
		}
	}
}
