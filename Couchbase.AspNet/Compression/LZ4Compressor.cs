/*
 * Wrapper for C# LZ4 Compression library
*/

using System.IO;
using LZ4Sharp;

namespace Couchbase.AspNet.Compression
{
	public class LZ4Compressor : ICompressor
	{
		private readonly ILZ4Compressor _compressor;
		private readonly ILZ4Decompressor _decompressor;

		public LZ4Compressor()
		{
			_compressor = LZ4CompressorFactory.CreateNew();
			_decompressor = LZ4DecompressorFactory.CreateNew();
		}

		public byte[] Compress(byte[] data)
		{
			return _compressor.Compress(data);
		}

		public byte[] Decompress(MemoryStream input)
		{
			var bytes = input.ToArray();
			return _decompressor.Decompress(bytes);
		}
	}
}
