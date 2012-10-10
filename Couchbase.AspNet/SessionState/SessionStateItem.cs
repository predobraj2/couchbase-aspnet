using System;
using System.IO.Compression;
using System.Web.SessionState;
using System.Web;
using System.IO;
using System.Web.UI;
using Enyim.Caching;
using Enyim.Caching.Memcached;

namespace Couchbase.AspNet.SessionState
{
	public class SessionStateItem
	{
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

		public bool Save(IMemcachedClient client, string id, bool metaOnly, bool useCas, bool compress)
		{
			using (var ms = new MemoryStream())
			{
				// Save the header first
				SaveHeader(ms);
				var ts = TimeSpan.FromMinutes(Timeout);

				// Attempt to save the header and fail if the CAS fails
				bool retval = useCas
					? client.Cas(StoreMode.Set, HeaderPrefix + id, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), ts, HeadCas).Result
					: client.Store(StoreMode.Set, HeaderPrefix + id, new ArraySegment<byte>(ms.GetBuffer(), 0, (int)ms.Length), ts);
				if (retval == false)
				{
					return false;
				}

				// Save the data
				if (!metaOnly)
				{
					ms.Position = 0;

					// Serialize the data
					using (var bw = new BinaryWriter(ms))
					{
						Data.Serialize(bw);

						byte[] data;
						int length;

						if (!compress)
						{
							data = ms.GetBuffer();
							length = (int)ms.Length;
						}
						else
						{
							var uncompressedData = ms.GetBuffer();
							data = Compress(uncompressedData);
							length = data.Length;
						}

						var arraySegment = new ArraySegment<byte>(data, 0, length);

						// Attempt to save the data and fail if the CAS fails
						retval = useCas
							? client.Cas(StoreMode.Set, DataPrefix + id, arraySegment, ts, DataCas).Result
							: client.Store(StoreMode.Set, DataPrefix + id, arraySegment, ts);
					}
				}

				// Return the success of the operation
				return retval;
			}
		}
	
		public static SessionStateItem Load(IMemcachedClient client, string id, bool metaOnly, bool compress)
		{
			return Load(HeaderPrefix, DataPrefix, client, id, metaOnly,compress);
		}

		public static SessionStateItem Load(string headerPrefix, string dataPrefix, IMemcachedClient client, string id, bool metaOnly, bool compress)
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
			if (!compress)
			{
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
					using (var output = new MemoryStream())
					{
						Decompress(input, output);
						output.Position = 0;
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

		private static byte[] Compress(byte[] data)
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

		private static void Decompress(Stream input, Stream output)
		{
			try
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
			}
			catch (Exception ex)
			{
				throw new Exception("Cannot Decompress Session Data", ex);
			}
		}

		#endregion

	}
}
