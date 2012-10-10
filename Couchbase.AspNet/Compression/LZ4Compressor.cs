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

		public void Decompress(Stream input, Stream output)
		{
			using (var br = new BinaryReader(input))
			{
				var res = Decompressor.Decompress(br.ReadBytes((int) input.Length));
				output.Write(res, 0, res.Length);
			}			
		}
	}
}
