﻿namespace UglyToad.PdfPig.Images
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Content;
    using Core;
    using Graphics.Colors;

    /// <summary>
    /// Utility for working with the bytes in <see cref="IPdfImage"/>s and converting according to their <see cref="ColorSpaceDetails"/>.s
    /// </summary>
    public static class ColorSpaceDetailsByteConverter
    {
        /// <summary>
        /// Converts the output bytes (if available) of <see cref="IPdfImage.TryGetBytes"/>
        /// to actual pixel values using the <see cref="IPdfImage.ColorSpaceDetails"/>. For most images this doesn't
        /// change the data but for <see cref="ColorSpace.Indexed"/> it will convert the bytes which are indexes into the
        /// real pixel data into the real pixel data.
        /// </summary>
        public static byte[] Convert(ColorSpaceDetails details, IReadOnlyList<byte> decoded)
        {
            if (decoded == null)
            {
                return EmptyArray<byte>.Instance;
            }

            if (details == null)
            {
                return decoded.ToArray();
            }

            switch (details)
            {
                case IndexedColorSpaceDetails indexed:
                    return UnwrapIndexedColorSpaceBytes(indexed, decoded);
            }

            return decoded.ToArray();
        }

        private static byte[] UnwrapIndexedColorSpaceBytes(IndexedColorSpaceDetails indexed, IReadOnlyList<byte> input)
        {
            var multiplier = 1;
            Func<byte, IEnumerable<byte>> transformer = null;
            switch (indexed.BaseColorSpaceDetails.Type)
            {
                case ColorSpace.DeviceRGB:
                    transformer = x =>
                    {
                        var r = new byte[3];
                        for (var i = 0; i < 3; i++)
                        {
                            r[i] = indexed.ColorTable[x * 3 + i];
                        }

                        return r;
                    };
                    multiplier = 3;
                    break;
                case ColorSpace.DeviceCMYK:
                    transformer = x =>
                    {
                        var r = new byte[4];
                        for (var i = 0; i < 4; i++)
                        {
                            r[i] = indexed.ColorTable[x * 4 + i];
                        }

                        return r;
                    };

                    multiplier = 4;
                    break;
                case ColorSpace.DeviceGray:
                    transformer = x => new[] { indexed.ColorTable[x] };
                    multiplier = 1;
                    break;
            }

            if (transformer != null)
            {
                var result = new byte[input.Count * multiplier];
                var i = 0;
                foreach (var b in input)
                {
                    foreach (var newByte in transformer(b))
                    {
                        result[i++] = newByte;
                    }
                }

                return result;
            }

            return input.ToArray();
        }
    }
}
