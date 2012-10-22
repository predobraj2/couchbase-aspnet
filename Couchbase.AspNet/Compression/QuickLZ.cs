/*
 * Wrapper for QuickLZ Dll wrapper
*/

using System.IO;
using QuickLZ;

namespace Couchbase.AspNet.Compression
{
	public class QuickLZCompressor : ICompressor
	{
		private static readonly QuickLZWrapper Compressor;

		static QuickLZCompressor()
		{
			Compressor = new QuickLZWrapper();
		}

		public byte[] Compress(byte[] data)
		{
			return Compressor.Compress(data);
		}

		public byte[] Decompress(MemoryStream input)
		{
			var bytes = input.ToArray();
			return Compressor.Decompress(bytes);
		}
	}
}
