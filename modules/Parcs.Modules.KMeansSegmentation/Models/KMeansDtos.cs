namespace Parcs.Modules.KMeansSegmentation.Models
{
    public sealed class CentroidSetDto
    {
        public int K { get; set; }
        public double[] R { get; set; } = [];
        public double[] G { get; set; } = [];
        public double[] B { get; set; } = [];
        public double[] X { get; set; } = [];
        public double[] Y { get; set; } = [];
    }

    public sealed class PartialAccumulatorDto
    {
        public int K { get; set; }
        public double[] SumR { get; set; } = [];
        public double[] SumG { get; set; } = [];
        public double[] SumB { get; set; } = [];
        public double[] SumX { get; set; } = [];
        public double[] SumY { get; set; } = [];
        public long[] Count { get; set; } = [];
    }
}
