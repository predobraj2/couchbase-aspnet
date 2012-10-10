using System;

namespace Couchbase.AspNet.Compression
{
	public static class CompressorFactory
	{
		public static ICompressor Create(CompressionType compressionType)
		{
			switch (compressionType)
			{
				case CompressionType.None:
					return null;

				case CompressionType.Gzip:
					return new GzipCompressor();					

				case CompressionType.LZ4:
					return new LZ4Compressor();					

				default:
					throw new NotSupportedException(string.Format("Given compression type {0} not supported", compressionType.ToString()));
			}
		}
	}
}
