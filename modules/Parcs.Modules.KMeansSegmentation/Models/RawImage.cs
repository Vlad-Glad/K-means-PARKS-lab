namespace Parcs.Modules.KMeansSegmentation.Models
{
    /// <summary>
    /// Contiguous 24-bit RGB image buffer in row-major order (3 bytes per pixel: R, G, B).
    /// </summary>
    public sealed class RawImage
    {
        public int Width { get; }

        public int Height { get; }

        public int TotalPixels => Width * Height;

        /// <summary>
        /// Length = Width * Height * 3. Pixel i has R = RgbBytes[3*i], G = RgbBytes[3*i+1], B = RgbBytes[3*i+2].
        /// </summary>
        public byte[] RgbBytes { get; }

        public RawImage(int width, int height, byte[] rgbBytes)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
            ArgumentNullException.ThrowIfNull(rgbBytes);
            if (rgbBytes.Length != width * height * 3)
            {
                throw new ArgumentException($"Expected {width * height * 3} RGB bytes, got {rgbBytes.Length}.");
            }

            Width = width;
            Height = height;
            RgbBytes = rgbBytes;
        }
    }
}
