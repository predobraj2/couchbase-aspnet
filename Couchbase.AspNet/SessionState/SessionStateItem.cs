using System;
using System.Web.SessionState;
using System.Web;
using System.IO;
using System.Web.UI;
using Couchbase.AspNet.Compression;
using Enyim.Caching;
using Enyim.Caching.Memcached;
using NLog;

namespace Couchbase.AspNet.SessionState
{
	public class SessionStateItem
	{
		#region Members

		private static readonly string HeaderPrefix = (System.Web.Hosting.HostingEnvironment.SiteName ?? String.Empty).Replace(" ", "-") + "+" + System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath + "info-";
		private static readonly string DataPrefix = (System.Web.Hosting.HostingEnvironment.SiteName ?? String.Empty).Replace(" ", "-") + "+" + System.Web.Hosting.HostingEnvironment.ApplicationVirtualPath + "data-";

		public SessionStateItemCollection Data;
		public SessionStateActions Flag;
		public ulong LockId;
		public DateTime LockTime;

		// this is in minutes
		public int Timeout;

		public ulong HeadCas;
		public ulong DataCas;

		#endregion

		public bool Save(IMemcachedClient client, string id, bool metaOnly, bool useCas, ICompressor compressor, Logger logger)
		{
			var ts = TimeSpan.FromMinutes(Timeout);

			bool retval;

			using (var ms = new MemoryStream())
			{
				// Save the header first
				SaveHeader(ms);

				// Attempt to save the header and fail if the CAS fails
				retval = useCas
					         ? client.Cas(StoreMode.Set, HeaderPrefix + id,
					                      new ArraySegment<byte>(ms.GetBuffer(), 0, (int) ms.Length), ts, HeadCas).Result
					         : client.Store(StoreMode.Set, HeaderPrefix + id,
					                        new ArraySegment<byte>(ms.GetBuffer(), 0, (int) ms.Length), ts);
				if (retval == false)
				{
					return false;
				}
			}

			// Save the data
			if (!metaOnly)
			{

				byte[] data;

				ArraySegment<byte> arraySegment;

				using (var ms = new MemoryStream())
				{
					using (var bw = new BinaryWriter(ms))
					{
						// Serialize the data
						Data.Serialize(bw);
						data = ms.ToArray();						
					}
				}
			
				if (compressor == null)
				{
					if (logger != null)
						logger.Info(string.Format("Save Item with id '{0}' with size {1}", id, data.LongLength));

					arraySegment = new ArraySegment<byte>(data);
				}
				else
				{
					var tempdata = compressor.Compress(data);

					if (logger != null)
						logger.Info(string.Format("Save Item with id '{0}' that was compressed from {1} bytes to {2} bytes", id, data.LongLength, tempdata.LongLength));

					arraySegment = new ArraySegment<byte>(tempdata);
				}

				// Attempt to save the data and fail if the CAS fails
				retval = useCas
					         ? client.Cas(StoreMode.Set, DataPrefix + id, arraySegment, ts, DataCas).Result
					         : client.Store(StoreMode.Set, DataPrefix + id, arraySegment, ts);
			}

			// Return the success of the operation
			return retval;

		}

		public static SessionStateItem Load(IMemcachedClient client, string id, bool metaOnly, ICompressor compressor, Logger logger)
		{
			return Load(HeaderPrefix, DataPrefix, client, id, metaOnly, compressor, logger);
		}
	
		public SessionStateStoreData ToStoreData(HttpContext context)
		{
			return new SessionStateStoreData(Data, SessionStateUtility.GetSessionStaticObjects(context), Timeout);
		}

		public static void Remove(IMemcachedClient client, string id)
		{
			client.Remove(DataPrefix + id);
			client.Remove(HeaderPrefix + id);
		}

		#region Private

		private void SaveHeader(MemoryStream ms)
		{
			var p = new Pair(
								(byte)1,
								new Triplet(
												(byte)Flag,
												Timeout,
												new Pair(
															LockId,
															LockTime.ToBinary()
														)
											)
							);

			new ObjectStateFormatter().Serialize(ms, p);
		}

		private static SessionStateItem Load(string headerPrefix, string dataPrefix, IMemcachedClient client, string id, bool metaOnly, ICompressor compressor, Logger logger)
		{
			// Load the header for the item 
			var header = client.GetWithCas<byte[]>(headerPrefix + id);
			if (header.Result == null)
			{
				return null;
			}

			// Deserialize the header values
			SessionStateItem entry;
			using (var ms = new MemoryStream(header.Result))
			{
				entry = LoadItem(ms);
			}
			entry.HeadCas = header.Cas;

			// Bail early if we are only loading the meta data
			if (metaOnly)
			{
				return entry;
			}

			// Load the data for the item
			var data = client.GetWithCas<byte[]>(dataPrefix + id);
			if (data.Result == null)
			{
				return null;
			}
			entry.DataCas = data.Cas;

			// Deserialize the data
			if (compressor == null)
			{
				if (logger != null)
					logger.Info(string.Format("Load data from Session item with id '{0}' with size {1}", id, data.Result.LongLength));

				using (var ms = new MemoryStream(data.Result))
				{
					using (var br = new BinaryReader(ms))
					{
						entry.Data = SessionStateItemCollection.Deserialize(br);
					}
				}
			}
			else
			{
				using (var input = new MemoryStream(data.Result))
				{
					var decompressed = compressor.Decompress(input);

					if (logger != null)
						logger.Info(string.Format("Load data from Session item with id '{0}' with compessed size {1}. Size after decompression is {2}", id, data.Result.LongLength, decompressed.LongLength));

					using (var output = new MemoryStream(decompressed))
					{
						using (var reader = new BinaryReader(output))
						{
							entry.Data = SessionStateItemCollection.Deserialize(reader);
						}
					}
				}
			}

			// Return the session entry
			return entry;
		}

		private static SessionStateItem LoadItem(MemoryStream ms)
		{
			var graph = new ObjectStateFormatter().Deserialize(ms) as Pair;
			if (graph == null)
				return null;

			if (((byte)graph.First) != 1)
				return null;

			var t = (Triplet)graph.Second;
			var retval = new SessionStateItem
			{
				Flag = (SessionStateActions)((byte)t.First),
				Timeout = (int)t.Second
			};

			var lockInfo = (Pair)t.Third;

			retval.LockId = (ulong)lockInfo.First;
			retval.LockTime = DateTime.FromBinary((long)lockInfo.Second);

			return retval;
		}
	
		#endregion

	}
}
