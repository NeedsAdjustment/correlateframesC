using System;
using System.Drawing;
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
                float[] correlationData = CF.plotCorrelation(mask, frameArray);
            });

            return rootCommand.InvokeAsync(args).Result;
        }

        public float[] plotCorrelation(int maskSize, Bitmap[] frameArray)
        {
            int frameCount = frameArray.Length;
            float[] correlation = new float[frameCount - 1];

            for (int i = 1; i <= frameCount - 1; i++)
            {
                float[] corrArray = new float[frameCount - i];

                for (int j = 1; j <= frameCount - i; j++)
                {
                    corrArray[j - 1] = getR(getMeanFrame(maskSize, frameArray[j - 1]), getMeanFrame(maskSize, frameArray[i + j - 1]));
                }
                correlation[i - 1] = corrArray.Average();
                Console.WriteLine(correlation[i - 1]);
            }
            return correlation;
        }

        public float[] getMeanFrame(int maskSize, Bitmap frame)
        {
            int masksX = (int) (frame.Width / maskSize);
            int masksY = (int) (frame.Height / maskSize);

            float[] maskArray = new float[masksX * masksY];
            float[] localArea = new float[maskSize * maskSize];

            int counter = 0;

            for (int i = 0; i < masksY; i++)
            {
                int yStep = i * maskSize;
                int yStepMask = yStep + maskSize;

                for (int j = 0; j < masksX; j++)
                {
                    int xStep = j * maskSize;
                    int xStepMask = xStep + maskSize;
                    int index = 0;

                    for (int y = yStep; y < yStepMask; y++)
                    {
                        for (int x = xStep; x < xStepMask; x++)
                        {
                            localArea[index] = frame.GetPixel(x, y).GetBrightness();
                            index++;
                        }
                    }
                    maskArray[counter] = localArea.Average();
                    counter++;
                }
            }
            return maskArray;
        }

        public float getR(float[] frame1, float[] frame2)
        {
            var avg1 = frame1.Average();
            var avg2 = frame2.Average();

            var sum1 = frame1.Zip(frame2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

            var sumSqr1 = (float) (frame1.Sum(x => Math.Pow((x - avg1), 2.0)));
            var sumSqr2 = (float) (frame2.Sum(y => Math.Pow((y - avg2), 2.0)));

            var result = (float) (sum1 / Math.Sqrt(sumSqr1 * sumSqr2));

            return result;
        }

        public float getStdDev(float mean, float[] frame)
        {
            float stdDev = 0;
			foreach (float fr in frame) {stdDev += (mean - fr) * (mean - fr);}
			return (float) (Math.Sqrt(stdDev / (frame.Length - 1)));
        }
    }
}
