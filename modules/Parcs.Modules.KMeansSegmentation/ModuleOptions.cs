using Parcs.Net;

namespace Parcs.Modules.KMeansSegmentation
{
    public class ModuleOptions : IModuleOptions
    {
        public int PointsNumber { get; set; } = 1;

        public int ClustersK { get; set; } = 8;

        public int MaxIterations { get; set; } = 40;

        public double ConvergenceThreshold { get; set; } = 0.5;

        public bool UseSpatialFeatures { get; set; } = true;

        public double SpatialWeight { get; set; } = 0.35;

        public int Seed { get; set; } = 42;

        public void Validate(int totalPixels, bool useWorkers)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(MaxIterations, 1);
            if (!double.IsFinite(ConvergenceThreshold) || ConvergenceThreshold < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ConvergenceThreshold), "ConvergenceThreshold must be finite and non-negative.");
            }

            if (!double.IsFinite(SpatialWeight) || SpatialWeight < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(SpatialWeight), "SpatialWeight must be finite and non-negative.");
            }

            if (useWorkers && (PointsNumber < 1 || PointsNumber > totalPixels))
            {
                throw new ArgumentOutOfRangeException(nameof(PointsNumber), $"PointsNumber must be between 1 and {totalPixels}.");
            }
        }
    }
}
