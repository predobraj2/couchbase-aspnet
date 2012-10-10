using System.IO;

namespace Couchbase.AspNet.Compression
{
	public interface ICompressor
	{
		byte[] Compress(byte[] data);

		void Decompress(Stream input, Stream output);
	}
}
