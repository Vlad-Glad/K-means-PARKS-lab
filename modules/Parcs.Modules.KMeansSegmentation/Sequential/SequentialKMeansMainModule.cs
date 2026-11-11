using System.Diagnostics;
using System.Text.Json;
using Parcs.Modules.KMeansSegmentation.Models;
using Parcs.Net;

namespace Parcs.Modules.KMeansSegmentation.Sequential
{
    public class SequentialKMeansMainModule : IModule
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
        {
            var options = moduleInfo.BindModuleOptions<ModuleOptions>();

            var filenames = moduleInfo.InputReader.GetFilenames()?.ToList() ?? [];
            var inputFileName = filenames.FirstOrDefault(ImageCodec.IsSupportedFileName) ?? filenames.FirstOrDefault()
                ?? throw new ArgumentException("No input image (.jpg/.png) found in Job Input.");

            RawImage image;
            await using (var stream = moduleInfo.InputReader.GetFileStreamForFile(inputFileName))
            {
                image = ImageCodec.LoadFromStream(stream, inputFileName);
            }

            options.Validate(image.TotalPixels, useWorkers: false);
            var stopwatch = Stopwatch.StartNew();

            var centroids = KMeansEngine.InitializeCentroids(
                image,
                options.ClustersK,
                options.UseSpatialFeatures,
                options.SpatialWeight,
                options.Seed);

            var labels = new byte[image.TotalPixels];
            int iterationsPerformed = 0;
            bool converged = false;

            for (int iter = 0; iter < options.MaxIterations; iter++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                iterationsPerformed = iter + 1;

                var partial = KMeansEngine.AssignChunkAndAccumulate(
                    image.RgbBytes,
                    startPixelIndex: 0,
                    pixelCount: image.TotalPixels,
                    imageWidth: image.Width,
                    imageHeight: image.Height,
                    useSpatialFeatures: options.UseSpatialFeatures,
                    spatialWeight: options.SpatialWeight,
                    centroids: centroids,
                    labelsOut: labels);

                (centroids, double shift) = KMeansEngine.UpdateCentroids(
                    centroids,
                    [partial],
                    options.UseSpatialFeatures);

                if (shift <= options.ConvergenceThreshold)
                {
                    converged = true;
                    break;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            KMeansEngine.AssignLabels(image.RgbBytes, 0, image.TotalPixels, image.Width, image.Height,
                options.UseSpatialFeatures, options.SpatialWeight, centroids, labels);

            var segmentedImage = KMeansEngine.ReconstructSegmentedImage(
                image.Width,
                image.Height,
                labels,
                centroids);

            stopwatch.Stop();

            const string outputImageFile = "segmented.png";
            var encodedBytes = ImageCodec.Encode(segmentedImage, outputImageFile);

            var output = new ModuleOutput
            {
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                IterationsPerformed = iterationsPerformed,
                Converged = converged,
                ImageWidth = image.Width,
                ImageHeight = image.Height,
                ClustersK = options.ClustersK,
                PointsNumber = 1,
                UseSpatialFeatures = options.UseSpatialFeatures,
            };

            await moduleInfo.OutputWriter.WriteToFileAsync(encodedBytes, outputImageFile);
            await moduleInfo.OutputWriter.WriteToFileAsync(
                JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions),
                "Output.txt");
        }
    }
}
