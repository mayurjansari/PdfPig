﻿namespace UglyToad.PdfPig.Images.Png
{
    using Content;
    using Graphics.Colors;

    internal static class PngFromPdfImageFactory
    {
        public static bool TryGenerate(IPdfImage image, out byte[] bytes)
        {
            bytes = null;

            var hasValidDetails = image.ColorSpaceDetails != null &&
                                  !(image.ColorSpaceDetails is UnsupportedColorSpaceDetails);
            var actualColorSpace = hasValidDetails ? image.ColorSpaceDetails.BaseType : image.ColorSpace;

            var isColorSpaceSupported =
                actualColorSpace == ColorSpace.DeviceGray || actualColorSpace == ColorSpace.DeviceRGB
                || actualColorSpace == ColorSpace.DeviceCMYK;

            if (!isColorSpaceSupported || !image.TryGetBytes(out var bytesPure))
            {
                return false;
            }

            bytesPure = ColorSpaceDetailsByteConverter.Convert(image.ColorSpaceDetails, bytesPure);

            try
            {
                var is3Byte = actualColorSpace == ColorSpace.DeviceRGB || actualColorSpace == ColorSpace.DeviceCMYK;
                var multiplier = is3Byte ? 3 : 1;

                var builder = PngBuilder.Create(image.WidthInSamples, image.HeightInSamples, false);

                var isCorrectlySized = bytesPure.Count == (image.WidthInSamples * image.HeightInSamples * (image.BitsPerComponent / 8) * multiplier);

                if (!isCorrectlySized)
                {
                    return false;
                }

                var i = 0;
                for (var col = 0; col < image.HeightInSamples; col++)
                {
                    for (var row = 0; row < image.WidthInSamples; row++)
                    {
                        if (actualColorSpace == ColorSpace.DeviceCMYK)
                        {
                            /*
                             * Where CMYK in 0..1
                             * R = 255 × (1-C) × (1-K)
                             * G = 255 × (1-M) × (1-K)
                             * B = 255 × (1-Y) × (1-K)
                             */

                            var c = (bytesPure[i++]/255d);
                            var m = (bytesPure[i++]/255d);
                            var y = (bytesPure[i++]/255d);
                            var k = (bytesPure[i++]/255d);
                            var r = (byte)(255 * (1 - c) * (1 - k));
                            var g = (byte)(255 * (1 - m) * (1 - k));
                            var b = (byte)(255 * (1 - y) * (1 - k));

                            builder.SetPixel(r, g, b, row, col);
                        }
                        else if (is3Byte)
                        {
                            builder.SetPixel(bytesPure[i++], bytesPure[i++], bytesPure[i++], row, col);
                        }
                        else
                        {
                            var pixel = bytesPure[i++];
                            builder.SetPixel(pixel, pixel, pixel, row, col);
                        }
                    }
                }

                bytes = builder.Save();

                return true;
            }
            catch
            {
                // ignored.
            }

            return false;
        }
    }
}
