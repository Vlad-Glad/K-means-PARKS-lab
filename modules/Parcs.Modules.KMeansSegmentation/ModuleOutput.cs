namespace Parcs.Modules.KMeansSegmentation
{
    public class ModuleOutput
    {
        public double ElapsedSeconds { get; set; }

        public int IterationsPerformed { get; set; }

        public bool Converged { get; set; }

        public int ImageWidth { get; set; }

        public int ImageHeight { get; set; }

        public int ClustersK { get; set; }

        public int PointsNumber { get; set; }

        public bool UseSpatialFeatures { get; set; }
    }
}
