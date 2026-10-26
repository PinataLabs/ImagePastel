// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Numerics;
using SixLabors.ImageSharp.ColorManagement;

namespace SixLabors.ImageSharp.Formats.Jpeg.Components.Decoder.ColorConverters;

internal abstract partial class JpegColorConverterBase
{
    /// <summary>
    /// Converts Adobe CMYK or YCCK samples through the embedded CMYK profile, instead of through the
    /// CMYK-to-RGB formula of <see cref="FromCmykScalar"/> and <see cref="FromYccKScalar"/>.
    /// </summary>
    /// <remarks>
    /// Adobe stores CMYK inverted: a sample of 0 is full ink. YCCK holds the inverted cyan, magenta
    /// and yellow samples as YCbCr, so the YCbCr-to-RGB transform gives the ink amounts directly,
    /// while black is stored inverted as in CMYK. The ink amounts (0 = no ink, 1 = full ink) are what
    /// an ICC CMYK profile takes.
    /// </remarks>
    internal sealed class FromCmykIcc : JpegColorConverterBase
    {
        // Pixels converted per call to the transform, so the ink buffer fits on the stack.
        private const int ChunkLength = 64;

        private readonly IccColorTransform transform;

        /// <summary>
        /// Initializes a new instance of the <see cref="FromCmykIcc"/> class.
        /// </summary>
        /// <param name="colorSpace">The color space of the samples: <see cref="JpegColorSpace.Cmyk"/> or <see cref="JpegColorSpace.Ycck"/>.</param>
        /// <param name="precision">The precision of the samples in bits.</param>
        /// <param name="transform">The transform from the embedded CMYK profile to RGB.</param>
        public FromCmykIcc(JpegColorSpace colorSpace, int precision, IccColorTransform transform)
            : base(colorSpace, precision)
        {
            DebugGuard.IsTrue(colorSpace is JpegColorSpace.Cmyk or JpegColorSpace.Ycck, nameof(colorSpace), "The color space must be CMYK or YCCK.");
            this.transform = transform;
        }

        /// <inheritdoc/>
        public override bool IsAvailable => true;

        /// <inheritdoc/>
        public override void ConvertToRgbInplace(in ComponentValues values)
        {
            Span<float> c0 = values.Component0;
            Span<float> c1 = values.Component1;
            Span<float> c2 = values.Component2;
            bool ycck = this.ColorSpace == JpegColorSpace.Ycck;

            Span<Vector4> buffer = stackalloc Vector4[ChunkLength];
            for (int start = 0; start < c0.Length; start += ChunkLength)
            {
                Span<Vector4> inks = buffer.Slice(0, Math.Min(ChunkLength, c0.Length - start));
                if (ycck)
                {
                    this.ReadYccK(values, start, inks);
                }
                else
                {
                    this.ReadCmyk(values, start, inks);
                }

                IccPixelConversion.ConvertRow(this.transform, inks);

                for (int i = 0; i < inks.Length; i++)
                {
                    Vector4 rgb = inks[i];
                    c0[start + i] = rgb.X;
                    c1[start + i] = rgb.Y;
                    c2[start + i] = rgb.Z;
                }
            }
        }

        private void ReadCmyk(in ComponentValues values, int start, Span<Vector4> inks)
        {
            float scale = 1F / this.MaximumValue;
            for (int i = 0; i < inks.Length; i++)
            {
                int x = start + i;
                inks[i] = new Vector4(
                    1F - (values.Component0[x] * scale),
                    1F - (values.Component1[x] * scale),
                    1F - (values.Component2[x] * scale),
                    1F - (values.Component3[x] * scale));
            }
        }

        private void ReadYccK(in ComponentValues values, int start, Span<Vector4> inks)
        {
            // The same YCbCr-to-RGB transform, rounding and range limit as FromYccKScalar and libjpeg.
            float max = this.MaximumValue;
            float half = this.HalfValue;
            float scale = 1F / max;
            for (int i = 0; i < inks.Length; i++)
            {
                int x = start + i;
                float y = values.Component0[x];
                float cb = values.Component1[x] - half;
                float cr = values.Component2[x] - half;
                inks[i] = new Vector4(
                    Numerics.Clamp(MathF.Round(y + (1.402F * cr), MidpointRounding.AwayFromZero), 0, max) * scale,
                    Numerics.Clamp(MathF.Round(y - (0.344136F * cb) - (0.714136F * cr), MidpointRounding.AwayFromZero), 0, max) * scale,
                    Numerics.Clamp(MathF.Round(y + (1.772F * cb), MidpointRounding.AwayFromZero), 0, max) * scale,
                    1F - (values.Component3[x] * scale));
            }
        }
    }
}
