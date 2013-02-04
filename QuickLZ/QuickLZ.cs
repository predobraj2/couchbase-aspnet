using System;
using System.Runtime.InteropServices;

namespace QuickLZ
{
    /// <summary>
    /// C# DLL wrapper class for the QuickLZ 1.5.x DLL files
    /// For QuickLZ documentation please check http://www.quicklz.com
    /// Please note that QuickLZ enterprise license is not FREE for more companies with more than 1 employees, so make sure you purchase licenses at http://www.quicklz.com/order.html
    /// Note: QuickLZ Dlls should be present in QuickLZC folder and copied to the same folder inside the bin folder of your app.
    /// </summary>
    public class QuickLZWrapper
    {
        // The C library passes many integers through the C type size_t which is 32 or 64 bits on 32 or 64 bit 
        // systems respectively. The C# type IntPtr has the same property but because IntPtr doesn't allow 
        // arithmetic we cast to and from int on each reference. To pass constants use (IntPrt)1234.
        [DllImport("QuickLZC/quicklz150_64_1.dll")]
        private static extern IntPtr qlz_compress(byte[] source, byte[] destination, IntPtr size, byte[] scratch);
        [DllImport("QuickLZC/quicklz150_64_1.dll")]
        private static extern IntPtr qlz_decompress(byte[] source, byte[] destination, byte[] scratch);
        [DllImport("QuickLZC/quicklz150_64_1.dll")]
        private static extern IntPtr qlz_size_compressed(byte[] source);
        [DllImport("QuickLZC/quicklz150_64_1.dll")]
        private static extern IntPtr qlz_size_decompressed(byte[] source);
        [DllImport("QuickLZC/quicklz150_64_1.dll")]
        private static extern int qlz_get_setting(int setting);

        private readonly byte[] _stateCompress;
        private readonly byte[] _stateDecompress;

        public QuickLZWrapper()
        {
            _stateCompress = new byte[qlz_get_setting(1)];
            _stateDecompress = QLZ_STREAMING_BUFFER == 0 ? _stateCompress : new byte[qlz_get_setting(2)];
        }

        public byte[] Compress(byte[] source)
        {
            var d = new byte[source.Length + 400];			
            var s = (uint)qlz_compress(source, d, (IntPtr)source.Length, _stateCompress);
            var d2 = new byte[s];
            Array.Copy(d, d2, s);
            return d2;
        }

        public byte[] Decompress(byte[] source)
        {
            var d = new byte[(uint)qlz_size_decompressed(source)];		
            var s = (uint)qlz_decompress(source, d, _stateDecompress);
            return d;
        }

        public uint SizeCompressed(byte[] source)
        {
            return (uint)qlz_size_compressed(source);
        }

        public uint SizeDecompressed(byte[] source)
        {
            return (uint)qlz_size_decompressed(source);
        }

        public uint QLZ_COMPRESSION_LEVEL
        {
            get
            {
                return (uint)qlz_get_setting(0);
            }
        }

        public uint QLZ_SCRATCH_COMPRESS
        {
            get
            {
                return (uint)qlz_get_setting(1);
            }
        }

        public uint QLZ_SCRATCH_DECOMPRESS
        {
            get
            {
                return (uint)qlz_get_setting(2);
            }
        }

        public uint QLZ_VERSION_MAJOR
        {
            get
            {
                return (uint)qlz_get_setting(7);
            }
        }

        public uint QLZ_VERSION_MINOR
        {
            get
            {
                return (uint)qlz_get_setting(8);
            }
        }


        public int QLZ_VERSION_REVISION
        {
            // negative means beta
            get
            {
                return qlz_get_setting(9);
            }
        }

        public uint QLZ_STREAMING_BUFFER
        {
            get
            {
                return (uint)qlz_get_setting(3);
            }
        }


        public bool QLZ_MEMORY_SAFE
        {
            get
            {
                return qlz_get_setting(6) == 1;
            }
        }



    }
}
