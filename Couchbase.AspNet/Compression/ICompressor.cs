using System.IO;

namespace Couchbase.AspNet.Compression
{
	public interface ICompressor
	{
		byte[] Compress(byte[] data);

		byte[] Decompress(MemoryStream input);
	}
}
