using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace Parcs.Modules.KMeansSegmentation.Models
{
    internal static class EmbeddedAssemblyResolver
    {
        private static int _registered;

        [ModuleInitializer]
        internal static void Register()
        {
            if (Interlocked.Exchange(ref _registered, 1) != 0)
            {
                return;
            }

            var currentAssembly = typeof(EmbeddedAssemblyResolver).Assembly;
            var alc = AssemblyLoadContext.GetLoadContext(currentAssembly) ?? AssemblyLoadContext.Default;

            alc.Resolving += (context, assemblyName) =>
            {
                if (string.Equals(assemblyName.Name, "SixLabors.ImageSharp", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = currentAssembly.GetManifestResourceStream("SixLabors.ImageSharp.dll");
                    if (stream is not null)
                    {
                        return context.LoadFromStream(stream);
                    }
                }
                return null;
            };
        }
    }

    /// <summary>
    /// Image reader and writer supporting JPG (.jpg/.jpeg) and PNG (.png) via embedded ImageSharp.
    /// </summary>
    public static class ImageCodec
    {
        public static bool IsSupportedFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".png" or ".jpg" or ".jpeg";
        }

        public static RawImage LoadFromStream(Stream stream, string fileNameHint = null)
        {
            if (!string.IsNullOrWhiteSpace(fileNameHint) && !IsSupportedFileName(fileNameHint))
            {
                throw new NotSupportedException(
                    $"Unsupported image extension '{Path.GetExtension(fileNameHint)}'. Only .jpg/.jpeg and .png are supported.");
            }

            using var image = Image.Load<Rgb24>(stream);
            var rgb = new byte[image.Width * image.Height * 3];
            image.CopyPixelDataTo(rgb);
            return new RawImage(image.Width, image.Height, rgb);
        }

        public static byte[] Encode(RawImage rawImage, string outputFileName)
        {
            var ext = Path.GetExtension(outputFileName ?? ".png").ToLowerInvariant();
            if (string.IsNullOrEmpty(ext))
            {
                ext = ".png";
            }

            if (ext is not (".png" or ".jpg" or ".jpeg"))
            {
                throw new NotSupportedException(
                    $"Unsupported output image format '{ext}'. Only .jpg/.jpeg and .png are supported.");
            }

            using var image = Image.LoadPixelData<Rgb24>(rawImage.RgbBytes, rawImage.Width, rawImage.Height);
            using var ms = new MemoryStream();

            if (ext is ".jpg" or ".jpeg")
            {
                image.Save(ms, new JpegEncoder { Quality = 95 });
            }
            else
            {
                image.Save(ms, new PngEncoder());
            }

            return ms.ToArray();
        }
    }
}
