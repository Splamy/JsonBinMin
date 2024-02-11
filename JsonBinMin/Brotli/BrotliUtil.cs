using System.Buffers;
using System.IO.Compression;

namespace JsonBinMin.Brotli;

internal class BrotliUtil
{
	public static ReadOnlyMemory<byte> Compress(ReadOnlySpan<byte> rawJbm)
	{
		var maxOutputSize = BrotliEncoder.GetMaxCompressedLength(rawJbm.Length);
		var outputBuffer = new byte[maxOutputSize];
		var output = outputBuffer.AsSpan();

		Util.Assert(BrotliEncoder.TryCompress(rawJbm, output, out var written, 11, 24));
		return outputBuffer.AsMemory(0, written);
	}

	public static ReadOnlyMemory<byte> Decompress(ReadOnlySpan<byte> data)
	{
		var mem = new MemoryStream(Math.Max(8192, data.Length * 2));
		using var bd = new BrotliDecoder();
		Span<byte> buffer = stackalloc byte[8192];
		while (true)
		{
			var res = bd.Decompress(data, buffer, out var read, out var written);
			data = data[read..];
			mem.Write(buffer[..written]);

			switch (res)
			{
			case OperationStatus.DestinationTooSmall:
				continue;
			case OperationStatus.Done:
				return mem.GetBuffer().AsMemory(0, (int)mem.Length);
			case OperationStatus.InvalidData:
				throw new Exception("InvalidData");
			case OperationStatus.NeedMoreData:
				throw new Exception("NeedMoreData");
			}
		}
	}
}
