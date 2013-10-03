/*
 * Wrapper for QuickLZ Dll wrapper
*/

using System.IO;
using QuickLZ;

namespace Couchbase.AspNet.Compression
{
	public class QuickLZCompressor : ICompressor
	{
		private readonly QuickLZWrapper _compressor;

		public QuickLZCompressor()
		{
			_compressor = new QuickLZWrapper();
		}

		public byte[] Compress(byte[] data)
		{
			return _compressor.Compress(data);
		}

		public byte[] Decompress(MemoryStream input)
		{
			var bytes = input.ToArray();
			return _compressor.Decompress(bytes);
		}
	}
}
