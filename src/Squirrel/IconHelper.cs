using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace Squirrel
{
    /// <summary>
    /// Provides helper methods for imaging
    /// </summary>
    public static class IconHelper
    {
        /// <summary>
        /// Converts a PNG image to a icon (ico)
        /// </summary>
        /// <param name="input">The input stream</param>
        /// <param name="output">The output stream</param>
        /// <param name="size">Needs to be a factor of 2 (16x16 px by default)</param>
        /// <param name="preserveAspectRatio">Preserve the aspect ratio</param>
        public static void ConvertToIcon(Stream input, Stream output, int size = 16, bool preserveAspectRatio = false)
        {
            var inputBitmap = (Bitmap) Image.FromStream(input);

            float width = size, height = size;
            if (preserveAspectRatio)
            {
                if (inputBitmap.Width > inputBitmap.Height)
                    height = (float)inputBitmap.Height / inputBitmap.Width * size;
                else
                    width = (float)inputBitmap.Width / inputBitmap.Height * size;
            }

            var newBitmap = new Bitmap(inputBitmap, new Size((int)width, (int)height));

            // save the resized png into a memory stream for future use
            using (var memoryStream = new MemoryStream())
            {
                newBitmap.Save(memoryStream, ImageFormat.Png);

                var iconWriter = new BinaryWriter(output);

                // 0-1 reserved, 0
                iconWriter.Write((byte)0);
                iconWriter.Write((byte)0);

                // 2-3 image type, 1 = icon, 2 = cursor
                iconWriter.Write((short)1);

                // 4-5 number of images
                iconWriter.Write((short)1);

                // image entry 1
                // 0 image width
                iconWriter.Write((byte)width);
                // 1 image height
                iconWriter.Write((byte)height);

                // 2 number of colors
                iconWriter.Write((byte)0);

                // 3 reserved
                iconWriter.Write((byte)0);

                // 4-5 color planes
                iconWriter.Write((short)0);

                // 6-7 bits per pixel
                iconWriter.Write((short)32);

                // 8-11 size of image data
                iconWriter.Write((int)memoryStream.Length);

                // 12-15 offset of image data
                iconWriter.Write(6 + 16);

                // write image data
                // png data must contain the whole png data file
                iconWriter.Write(memoryStream.ToArray());

                iconWriter.Flush();
            }
        }
    }
}