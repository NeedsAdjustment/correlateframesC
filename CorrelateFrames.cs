using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace whiterabbitc
{
    internal static class SafeNativeMethods
    {
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        public static extern int StrCmpLogicalW(string psz1, string psz2);
    }

    public sealed class NaturalFileInfoNameComparer : IComparer<FileInfo>
    {
        public int Compare(FileInfo a, FileInfo b)
        {
            return SafeNativeMethods.StrCmpLogicalW(a.Name, b.Name);
        }
    }
    class CorrelateFrames
    {
        public static int Main(string[] args)
        {
            var rootCommand = new RootCommand
            {
                new Option(
                    "--input",
                    "Path to the input directory")
                {
                    Argument = new Argument<DirectoryInfo>()
                },
                new Option(
                    "--frames",
                    "Number of frames to process")
                {
                    Argument = new Argument<int>(defaultValue: () => 0)
                },
                new Option(
                    "--mask",
                    "Mask size")
                {
                    Argument = new Argument<int>(defaultValue: () => 3)
                }
            };
            rootCommand.Handler = CommandHandler.Create<DirectoryInfo, int, int>((input, frames, mask) =>
            {
                FileInfo[] fileArray = input.GetFiles();
                Array.Sort(fileArray, new NaturalFileInfoNameComparer());
                if (frames >= fileArray.Length) {frames = fileArray.Length;};
                if (frames == 0) {frames = fileArray.Length;};
                Bitmap[] frameArray = new Bitmap[frames];
                for (int i = 0; i < frames; i++) {frameArray[i] = new Bitmap(fileArray[i].FullName);}
                CorrelateFrames CF = new CorrelateFrames();
                double[] correlationData = CF.plotCorrelation(mask, frameArray);
            });

            return rootCommand.InvokeAsync(args).Result;
        }

        public double[] plotCorrelation(int maskSize, Bitmap[] frameArray)
        {
            int frameCount = frameArray.Length;
            double[] correlation = new double[frameCount - 1];

            for (int i = 1; i <= frameCount - 1; i++)
            {
                double corrSum = 0;

                for (int j = 1; j <= frameCount - i; j++)
                {
                    corrSum += getR(getMeanFrame(maskSize, frameArray[j - 1]), getMeanFrame(maskSize, frameArray[i + j - 1]));
                }
                correlation[i - 1] = corrSum / (frameCount - i);
                Console.WriteLine(correlation[i - 1]);
            }
            return correlation;
        }

        public double[] getMeanFrame(int maskSize, Bitmap frame)
        {
            int counter = 0;
            double[] maskArray;

            unsafe
            {
                BitmapData frameData = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadWrite, frame.PixelFormat);
                int masksX = (int) (frameData.Width / maskSize);
                int masksY = (int) (frameData.Height / maskSize);
                double[] maskArrayUnsafe = new double[masksX * masksY];
                int bytesPerPixel = System.Drawing.Bitmap.GetPixelFormatSize(frame.PixelFormat) / 8;
                int maskBytes = bytesPerPixel * maskSize;
                int heightInPixels = frameData.Height;
                int widthInBytes = frameData.Width * bytesPerPixel;
                byte* PtrFirstPixel = (byte*)frameData.Scan0;

                for (int i = 0; i < masksY; i++)
                {
                    int yStep = i * maskSize;

                    for (int j = 0; j < masksX; j++)
                    {
                        int xStep = j * maskSize * bytesPerPixel;
                        double sum = 0;

                        for (int y = yStep; y < yStep + maskSize; y++)
                        {
                            byte* currentLine = PtrFirstPixel + (y * frameData.Stride);
                            for (int x = xStep; x < xStep + maskBytes; x = x + bytesPerPixel)
                            {
                                sum += (double)currentLine[x]/255.00;
                            }
                        }
                        maskArrayUnsafe[counter] = sum / (maskSize * maskSize);
                        counter++;
                    }
                }
                maskArray = maskArrayUnsafe;
                frame.UnlockBits(frameData);
            }
            return maskArray;
        }

        public double getR(double[] frame1, double[] frame2)
        {
            var avg1 = frame1.Average();
            var avg2 = frame2.Average();

            var sum1 = frame1.Zip(frame2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

            var sumSqr1 = frame1.Sum(x => Math.Pow((x - avg1), 2));
            var sumSqr2 = frame2.Sum(y => Math.Pow((y - avg2), 2));

            return sum1 / Math.Sqrt(sumSqr1 * sumSqr2);
        }

        public double getStdDev(double mean, double[] frame)
        {
            double stdDev = 0;
			foreach (double mask in frame) {stdDev += Math.Pow((mean - mask), 2);}
			return Math.Sqrt(stdDev / (frame.Length - 1));
        }
    }
}
