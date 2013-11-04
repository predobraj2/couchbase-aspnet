using System;
using System.Diagnostics;
using System.Globalization;
using System.Web.SessionState;
using System.Web;
using Couchbase.AspNet.Compression;
using Enyim.Caching;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace Couchbase.AspNet.SessionState
{
	public class CouchbaseSessionStateProvider : SessionStateStoreProviderBase
	{
		#region Members

		private IMemcachedClient _client;
        private bool _disposeClient;

        private static bool _exclusiveAccess;
		private static bool _compressData;
		private static bool _isLoggingEnabled;

		private static ICompressor _compressor;				
		private static Logger _logger;
		
		private static bool _isFirstTimeInitialization = true;
		private static readonly object FirstTimeInitializationSync = new object();

		#endregion

		#region Constructors
	
		#endregion

		public override void Initialize(string name, System.Collections.Specialized.NameValueCollection config)
		{
            // Initialize the base class
			base.Initialize(name, config);

            // Create our Couchbase client instance
            _client = ProviderHelper.GetClient(name, config, () => (ICouchbaseClientFactory)new CouchbaseClientFactory(), out _disposeClient);

			if (_isFirstTimeInitialization)
			{
				lock (FirstTimeInitializationSync)
				{
					if (_isFirstTimeInitialization)
					{
                        var loggingEnabled = ProviderHelper.GetAndRemove(config, "logging", false) ?? "false";
                        _isLoggingEnabled = (String.Compare(loggingEnabled, "true", StringComparison.OrdinalIgnoreCase) == 0);

					    if (_isLoggingEnabled)
					    {
                            // Path where to store NLog logs (effective if useExistedLoggingConfig != "true")
                            var loggingFileName = ProviderHelper.GetAndRemove(config, "loggingFilename", false) ?? "Logs/CouchbaseSessionStateProvider.log";
                            
                            // useExistedLoggingConfig = "true" to use existed application NLog config file 
                            // or useExistedLoggingConfig = "false" (or just skip this config) to configure NLog in Couchbase provider and override application settings (if exists)
                            var useExistedLoggingConfigString = ProviderHelper.GetAndRemove(config, "useExistedLoggingConfig", false) ?? "false";
                            var useExistedLoggingConfig = (String.Compare(useExistedLoggingConfigString, "true", StringComparison.OrdinalIgnoreCase) == 0);
                            
                            _logger = SetupLogger(useExistedLoggingConfig, loggingFileName);
					    }

					    // By default use exclusive session access. But allow it to be overridden in the config file
						var exclusive = ProviderHelper.GetAndRemove(config, "exclusiveAccess", false) ?? "true";
						_exclusiveAccess = (String.Compare(exclusive, "true", StringComparison.OrdinalIgnoreCase) == 0);

						// By default do not use compression on session data
						var compress = ProviderHelper.GetAndRemove(config, "compress", false) ?? "false";
						_compressData = (String.Compare(compress, "true", StringComparison.OrdinalIgnoreCase) == 0);
						
						// By default we use lz4 instead of gzip, because it is considered much faster!
						var compressionTypeString = ProviderHelper.GetAndRemove(config, "compressionType", false) ?? "quicklz";

						_compressor = CompressorFactory.Create(compressionTypeString);

						LogProviderConfiguration();

						// Make sure no extra attributes are included
						ProviderHelper.CheckForUnknownAttributes(config);

						_isFirstTimeInitialization = false;
					}
				}
			}
		}
	
		public override SessionStateStoreData CreateNewStoreData(HttpContext context, int timeout)
		{
			return new SessionStateStoreData(new SessionStateItemCollection(), SessionStateUtility.GetSessionStaticObjects(context), timeout);
		}

        public override void CreateUninitializedItem(HttpContext context, string id, int timeout)
        {
            var e = new SessionStateItem {
                Data = new SessionStateItemCollection(),
                Flag = SessionStateActions.InitializeItem,
                LockId = 0,
                Timeout = timeout
            };

			e.Save(_client, id, false, false, _compressor, _isLoggingEnabled ? _logger : null);
        }

		public override void Dispose()
		{
            if (_disposeClient) {
                _client.Dispose();
            }
		}

        public override void EndRequest(HttpContext context) { }

        public override SessionStateStoreData GetItem(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            var e = Get(_client, false, id, out locked, out lockAge, out lockId, out actions);

            return (e == null)
                    ? null
                    : e.ToStoreData(context);
        }

        public override SessionStateStoreData GetItemExclusive(HttpContext context, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
        {
            var e = Get(_client, true, id, out locked, out lockAge, out lockId, out actions);

            return (e == null)
                    ? null
                    : e.ToStoreData(context);
        }
	
        public override void InitializeRequest(HttpContext context)
        {
        }

        public override void ReleaseItemExclusive(HttpContext context, string id, object lockId)
        {
            var tmp = (ulong)lockId;
            SessionStateItem e;
            do {
                // Load the header for the item with CAS
				e = SessionStateItem.Load(_client, id, true, _compressor, _isLoggingEnabled ? _logger : null);

                // Bail if the entry does not exist, or the lock ID does not match our lock ID
                if (e == null || e.LockId != tmp) {
                    break;
                }

                // Attempt to clear the lock for this item and loop around until we succeed
                e.LockId = 0;
                e.LockTime = DateTime.MinValue;
			} while (!e.Save(_client, id, true, _exclusiveAccess, _compressor, _isLoggingEnabled ? _logger : null));
        }

        public override void RemoveItem(HttpContext context, string id, object lockId, SessionStateStoreData item)
        {
            var tmp = (ulong)lockId;
			var e = SessionStateItem.Load(_client, id, true, _compressor, _isLoggingEnabled ? _logger : null);

            if (e != null && e.LockId == tmp) {
                SessionStateItem.Remove(_client, id);
            }
        }

        public override void ResetItemTimeout(HttpContext context, string id)
        {
            SessionStateItem e;
            do {
                // Load the item with CAS
				e = SessionStateItem.Load(_client, id, false, _compressor, _isLoggingEnabled ? _logger : null);
                if (e == null) {
                    break;
                }

                // Try to save with CAS, and loop around until we succeed
			} while (!e.Save(_client, id, false, _exclusiveAccess, _compressor, _isLoggingEnabled ? _logger : null));
        }

        public override void SetAndReleaseItemExclusive(HttpContext context, string id, SessionStateStoreData item, object lockId, bool newItem)
        {
            SessionStateItem e;
            do {
                if (!newItem) {
                    var tmp = (ulong)lockId;

                    // Load the entire item with CAS (need the DataCas value also for the save)
					e = SessionStateItem.Load(_client, id, false, _compressor, _isLoggingEnabled ? _logger : null);

                    // if we're expecting an existing item, but
                    // it's not in the cache
                    // or it's locked by someone else, then quit
                    if (e == null || e.LockId != tmp) {
                        return;
                    }
                } else {
                    // Create a new item if it requested
                    e = new SessionStateItem();
                }

                // Set the new data and reset the locks
                e.Timeout = item.Timeout;
                e.Data = (SessionStateItemCollection)item.Items;
                e.Flag = SessionStateActions.None;
                e.LockId = 0;
                e.LockTime = DateTime.MinValue;

                // Attempt to save with CAS and loop around if it fails
			} while (!e.Save(_client, id, false, _exclusiveAccess && !newItem, _compressor, _isLoggingEnabled ? _logger : null));
        }

        public override bool SetItemExpireCallback(SessionStateItemExpireCallback expireCallback)
        {
            return false;
		}

		#region Private Methods

		private static Logger SetupLogger(bool useExistedLoggingConfig, string filename)
		{
		    if (!useExistedLoggingConfig)
		    {
		        var config = new LoggingConfiguration();
		        var fileTarget = new FileTarget();
		        config.AddTarget("file", fileTarget);
		        fileTarget.FileName = "${basedir}/" + filename;
		        fileTarget.Layout = "${longdate}|${level:uppercase=true}|${message}";
		        var rule = new LoggingRule("*", LogLevel.Debug, fileTarget);
		        config.LoggingRules.Add(rule);
		        NLog.LogManager.Configuration = config;
		    }

		    return NLog.LogManager.GetCurrentClassLogger();
		}

		private static SessionStateItem Get(IMemcachedClient client, bool acquireLock, string id, out bool locked, out TimeSpan lockAge, out object lockId, out SessionStateActions actions)
		{
			locked = false;
			lockId = null;
			lockAge = TimeSpan.Zero;
			actions = SessionStateActions.None;

			var e = SessionStateItem.Load(client, id, false, _compressor, _isLoggingEnabled ? _logger : null);
			if (e == null)
				return null;

			if (acquireLock)
			{
				// repeat until we can update the retrieved 
				// item (i.e. nobody changes it between the 
				// time we get it from the store and updates it s attributes)
				// Save() will return false if Cas() fails
				while (true)
				{
					if (e.LockId > 0)
						break;

					actions = e.Flag;

					e.LockId = _exclusiveAccess ? e.HeadCas : 0;
					e.LockTime = DateTime.UtcNow;
					e.Flag = SessionStateActions.None;

					// try to update the item in the store
					if (e.Save(client, id, true, _exclusiveAccess, _compressor, _isLoggingEnabled ? _logger : null))
					{
						locked = true;
						lockId = e.LockId;

						return e;
					}

					// it has been modified between we loaded and tried to save it
					e = SessionStateItem.Load(client, id, false, _compressor, _isLoggingEnabled ? _logger : null);
					if (e == null)
						return null;
				}
			}

			locked = true;
			lockAge = DateTime.UtcNow - e.LockTime;
			lockId = e.LockId;
			actions = SessionStateActions.None;

			return acquireLock ? null : e;
		}

		private static void LogProviderConfiguration()
		{
			if (_isLoggingEnabled)
			{
				var log = new EventLog
					{
						Source = "CouchbaseSessionStateProvider"
					};

				var str =
					string.Format(
						"IIS Process Id is {0}. Compression is set to {1}. Selected Compressor: {2}. Exclusive Access is set to {3}.",
						Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture),
						_compressData.ToString(),
						_compressor != null ? _compressor.GetType().Name : "None",
						_exclusiveAccess.ToString()
						);

				log.WriteEntry(str);

				_logger.Info(str);
			}
		}

		#endregion

	}
}

#region [ License information          ]
/* ************************************************************
 * 
 *    @author Couchbase <info@couchbase.com>
 *    @copyright 2012 Couchbase, Inc.
 *    @copyright 2012 Attila Kiskó, enyim.com
 *    @copyright 2012 Good Time Hobbies, Inc.
 *    
 *    Licensed under the Apache License, Version 2.0 (the "License");
 *    you may not use this file except in compliance with the License.
 *    You may obtain a copy of the License at
 *    
 *        http://www.apache.org/licenses/LICENSE-2.0
 *    
 *    Unless required by applicable law or agreed to in writing, software
 *    distributed under the License is distributed on an "AS IS" BASIS,
 *    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 *    See the License for the specific language governing permissions and
 *    limitations under the License.
 *    
 * ************************************************************/
#endregion