using System;

namespace Couchbase.AspNet.Compression
{

    /// <summary>
    /// Responsable to Create Compressor
    /// </summary>
	public static class CompressorFactory
	{
		private volatile static ICompressor _compressor;
		private static readonly object CompressorSync = new object();

		/// <summary>
		/// Creates Compressor of given type and cache instance locally
		/// </summary>
		/// <param name="compressionTypeString"></param>
		/// <returns></returns>
		public static ICompressor Create(string compressionTypeString)
		{
			CompressionType compressionType;

			switch (compressionTypeString.ToLowerInvariant())
			{
				case "none":
					compressionType = CompressionType.None;
					break;

				case "gzip":
					compressionType = CompressionType.Gzip;
					break;

				case "lz4":
					compressionType = CompressionType.LZ4;
					break;

				case "quicklz":
					compressionType = CompressionType.QuickLZ;
					break;

				default:
					throw new NotSupportedException(string.Format("Given compression type {0} not supported", compressionTypeString));
			}

			switch (compressionType)
			{
				case CompressionType.None:
					return null;

				case CompressionType.Gzip:										

					if (_compressor == null)
					{
						lock (CompressorSync)
						{
							if (_compressor == null)
							{
								_compressor = new GzipCompressor();								
							}
						}
					}

					return _compressor;

				case CompressionType.LZ4:

					if (_compressor == null)
					{
						lock (CompressorSync)
						{
							if (_compressor == null)
								_compressor = new LZ4Compressor();
						}
					}

					return _compressor;

				case CompressionType.QuickLZ:

					if (_compressor == null)
					{
						lock (CompressorSync)
						{
							if (_compressor == null)
							{
								_compressor = new QuickLZCompressor();
							}
						}
					}

					return _compressor;		


				default:
					throw new NotSupportedException(string.Format("Given compression type {0} not supported", compressionType.ToString()));
			}
		}
	}
}
