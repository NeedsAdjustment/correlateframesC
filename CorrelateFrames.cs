using System;
using System.Drawing;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.IO;

namespace whiterabbitc
{
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
                if (frames >= fileArray.Length) {frames = fileArray.Length;};
                if (frames == 0) {frames = fileArray.Length;};
                Bitmap[] frameArray = new Bitmap[frames];
                for (int i = 0; i < frames; i++)
                {
                    frameArray[i] = new Bitmap(fileArray[i].FullName);
                }
                CorrelateFrames CF = new CorrelateFrames();
                float[] correlationData = CF.plotCorrelation(mask, frameArray);
            });

            return rootCommand.InvokeAsync(args).Result;
        }

        public float[] plotCorrelation(int maskSize, Bitmap[] frameArray)
        {
            int frameCount = frameArray.Length;
            float[] correlation = new float[frameCount - 1];
            Bitmap frame1;
            Bitmap frame2;

            for (int i = 1; i < frameCount; i++)
            {
                float[] corrArray = new float[frameCount - i];

                for (int j = 1; j < frameCount - i; j++)
                {
                    frame1 = frameArray[j];
                    frame2 = frameArray[i + j];

                    float[] meanframe1 = getMeanFrame(maskSize, frame1);
                    float[] meanframe2 = getMeanFrame(maskSize, frame2);

                    corrArray[j - 1] = (float) (getR(meanframe1, meanframe2));
                }
                correlation[i - 1] = getMean(corrArray);
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

                for (int j = 0; j < masksX; j++)
                {
                    int xStep = j * maskSize;
                    int index = 0;

                    for (int y = yStep; y < (yStep + maskSize); y++)
                    {
                        for (int x = xStep; x < (xStep + maskSize); x++)
                        {
                            localArea[index] = frame.GetPixel(x, y).GetBrightness();
                            index++;
                        }
                    }
                    maskArray[counter] = getMean(localArea);
                    counter++;
                }
            }
            return maskArray;
        }

        public float getR(float[] frame1, float[] frame2)
        {
            float t1 = 0, t2 = 0, sum = 0;
		    float xMean = getMean(frame1);
		    float yMean = getMean(frame2);
		    float xStd = getStdDev(xMean, frame1);
		    float yStd = getStdDev(yMean, frame2);
		    for (int i = 0; i < frame1.Length; i++)
            {
			    t1 = (frame1[i] - xMean) / xStd;
			    t2 = (frame2[i] - yMean) / yStd;
			    sum = sum + (t1 * t2);
		    }
		    float r = sum / (frame1.Length - 1);
		    return r;
        }

        public float getMean(float[] dataSet)
        {
            float mean = 0;
		    for (int i = 0; i < dataSet.Length; i++) {
			mean += dataSet[i];
		}
		return (float) (mean / dataSet.Length);
        }

        public float getStdDev(float mean, float[] frame)
        {
            float stdDev = 0;
			for (int i = 0; i < frame.Length; i++) {
				stdDev += sqr(mean - frame[i]);
			}
			return (float) (Math.Sqrt(stdDev / (frame.Length - 1)));
        }

        public float sqr(float i) { return i * i;}
    }
}
