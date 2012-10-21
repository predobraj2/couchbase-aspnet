/*
 * Wrapper for C# LZ4 Compression library
*/

using System.IO;
using LZ4Sharp;

namespace Couchbase.AspNet.Compression
{
	public class LZ4Compressor : ICompressor
	{
		private static readonly ILZ4Compressor Compressor;
		private static readonly ILZ4Decompressor Decompressor;

		static LZ4Compressor()
		{
			Compressor = LZ4CompressorFactory.CreateNew();
			Decompressor = LZ4DecompressorFactory.CreateNew();
		}

		public byte[] Compress(byte[] data)
		{
			return Compressor.Compress(data);
		}

		public byte[] Decompress(MemoryStream input)
		{
			return Decompressor.Decompress(input.ToArray());			
		}
	}
}
