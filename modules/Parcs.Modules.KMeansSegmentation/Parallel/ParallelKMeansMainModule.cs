using System.Diagnostics;
using System.Text.Json;
using Parcs.Modules.KMeansSegmentation.Models;
using Parcs.Net;

namespace Parcs.Modules.KMeansSegmentation.Parallel
{
    public class ParallelKMeansMainModule : IModule
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

            int p = options.PointsNumber;
            int totalPixels = image.TotalPixels;
            options.Validate(totalPixels, useWorkers: true);
            var points = new IPoint[p];
            var channels = new IChannel[p];
            var startPixels = new int[p];
            var chunkCounts = new int[p];

            int baseChunk = totalPixels / p;
            int remainder = totalPixels % p;

            for (int w = 0; w < p; w++)
            {
                points[w] = await moduleInfo.CreatePointAsync();
                channels[w] = await points[w].CreateChannelAsync();
                await points[w].ExecuteClassAsync<ParallelKMeansWorkerModule>();

                startPixels[w] = w * baseChunk + Math.Min(w, remainder);
                chunkCounts[w] = baseChunk + (w < remainder ? 1 : 0);
            }

            try
            {
                var stopwatch = Stopwatch.StartNew();

                var centroids = KMeansEngine.InitializeCentroids(
                    image,
                    options.ClustersK,
                    options.UseSpatialFeatures,
                    options.SpatialWeight,
                    options.Seed);

                var scatterTasks = new Task[p];
                for (int w = 0; w < p; w++)
                {
                    int workerIdx = w;
                    scatterTasks[workerIdx] = Task.Run(async () =>
                    {
                        int startPx = startPixels[workerIdx];
                        int countPx = chunkCounts[workerIdx];
                        var chunkRgb = new byte[countPx * 3];
                        Buffer.BlockCopy(image.RgbBytes, startPx * 3, chunkRgb, 0, chunkRgb.Length);

                        var ch = channels[workerIdx];
                        await ch.WriteDataAsync(startPx);
                        await ch.WriteDataAsync(countPx);
                        await ch.WriteDataAsync(image.Width);
                        await ch.WriteDataAsync(image.Height);
                        await ch.WriteDataAsync(options.UseSpatialFeatures);
                        await ch.WriteDataAsync(options.SpatialWeight);
                        await ch.WriteDataAsync(chunkRgb);
                    }, cancellationToken);
                }
                await Task.WhenAll(scatterTasks);

                int iterationsPerformed = 0;
                bool converged = false;

                for (int iter = 0; iter < options.MaxIterations; iter++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    iterationsPerformed = iter + 1;

                    var broadcastTasks = new Task[p];
                    for (int w = 0; w < p; w++)
                    {
                        int workerIdx = w;
                        broadcastTasks[workerIdx] = Task.Run(async () =>
                        {
                            await channels[workerIdx].WriteDataAsync(true);
                            await channels[workerIdx].WriteObjectAsync(centroids);
                        }, cancellationToken);
                    }
                    await Task.WhenAll(broadcastTasks);

                    var gatherTasks = new Task<PartialAccumulatorDto>[p];
                    for (int w = 0; w < p; w++)
                    {
                        gatherTasks[w] = channels[w].ReadObjectAsync<PartialAccumulatorDto>();
                    }
                    var partials = await Task.WhenAll(gatherTasks);

                    (centroids, double shift) = KMeansEngine.UpdateCentroids(
                        centroids,
                        partials,
                        options.UseSpatialFeatures);

                    if (shift <= options.ConvergenceThreshold)
                    {
                        converged = true;
                        break;
                    }
                }

                var labelTasks = new Task<byte[]>[p];
                for (int w = 0; w < p; w++)
                {
                    int workerIdx = w;
                    labelTasks[workerIdx] = Task.Run(async () =>
                    {
                        await channels[workerIdx].WriteDataAsync(false);
                        await channels[workerIdx].WriteObjectAsync(centroids);
                        return await channels[workerIdx].ReadBytesAsync();
                    }, cancellationToken);
                }

                var workerLabels = await Task.WhenAll(labelTasks);
                var fullLabels = new byte[totalPixels];
                for (int w = 0; w < p; w++)
                {
                    Buffer.BlockCopy(workerLabels[w], 0, fullLabels, startPixels[w], chunkCounts[w]);
                }

                var segmentedImage = KMeansEngine.ReconstructSegmentedImage(
                    image.Width,
                    image.Height,
                    fullLabels,
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
                    PointsNumber = p,
                    UseSpatialFeatures = options.UseSpatialFeatures,
                };

                await moduleInfo.OutputWriter.WriteToFileAsync(encodedBytes, outputImageFile);
                await moduleInfo.OutputWriter.WriteToFileAsync(
                    JsonSerializer.SerializeToUtf8Bytes(output, JsonOptions),
                    "Output.txt");
            }
            finally
            {
                foreach (var point in points)
                {
                    if (point is not null)
                    {
                        try { await point.DeleteAsync(); } catch { }
                    }
                }

                foreach (var channel in channels)
                {
                    if (channel is not null)
                    {
                        try { channel.Dispose(); } catch { }
                    }
                }
            }
        }
    }
}
