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
                    new[] {"--output", "-o"},
                    "Output filepath")
                {
                    Argument = new Argument<FileInfo>(defaultValue: () => null)
                },
                new Option(
                    new[] {"--frames", "-f"},
                    "Frames to process")
                {
                    Argument = new Argument<int>(defaultValue: () => 0)
                },
                new Option(
                    new[] {"--mask", "-m"},
                    "Mask size")
                {
                    Argument = new Argument<int>(defaultValue: () => 3)
                },
                new Option(
                    new[] {"--maskx", "-x"},
                    "Mask X-dimension")
                {
                    Argument = new Argument<int>(defaultValue: () => 0)
                },
                new Option(
                    new[] {"--masky", "-y"},
                    "Mask Y-dimension")
                {
                    Argument = new Argument<int>(defaultValue: () => 3)
                },
                new Option(
                    new[] {"--separation", "-s"},
                    "Frame separation")
                {
                    Argument = new Argument<int>(defaultValue: () => 0)
                },
                new Option(
                    new[] {"--title", "-title"},
                    "Correlation title")
                {
                    Argument = new Argument<string>(defaultValue: () => "Correlation")
                }
            };
            rootCommand.Handler = CommandHandler.Create<FileInfo, int, int, int, int, int, string>((output, frames, mask, maskx, masky, separation, title) =>
            {
                DirectoryInfo workingPath = Directory.GetParent(Directory.GetCurrentDirectory());
                FileInfo[] fileArray = workingPath.GetFiles();
                Array.Sort(fileArray, new NaturalFileInfoNameComparer());
                if (frames >= fileArray.Length || frames == 0) {frames = fileArray.Length;}
                if (maskx == 0) {maskx = mask; masky = mask;}
                Bitmap[] frameArray = new Bitmap[frames];
                for (int i = 0; i < frames; i++) {frameArray[i] = new Bitmap(fileArray[i].FullName);}
                CorrelateFrames CF = new CorrelateFrames();
                var correlationData = CF.plotCorrelation(maskx, masky, frameArray, separation);

                int[] outputIndex = Enumerable.Range(1, frames).ToArray();
                List<string> indexStr = outputIndex.Select(x=>x.ToString()).ToList();
                indexStr.Insert(0, "Frame");

                List<string> correlationStr = correlationData.correlation.Select(x=>x.ToString()).ToList();
                correlationStr.Insert(0, "1");
                correlationStr.Insert(0, "Correlation");

                List<string> brightnessStr = correlationData.brightness.Select(x=>x.ToString()).ToList();
                brightnessStr.Insert(0, "Brightness");

                string[][] csvData = new string[3][];
                csvData[0] = indexStr.ToArray();
                csvData[1] = correlationStr.ToArray();
                csvData[2] = brightnessStr.ToArray();

                if (output == null) {output = new FileInfo("output.csv");}

                using (StreamWriter outputFile = output.CreateText())
                {
                    for (int x = 0; x < frames + 1; x++)
                    {
                        string line = csvData[0][x] + "," + csvData[1][x] + "," + csvData[2][x];
                        outputFile.WriteLine(line);
                    }
                }
            });

            return rootCommand.InvokeAsync(args).Result;
        }

        public (double[] correlation, double[] brightness) plotCorrelation(int maskSizeX, int maskSizeY, Bitmap[] frameArray, int separation)
        {
            int frameCount = frameArray.Length;

            double[][] brightnessArrayArray = new double[frameCount][];
            for (int i = 0; i < frameCount; i++) {brightnessArrayArray[i] = getMeanFrame(maskSizeX, maskSizeY, frameArray[i]);}

            double[] brightness  = new double[frameCount];
            for (int i = 0; i < frameCount; i++) {brightness[i] = brightnessArrayArray[i].Average();}

            double[] correlation = new double[frameCount - 1];
            for (int i = 1; i <= frameCount - 1; i++)
            {
                double corrSum = 0;

                if (separation == 0)
                {
                    for (int j = 1; j <= frameCount - i; j++) {corrSum += getR(brightnessArrayArray[j - 1], brightnessArrayArray[i + j - 1]);}
                    correlation[i - 1] = corrSum / (frameCount - i);
                }
                else
                {
                    for (int j = 1; j <= frameCount - i && j <= separation; j++)
                    {corrSum += getR(brightnessArrayArray[j - 1], brightnessArrayArray[i + j - 1]);}
                    if ((frameCount - i) < separation) {correlation[i - 1] = corrSum / (frameCount - i);}
                    else {correlation[i - 1] = corrSum / separation;}
                }
            }
            return (correlation, brightness);
        }

        public double[] getMeanFrame(int maskSizeX, int maskSizeY, Bitmap frame)
        {
            double[] maskArray;

            unsafe
            {
                BitmapData frameData = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadWrite, frame.PixelFormat);

                int masksX = frameData.Width / maskSizeX;
                int masksY = frameData.Height / maskSizeY;
                double[] maskArrayUnsafe = new double[masksX * masksY];

                int bytesPerPixel = System.Drawing.Bitmap.GetPixelFormatSize(frame.PixelFormat) / 8;
                byte* PtrFirstPixel = (byte*)frameData.Scan0;

                int counter = 0;

                for (int i = 0; i < masksY; i++)
                {
                    int yStep = i * maskSizeY;

                    for (int j = 0; j < masksX; j++)
                    {
                        int xStep = j * maskSizeX * bytesPerPixel;
                        double sum = 0;

                        for (int y = yStep; y < yStep + maskSizeY; y++)
                        {
                            byte* currentLine = PtrFirstPixel + (y * frameData.Stride);
                            for (int x = xStep; x < xStep + bytesPerPixel * maskSizeX; x = x + bytesPerPixel)
                            {
                                sum += (double) currentLine[x] / 255;
                            }
                        }
                        maskArrayUnsafe[counter] = sum / (maskSizeX * maskSizeY);
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
            double avg1 = frame1.Average();
            double avg2 = frame2.Average();

            double sum1 = frame1.Zip(frame2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

            double sumSqr1 = frame1.Sum(x => Math.Pow((x - avg1), 2));
            double sumSqr2 = frame2.Sum(y => Math.Pow((y - avg2), 2));

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
