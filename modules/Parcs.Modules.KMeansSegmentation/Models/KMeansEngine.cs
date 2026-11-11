namespace Parcs.Modules.KMeansSegmentation.Models
{
    public static class KMeansEngine
    {
        public static CentroidSetDto InitializeCentroids(
            RawImage image,
            int k,
            bool useSpatialFeatures,
            double spatialWeight,
            int seed)
        {
            int n = image.TotalPixels;
            if (k < 2 || k > 64)
            {
                throw new ArgumentOutOfRangeException(nameof(k), "ClustersK must be between 2 and 64.");
            }

            if (n < k)
            {
                throw new ArgumentException($"Image has fewer pixels ({n}) than ClustersK ({k}).");
            }

            var rng = new Random(seed);
            var centroids = new CentroidSetDto
            {
                K = k,
                R = new double[k],
                G = new double[k],
                B = new double[k],
                X = new double[k],
                Y = new double[k],
            };

            double invW = image.Width > 1 ? 255.0 * spatialWeight / (image.Width - 1) : 0.0;
            double invH = image.Height > 1 ? 255.0 * spatialWeight / (image.Height - 1) : 0.0;
            byte[] rgb = image.RgbBytes;

            var chosenIndices = new HashSet<int>();
            for (int c = 0; c < k; c++)
            {
                int idx;
                do
                {
                    idx = rng.Next(n);
                }
                while (!chosenIndices.Add(idx));

                SetCentroidFromPixel(centroids, c, idx, rgb, image.Width, useSpatialFeatures, invW, invH);
            }

            return centroids;
        }

        public static PartialAccumulatorDto AssignChunkAndAccumulate(
            byte[] chunkRgbBytes,
            int startPixelIndex,
            int pixelCount,
            int imageWidth,
            int imageHeight,
            bool useSpatialFeatures,
            double spatialWeight,
            CentroidSetDto centroids,
            byte[] labelsOut)
        {
            int k = centroids.K;
            var acc = new PartialAccumulatorDto
            {
                K = k,
                SumR = new double[k],
                SumG = new double[k],
                SumB = new double[k],
                SumX = new double[k],
                SumY = new double[k],
                Count = new long[k],
            };

            AssignLabels(chunkRgbBytes, startPixelIndex, pixelCount, imageWidth, imageHeight,
                useSpatialFeatures, spatialWeight, centroids, labelsOut, acc);
            return acc;
        }

        public static void AssignLabels(
            byte[] chunkRgbBytes,
            int startPixelIndex,
            int pixelCount,
            int imageWidth,
            int imageHeight,
            bool useSpatialFeatures,
            double spatialWeight,
            CentroidSetDto centroids,
            byte[] labelsOut)
        {
            AssignLabels(chunkRgbBytes, startPixelIndex, pixelCount, imageWidth, imageHeight,
                useSpatialFeatures, spatialWeight, centroids, labelsOut, acc: null);
        }

        private static void AssignLabels(
            byte[] chunkRgbBytes,
            int startPixelIndex,
            int pixelCount,
            int imageWidth,
            int imageHeight,
            bool useSpatialFeatures,
            double spatialWeight,
            CentroidSetDto centroids,
            byte[] labelsOut,
            PartialAccumulatorDto acc)
        {
            int k = centroids.K;
            double invW = useSpatialFeatures && imageWidth > 1 ? 255.0 * spatialWeight / (imageWidth - 1) : 0.0;
            double invH = useSpatialFeatures && imageHeight > 1 ? 255.0 * spatialWeight / (imageHeight - 1) : 0.0;

            double[] cR = centroids.R;
            double[] cG = centroids.G;
            double[] cB = centroids.B;
            double[] cX = centroids.X;
            double[] cY = centroids.Y;

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 3;
                double r = chunkRgbBytes[offset];
                double g = chunkRgbBytes[offset + 1];
                double b = chunkRgbBytes[offset + 2];

                double x = 0.0;
                double y = 0.0;
                if (useSpatialFeatures)
                {
                    int globalIdx = startPixelIndex + i;
                    int row = globalIdx / imageWidth;
                    int col = globalIdx - row * imageWidth;
                    x = col * invW;
                    y = row * invH;
                }

                int bestCluster = 0;
                double bestDistSq = double.MaxValue;

                for (int c = 0; c < k; c++)
                {
                    double dr = r - cR[c];
                    double dg = g - cG[c];
                    double db = b - cB[c];

                    double distSq = dr * dr + dg * dg + db * db;

                    if (useSpatialFeatures)
                    {
                        double dx = x - cX[c];
                        double dy = y - cY[c];
                        distSq += dx * dx + dy * dy;
                    }

                    if (distSq < bestDistSq)
                    {
                        bestDistSq = distSq;
                        bestCluster = c;
                    }
                }

                labelsOut[i] = (byte)bestCluster;
                if (acc is null)
                {
                    continue;
                }

                acc.SumR[bestCluster] += r;
                acc.SumG[bestCluster] += g;
                acc.SumB[bestCluster] += b;
                if (useSpatialFeatures)
                {
                    acc.SumX[bestCluster] += x;
                    acc.SumY[bestCluster] += y;
                }
                acc.Count[bestCluster]++;
            }
        }

        public static (CentroidSetDto NextCentroids, double MaxShift) UpdateCentroids(
            CentroidSetDto previous,
            IReadOnlyList<PartialAccumulatorDto> partials,
            bool useSpatialFeatures)
        {
            int k = previous.K;
            var totalR = new double[k];
            var totalG = new double[k];
            var totalB = new double[k];
            var totalX = new double[k];
            var totalY = new double[k];
            var totalCount = new long[k];

            foreach (var p in partials)
            {
                for (int c = 0; c < k; c++)
                {
                    totalR[c] += p.SumR[c];
                    totalG[c] += p.SumG[c];
                    totalB[c] += p.SumB[c];
                    if (useSpatialFeatures)
                    {
                        totalX[c] += p.SumX[c];
                        totalY[c] += p.SumY[c];
                    }
                    totalCount[c] += p.Count[c];
                }
            }

            var next = new CentroidSetDto
            {
                K = k,
                R = new double[k],
                G = new double[k],
                B = new double[k],
                X = new double[k],
                Y = new double[k],
            };

            double maxShift = 0.0;
            for (int c = 0; c < k; c++)
            {
                if (totalCount[c] > 0)
                {
                    double invCount = 1.0 / totalCount[c];
                    next.R[c] = totalR[c] * invCount;
                    next.G[c] = totalG[c] * invCount;
                    next.B[c] = totalB[c] * invCount;
                    next.X[c] = useSpatialFeatures ? totalX[c] * invCount : 0.0;
                    next.Y[c] = useSpatialFeatures ? totalY[c] * invCount : 0.0;
                }
                else
                {
                    next.R[c] = previous.R[c];
                    next.G[c] = previous.G[c];
                    next.B[c] = previous.B[c];
                    next.X[c] = previous.X[c];
                    next.Y[c] = previous.Y[c];
                }

                double dr = next.R[c] - previous.R[c];
                double dg = next.G[c] - previous.G[c];
                double db = next.B[c] - previous.B[c];
                double shiftSq = dr * dr + dg * dg + db * db;
                if (useSpatialFeatures)
                {
                    double dx = next.X[c] - previous.X[c];
                    double dy = next.Y[c] - previous.Y[c];
                    shiftSq += dx * dx + dy * dy;
                }

                double shift = Math.Sqrt(shiftSq);
                if (shift > maxShift)
                {
                    maxShift = shift;
                }
            }

            return (next, maxShift);
        }

        public static RawImage ReconstructSegmentedImage(
            int width,
            int height,
            byte[] labels,
            CentroidSetDto centroids)
        {
            int k = centroids.K;
            var paletteR = new byte[k];
            var paletteG = new byte[k];
            var paletteB = new byte[k];

            for (int c = 0; c < k; c++)
            {
                paletteR[c] = (byte)Math.Clamp((int)Math.Round(centroids.R[c]), 0, 255);
                paletteG[c] = (byte)Math.Clamp((int)Math.Round(centroids.G[c]), 0, 255);
                paletteB[c] = (byte)Math.Clamp((int)Math.Round(centroids.B[c]), 0, 255);
            }

            int n = width * height;
            var rgbOut = new byte[n * 3];
            for (int i = 0; i < n; i++)
            {
                byte cluster = labels[i];
                int offset = i * 3;
                rgbOut[offset] = paletteR[cluster];
                rgbOut[offset + 1] = paletteG[cluster];
                rgbOut[offset + 2] = paletteB[cluster];
            }

            return new RawImage(width, height, rgbOut);
        }

        private static void SetCentroidFromPixel(
            CentroidSetDto centroids,
            int c,
            int pixelIndex,
            byte[] rgb,
            int width,
            bool useSpatialFeatures,
            double invW,
            double invH)
        {
            int offset = pixelIndex * 3;
            centroids.R[c] = rgb[offset];
            centroids.G[c] = rgb[offset + 1];
            centroids.B[c] = rgb[offset + 2];

            if (useSpatialFeatures)
            {
                int row = pixelIndex / width;
                int col = pixelIndex - row * width;
                centroids.X[c] = col * invW;
                centroids.Y[c] = row * invH;
            }
        }
    }
}
