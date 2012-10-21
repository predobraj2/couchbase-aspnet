using System;
using System.IO;
using System.IO.Compression;

namespace Couchbase.AspNet.Compression
{
	public class GzipCompressor : ICompressor
	{
		public byte[] Compress(byte[] data)
		{

			using (var output = new MemoryStream())
			{
				try
				{
					using (var gzip = new GZipStream(output, CompressionMode.Compress, true))
					{
						gzip.Write(data, 0, data.Length);
						gzip.Close();
					}
				}
				catch (Exception ex)
				{
					throw new Exception("Cannot Compress Session Data", ex);
				}
				return output.ToArray();
			}
		}

		public byte[] Decompress(MemoryStream input)
		{
			try
			{
				using (var output = new MemoryStream())
				{
					using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
					{
						var buff = new byte[64];
						int read = gzip.Read(buff, 0, buff.Length);
						while (read > 0)
						{
							output.Write(buff, 0, read);
							read = gzip.Read(buff, 0, buff.Length);
						}
						gzip.Close();
					}
					return output.ToArray();
				}
			}
			catch (Exception ex)
			{
				throw new Exception("Cannot Decompress Session Data", ex);
			}
		}
	}
}
