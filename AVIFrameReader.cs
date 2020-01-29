using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace whiterabbitc
{
    internal class AVIFrameReader : IDisposable
    {
        public const int ST_VIDEO = 0x73646976;                     //Four-byte code (FourCC) corresponding to video streams (ascii: "vids")
        public const int ERR_READ_ST_FMT_OVERFLOW = -2147205004;    //Error code returned when getting stream format and more data is present

        //Structure containing information about a bitmap
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BITMAPINFOHEADER
        {
            public int biSize;
            public int biWidth;
            public int biHeight;
            public short biPlanes;
            public short biBitCount;
            public int biCompression;
            public int biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public int biClrUsed;
            public int biClrImportant;
        }

        //Structure containing information about a stream
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct AVISTREAMINFO
        {
            public int fccType;
            public int fccHandler;
            public int dwFlags;
            public int dwCaps;
            public short wPriority;
            public short wLanguage;
            public int dwScale;
            public int dwRate;
            public int dwStart;
            public int dwLength;
            public int dwInitialFrames;
            public int dwSuggestedBufferSize;
            public int dwQuality;
            public int dwSampleSize;
            public uint rcFrameLeft;
            public uint rcFrameTop;
            public uint rcFrameRight;
            public uint rcFrameBottom;
            public int dwEditCount;
            public int dwFormatChangeCount;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] szName;
        }

        private readonly IntPtr avi;
        private readonly IntPtr frameObj;
        private readonly int dataOffset;

        private IntPtr frame;
        private BITMAPINFOHEADER bmInfo = new BITMAPINFOHEADER();
        private AVISTREAMINFO stInfo = new AVISTREAMINFO();
        private bool streamClosed = false;

        /// <summary>
        /// Returns the stride of each frame (number of bytes comprising one scan line)
        /// </summary>
        public int FrameStride { get; }

        /// <summary>
        /// Returns the number of frames in the stream
        /// </summary>
        public int FrameCount { get; }

        /// <summary>
        /// Returns the width of each frame in pixels
        /// </summary>
        public int FrameWidth { get; }

        /// <summary>
        /// Returns the height of each frame in pixels
        /// </summary>
        public int FrameHeight { get; }

        /// <summary>
        /// Returns the size of each frame in bytes
        /// </summary>
        public int FrameSize { get; }

        /// <summary>
        /// Returns the frame rate of the stream
        /// </summary>
        public double FrameRate { get; }

        /// <summary>
        /// Opens a new .avi filestream for decoding based on the given path string.
        /// TODO: Extend colour format support; deal with inverted bitmaps
        /// </summary>
        /// <param name="path">File path</param>
        public AVIFrameReader(string path)
        {
            int bmInfoSz = Marshal.SizeOf(bmInfo);

            int res = AVIStreamOpenFromFile(out avi, path, ST_VIDEO, 0, 0, 0);
            if (res != 0)
            {
                throw new Exception("Unable to open file '" + path + "'. Error code: " + res.ToString());
            }

            if (AVIStreamInfo(avi, ref stInfo, Marshal.SizeOf(stInfo)) != 0)
            {
                throw new Exception("Unable to read .avi stream information");
            }

            //Allow an error result here as only interested in the bitmap header and not subsequent not palette information
            res = AVIStreamReadFormat(avi, 0, ref bmInfo, ref bmInfoSz);
            if (res != 0 && res != ERR_READ_ST_FMT_OVERFLOW)
            {
                throw new Exception("Unable to read .avi stream format");
            }

            if (bmInfo.biBitCount != 8)
            {
                throw new Exception("Unsupported image format: images must be 8-bit monochrome");
            }

            frameObj = AVIStreamGetFrameOpen(avi, ref bmInfo);
            if (frameObj == null)
            {
                throw new Exception("No suitible decompressors found for supplied .avi file");
            }

            //Load frame information
            FrameCount = stInfo.dwLength - stInfo.dwStart - 1;
            FrameWidth = bmInfo.biWidth;
            FrameHeight = Math.Abs(bmInfo.biHeight);    //May be negative if bitmap inverted
            FrameStride = FrameWidth * bmInfo.biBitCount / 8;
            FrameSize = FrameStride * FrameHeight;
            FrameRate = (double)stInfo.dwRate / stInfo.dwScale;

            //Offset is size of info header + colour palette (32 bpp)
            dataOffset = Marshal.SizeOf(bmInfo) + (bmInfo.biClrUsed * 4);
        }

        /// <summary>
        /// Returns a byte array containing the bitmap data for a single (zero-indexed) frame n.
        /// The format is one byte per pixel, and the frame size is given by properties FrameWidth and FrameHeight.
        /// </summary>
        /// <param name="n">Frame number</param>
        /// <returns></returns>
        public byte[] GetFrameData(int n)
        {
            if (streamClosed == true)
            {
                throw new Exception("Error: stream is closed");
            }

            if (n < 0 || n >= FrameCount)
            {
                throw new Exception("Supplied frame number is out of range");
            }

            frame = AVIStreamGetFrame(frameObj, n + 1 + stInfo.dwStart);

            if (frame == null)
            {
                throw new Exception("Unable to read frame " + n.ToString());
            }

            byte[] dataArray = new byte[bmInfo.biSizeImage];
            Marshal.Copy(frame + dataOffset, dataArray, 0, bmInfo.biSizeImage);     //Copy data over to managed array

            return dataArray;
        }

        /// <summary>
        /// Helper function for converting a byte array (obtained with GetFrameData) to a windows bitmap.
        /// </summary>
        /// <param name="data">Byte array containing bitmap data (1 byte per pixel)</param>
        /// <param name="width">Width of bitmap in pixels</param>
        /// <param name="height">Height of bitmap in pixels</param>
        /// <returns></returns>
        public static Bitmap GetBitmapFromFrameData(byte[] data, int width, int height)
        {
            Bitmap bmp = new Bitmap(width, height, PixelFormat.Format8bppIndexed);

            BitmapData bmData = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, bmp.PixelFormat);
            Marshal.Copy(data, 0, bmData.Scan0, data.Length);
            bmp.UnlockBits(bmData);

            ColorPalette ncp = bmp.Palette;
            for (int i = 0; i < 256; i++) ncp.Entries[i] = Color.FromArgb(255, i, i, i);
            bmp.Palette = ncp;

            return bmp;
        }

        /// <summary>
        /// Close the .avi stream and release any resources used by the AVIFrameReader
        /// </summary>
        public void Close()
        {
            streamClosed = true;
            AVIStreamGetFrameClose(frameObj);
            AVIStreamRelease(avi);
        }

        public void Dispose()
        {
            Close();
        }

        #region External Imports

        //Open a single stream from a file
        [DllImport("avifil32.dll")]
        private static extern int AVIStreamOpenFromFile(
                out IntPtr ppavi,
                string szFile,
                int fccType,
                int lParam,
                int mode,
                int pclsidHandler);

        //Release an open AVI stream
        [DllImport("avifil32.dll")]
        private static extern ulong AVIStreamRelease(IntPtr aviStream);

        //Get a pointer to a GETFRAME object (returns 0 on error)
        [DllImport("avifil32.dll")]
        private static extern IntPtr AVIStreamGetFrameOpen(
            IntPtr pAVIStream,
            ref BITMAPINFOHEADER bih);

        //Release the GETFRAME object
        [DllImport("avifil32.dll")]
        private static extern int AVIStreamGetFrameClose(IntPtr pGetFrameObj);

        //Get a pointer to a packed DIB (returns 0 on error)
        [DllImport("avifil32.dll")]
        private static extern IntPtr AVIStreamGetFrame(
            IntPtr pGetFrameObj,
            int lPos);

        //Read the format for a stream
        [DllImport("avifil32.dll")]
        private static extern int AVIStreamReadFormat(
            IntPtr aviStream,
            int lPos,
            ref BITMAPINFOHEADER lpFormat,
            ref int cbFormat);

        //Obtains stream header information
        [DllImport("avifil32.dll")]
        private static extern int AVIStreamInfo(
            IntPtr pavi,
            ref AVISTREAMINFO psi,
            int lSize
        );

        #endregion

    }
}
