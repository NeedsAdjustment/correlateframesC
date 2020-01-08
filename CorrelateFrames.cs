

using System;
using System.Drawing;

namespace whiterabbitc
{
    class CorrelateFrames
    {
        private int maskSize;
        private Bitmap[] frameArray;
        
        //code to load bitmaps and mask size

        private float[] correlation => plotCorrelation(maskSize, frameArray);

        public float[] plotCorrelation(int maskSize, Bitmap[] frameArray)
        {
            int frameCount = frameArray.Length;
            float[] correlation = new float[frameCount - 1];
            Bitmap frame1;
            Bitmap frame2;

            for (int i = 1; i <= frameCount - 1; i++) 
            {
                float[] corrArray = new float[frameCount - i];

                for (int j = 1; j <= frameCount - 1; j++)
                {
                    frame1 = frameArray[j];
                    frame2 = frameArray[i + j];

                    float[] meanframe1 = getMeanFrame(maskSize, frame1);
                    float[] meanframe2 = getMeanFrame(maskSize, frame2);

                    corrArray[j - 1] = (float) (getR(meanframe1, meanframe2));
                }
                correlation[i - 1] = getMean(corrArray);
            }
            return correlation;
        }

        public float[] getMeanFrame(int maskSize, Bitmap frame)
        {
            int masksX = (int) (frame.Width / maskSize);
            int masksY = (int) (frame.Height / maskSize);

            float[] maskArray = new float[masksX * masksY];
            float[] localArea = new float[maskSize^2];

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
                            localArea[index] = frame.GetPixel(x, y).R;

                        }
                    }
                }
            }
        }
    }
}
