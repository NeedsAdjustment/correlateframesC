using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace whiterabbitc
{
    class CorrelateFrames
    {
        private const int framesDefault = 0, maskDefault = 3, maskXDefault = 0, maskYDefault = 3, sepDefault = 0;

        public static int Main(string[] args)
        {
            if (args.Length != 1)
            {
                var rootCommand = new RootCommand
                {
                    new Option(
                        new[] {"--input", "-i"},
                        "Input file (.avi) or folder containing .avi files")
                    {
                        Argument = new Argument<DirectoryInfo>(defaultValue: () => null)
                    },
                    new Option(
                        new[] {"--output", "-o"},
                        "Output folder to store results")
                    {
                        Argument = new Argument<DirectoryInfo>(defaultValue: () => null)
                    },
                    new Option(
                        new[] {"--frames", "-f"},
                        "Frames to process")
                    {
                        Argument = new Argument<int>(defaultValue: () => framesDefault)
                    },
                    new Option(
                        new[] {"--mask", "-m"},
                        "Mask size")
                    {
                        Argument = new Argument<int>(defaultValue: () => maskDefault)
                    },
                    new Option(
                        new[] {"--maskx", "-x"},
                        "Mask X-dimension")
                    {
                        Argument = new Argument<int>(defaultValue: () => maskXDefault)
                    },
                    new Option(
                        new[] {"--masky", "-y"},
                        "Mask Y-dimension")
                    {
                        Argument = new Argument<int>(defaultValue: () => maskYDefault)
                    },
                    new Option(
                        new[] {"--separation", "-s"},
                        "Frame separation")
                    {
                        Argument = new Argument<int>(defaultValue: () => sepDefault)
                    }
                };

                rootCommand.Handler = CommandHandler.Create(new Action<DirectoryInfo, DirectoryInfo, int, int, int, int, int>(Excecute));
                return rootCommand.InvokeAsync(args).Result;
            } else
            {
                Excecute(new DirectoryInfo(args[0]), null, framesDefault, maskDefault, maskXDefault, maskYDefault, sepDefault);
                return 0;
            }
        }

        public static void Excecute(DirectoryInfo input, DirectoryInfo output, int frames, int mask, int maskx, int masky, int separation)
        {
            FileInfo[] fileArray;
            int videoCount = 0, successCount = 0;

            if (input == null) input = new DirectoryInfo(Directory.GetCurrentDirectory());  //Use current directory if none specified
            FileInfo inputFile = new FileInfo(input.FullName);

            if (input.Exists)
            {
                fileArray = input.GetFiles();   //Input path is a directory; retrieve the list of files               
            } else if(inputFile.Exists)
            {
                fileArray = new FileInfo[] { new FileInfo(input.FullName) };    //Input path is a file; instantiate array with a single file
                input = new DirectoryInfo(Path.GetDirectoryName(input.FullName));  //Use directory of file for output if not specified
            }
            else {
                Console.WriteLine(input + " does not exist.");
                return;
            }

            foreach (FileInfo file in fileArray) if (file.Extension == ".avi") videoCount++;    //Count all the .avi files
            Console.WriteLine("Found " + videoCount + " video file(s) in " + input);
            if (videoCount == 0) return;

            if (output == null) output = input; //Use input directory for output if not specified

            //Correct for omission of trailing '\' on directory path
            string outDir = output.FullName.EndsWith('\\') ? output.FullName : output.FullName + '\\';

            foreach (FileInfo file in fileArray)
            {
                if (file.Extension == ".avi")
                {
                    try
                    {
                        using (AVIFrameReader aviRead = new AVIFrameReader(file.FullName))
                        {
                            if (maskx == 0) { maskx = mask; masky = mask; }
                            int nFrames = (aviRead.FrameCount < frames || frames < 1) ? aviRead.FrameCount : frames;

                            Console.WriteLine("Reading " + Path.GetFileName(file.FullName) + "...");

                            //Load all frames from .avi into array
                            byte[][] frameArray = new byte[nFrames][];
                            for (int i = 0; i < nFrames; i++)
                            {
                                frameArray[i] = aviRead.GetFrameData(i);
                            }

                            Console.WriteLine("Processing...");
                            var correlationData = PlotCorrelation(maskx, masky, aviRead.FrameWidth, aviRead.FrameHeight, aviRead.FrameStride, frameArray, separation);

                            string[] outputIndex = new string[nFrames + 1];
                            outputIndex[0] = "Lag Time (s)";
                            for (int i = 0; i < nFrames; i++) outputIndex[i + 1] = (i / aviRead.FrameRate).ToString();

                            List<string> correlationStr = correlationData.correlation.Select(x => x.ToString()).ToList();
                            correlationStr.Insert(0, "1");
                            correlationStr.Insert(0, "Correlation");

                            List<string> brightnessStr = correlationData.brightness.Select(x => x.ToString()).ToList();
                            brightnessStr.Insert(0, "Brightness");

                            string[][] csvData = new string[3][];
                            csvData[0] = outputIndex;
                            csvData[1] = correlationStr.ToArray();
                            csvData[2] = brightnessStr.ToArray();

                            //Create file name for output .csv with same name as input .avi in supplied output directory
                            FileInfo fileOutput = new FileInfo(outDir + Path.GetFileNameWithoutExtension(file.FullName) + ".csv");

                            using (StreamWriter outputFile = fileOutput.CreateText())
                            {
                                for (int x = 0; x < nFrames + 1; x++)
                                {
                                    string line = csvData[0][x] + "," + csvData[1][x] + "," + csvData[2][x];
                                    outputFile.WriteLine(line);
                                }
                            }
                            successCount++;
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message);
                    }
                }
            }
            Console.WriteLine("Successfully auto-correlated " + successCount + "/" + videoCount + " videos.");
        }

        public static (double[] correlation, double[] brightness) PlotCorrelation(int maskSizeX, int maskSizeY, int width, int height, int stride, byte[][] frameArray, int separation)
        {
            int frameCount = frameArray.Length;

            double[][] brightnessArrayArray = new double[frameCount][];
            for (int i = 0; i < frameCount; i++) {brightnessArrayArray[i] = GetMeanFrame(maskSizeX, maskSizeY, width, height, stride, frameArray[i]);}

            double[] brightness  = new double[frameCount];
            for (int i = 0; i < frameCount; i++) {brightness[i] = brightnessArrayArray[i].Average();}

            double[] correlation = new double[frameCount - 1];
            for (int i = 1; i <= frameCount - 1; i++)
            {
                double corrSum = 0;

                if (separation == 0)
                {
                    for (int j = 1; j <= frameCount - i; j++) {corrSum += GetR(brightnessArrayArray[j - 1], brightnessArrayArray[i + j - 1]);}
                    correlation[i - 1] = corrSum / (frameCount - i);
                }
                else
                {
                    for (int j = 1; j <= frameCount - i && j <= separation; j++)
                    {corrSum += GetR(brightnessArrayArray[j - 1], brightnessArrayArray[i + j - 1]);}
                    if ((frameCount - i) < separation) {correlation[i - 1] = corrSum / (frameCount - i);}
                    else {correlation[i - 1] = corrSum / separation;}
                }
            }
            return (correlation, brightness);
        }

        public static double[] GetMeanFrame(int maskSizeX, int maskSizeY, int width, int height, int stride, byte[] frame)
        {
            int masksX = width / maskSizeX;
            int masksY = height / maskSizeY;
            double[] maskArray = new double[masksX * masksY];

            int bytesPerPixel = 1;

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
                        int currentLineIndex = y * stride;
                        for (int x = xStep; x < xStep + bytesPerPixel * maskSizeX; x += bytesPerPixel)
                        {
                            sum += (double) frame[currentLineIndex + x] / 255;
                        }
                    }
                    maskArray[counter] = sum / (maskSizeX * maskSizeY);
                    counter++;
                }
            }

            return maskArray;
        }

        public static double GetR(double[] frame1, double[] frame2)
        {
            double avg1 = frame1.Average();
            double avg2 = frame2.Average();

            double sum1 = frame1.Zip(frame2, (x1, y1) => (x1 - avg1) * (y1 - avg2)).Sum();

            double sumSqr1 = frame1.Sum(x => Math.Pow((x - avg1), 2));
            double sumSqr2 = frame2.Sum(y => Math.Pow((y - avg2), 2));

            return sum1 / Math.Sqrt(sumSqr1 * sumSqr2);
        }

        public static double GetStdDev(double mean, double[] frame)
        {
            double stdDev = 0;
			foreach (double mask in frame) {stdDev += Math.Pow((mean - mask), 2);}
			return Math.Sqrt(stdDev / (frame.Length - 1));
        }
    }
}
