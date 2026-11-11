using Parcs.Modules.KMeansSegmentation.Models;
using Parcs.Net;

namespace Parcs.Modules.KMeansSegmentation.Parallel
{
    /// <summary>
    /// PARCS Worker module executing on an isolated daemon VM (1 point per VM).
    /// Receives its contiguous slice of pixels once, then loops over K-Means iterations
    /// in either 3D (R,G,B) or 5D (R,G,B,X,Y) feature space.
    /// </summary>
    public class ParallelKMeansWorkerModule : IModule
    {
        public async Task RunAsync(IModuleInfo moduleInfo, CancellationToken cancellationToken = default)
        {
            var channel = moduleInfo.Parent;

            int startPixelIndex = await channel.ReadIntAsync();
            int pixelCount = await channel.ReadIntAsync();
            int imageWidth = await channel.ReadIntAsync();
            int imageHeight = await channel.ReadIntAsync();
            bool useSpatialFeatures = await channel.ReadBooleanAsync();
            double spatialWeight = await channel.ReadDoubleAsync();
            byte[] chunkRgb = await channel.ReadBytesAsync();

            var labels = new byte[pixelCount];

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool continueIterating = await channel.ReadBooleanAsync();
                if (!continueIterating)
                {
                    break;
                }

                var centroids = await channel.ReadObjectAsync<CentroidSetDto>();

                var partialSums = KMeansEngine.AssignChunkAndAccumulate(
                    chunkRgb,
                    startPixelIndex,
                    pixelCount,
                    imageWidth,
                    imageHeight,
                    useSpatialFeatures,
                    spatialWeight,
                    centroids,
                    labels);

                await channel.WriteObjectAsync(partialSums);
            }

            var finalCentroids = await channel.ReadObjectAsync<CentroidSetDto>();
            KMeansEngine.AssignLabels(chunkRgb, startPixelIndex, pixelCount, imageWidth, imageHeight,
                useSpatialFeatures, spatialWeight, finalCentroids, labels);
            await channel.WriteDataAsync(labels);
        }
    }
}
