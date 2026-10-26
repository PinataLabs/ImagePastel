// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Frame;
using PinataLabs.ImagePastel.Formats.Jxl.VarDct;
using PinataLabs.ImagePastel.Memory;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// The encoder's reconstruction of its own frame from its coefficients (libjxl's <c>ReconstructImage</c>,
/// E5g), the prefactor of dynamic EPF (E5h). Nothing in the encode path calls it yet, so each test calls it
/// from the encoder's probe, where libjxl's <c>ComputeARHeuristics</c> would, and compares it with what our
/// decoder renders from the written file with the XYB stage off: the opsin planes after Gaborish and EPF.
/// The two share the decoder's dequantisation, transforms and loop filters, but one is fed from the
/// encoder's coefficients, DC and per-block choices, the other from the bitstream, so they agree bit for
/// bit only if the encoder holds exactly what it writes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncReconstructTests
{
    // Bike is odd-sized and has six groups; the flower crop has two groups and a partial block row; the
    // gradient strip has nine groups over two LF groups and a one-pixel partial block column.
    [Theory]
    [InlineData("bike-582x453", 1.0f)]
    [InlineData("bike-582x453", 4.0f)]
    [InlineData("flower-384x244", 1.0f)]
    [InlineData("flower-384x244", 4.0f)]
    [InlineData("gradient-2049x9", 1.0f)]
    public void ReconstructImage_DefaultEncode_EqualsTheDecodedOpsinPlanes(string sizeCase, float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using Reconstruction reconstruction = EncodeAndReconstruct(image, JxlEffort.Default, distance, setSharpness: null);

        // Gaborish and EPF are on at Default from d1.
        Assert.True(reconstruction.FrameHeader.LoopFilter.Gab);
        Assert.True(reconstruction.FrameHeader.LoopFilter.EpfIters > 0);
        AssertEqualsDecodedOpsin(reconstruction, $"{sizeCase} d{distance}");
    }

    // Below d0.7 there is no EPF, and at d0.5 no Gaborish either: the reconstruction is the inverse
    // transforms alone.
    [Fact]
    public void ReconstructImage_WithoutLoopFilters_EqualsTheDecodedOpsinPlanes()
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase("flower-384x244");

        using Reconstruction reconstruction = EncodeAndReconstruct(image, JxlEffort.Default, 0.5f, setSharpness: null);

        Assert.False(reconstruction.FrameHeader.LoopFilter.Gab);
        Assert.Equal(0u, reconstruction.FrameHeader.LoopFilter.EpfIters);
        AssertEqualsDecodedOpsin(reconstruction, "flower d0.5");
    }

    // The EPF sharpness the probe sets, which the file carries in its AC metadata, reaches the
    // reconstruction's EPF through ComputeSigma, as the sharpness E5h will try.
    [Theory]
    [InlineData("bike-582x453", 1.0f)]
    [InlineData("flower-384x244", 4.0f)]
    public void ReconstructImage_NonConstantEpfSharpness_EqualsTheDecodedOpsinPlanes(string sizeCase, float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using Reconstruction reconstruction = EncodeAndReconstruct(image, JxlEffort.Default, distance, SetPattern);

        using JxlDecodedDcGroups frame = JxlDecodedDcGroups.Decode(reconstruction.Encoded);
        int differing = 0;
        for (int by = 0; by < frame.State.YSizeBlocks; by++)
        {
            Span<byte> row = frame.State.EpfSharpness.DangerousGetRowSpan(by);
            for (int bx = 0; bx < frame.State.XSizeBlocks; bx++)
            {
                Assert.Equal(Pattern(bx, by), row[bx]);
                differing += row[bx] != 4 ? 1 : 0;
            }
        }

        Assert.True(differing > 0);
        AssertEqualsDecodedOpsin(reconstruction, $"{sizeCase} d{distance}, patterned sharpness");
    }

    // Fastest has no Gaborish, every block DCT8 and a constant quant field; EPF is on from d0.7.
    [Theory]
    [InlineData("bike-582x453", 1.0f)]
    [InlineData("flower-384x244", 4.0f)]
    public void ReconstructImage_FastestEncode_EqualsTheDecodedOpsinPlanes(string sizeCase, float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using Reconstruction reconstruction = EncodeAndReconstruct(image, JxlEffort.Fastest, distance, setSharpness: null);

        Assert.False(reconstruction.FrameHeader.LoopFilter.Gab);
        Assert.True(reconstruction.FrameHeader.LoopFilter.EpfIters > 0);
        AssertEqualsDecodedOpsin(reconstruction, $"{sizeCase} d{distance} Fastest");
    }

    // Thorough (kSquirrel), where E5h calls the reconstruction, with the patterned sharpness E5h's search
    // would try.
    [Theory]
    [InlineData("bike-582x453", 1.0f)]
    [InlineData("flower-384x244", 4.0f)]
    public void ReconstructImage_ThoroughEncode_EqualsTheDecodedOpsinPlanes(string sizeCase, float distance)
    {
        using Image<Rgb24> image = JxlLossyEncoderTests.LoadSizeCase(sizeCase);

        using Reconstruction reconstruction = EncodeAndReconstruct(image, JxlEffort.Thorough, distance, SetPattern);

        Assert.True(reconstruction.FrameHeader.LoopFilter.Gab);
        Assert.True(reconstruction.FrameHeader.LoopFilter.EpfIters > 0);
        AssertEqualsDecodedOpsin(reconstruction, $"{sizeCase} d{distance} Thorough");
    }

    // Every value 0 to 7, in a pattern that differs from a block's top and left neighbours.
    private static byte Pattern(int bx, int by) => (byte)(((bx * 3) + (by * 5)) % 8);

    private static void SetPattern(JxlPassesEncoderState encState)
    {
        for (int by = 0; by < encState.YSizeBlocks; by++)
        {
            for (int bx = 0; bx < encState.XSizeBlocks; bx++)
            {
                encState.EpfSharpness[encState.BlockIndex(bx, by)] = Pattern(bx, by);
            }
        }
    }

    // Encodes the image, calling ReconstructImage from the probe after setting the sharpness, if asked.
    private static Reconstruction EncodeAndReconstruct(Image<Rgb24> image, JxlEffort effort, float distance, Action<JxlPassesEncoderState> setSharpness)
    {
        Reconstruction result = new();
        JxlEncoder encoder = new()
        {
            Distance = distance,
            Effort = effort,
            TestHooks = new()
            {
                ArHeuristicsProbe = (frameHeader, encState) =>
                {
                    setSharpness?.Invoke(encState);
                    result.FrameHeader = frameHeader;
                    result.XSize = (int)encState.FrameDim.XSize;
                    result.YSize = (int)encState.FrameDim.YSize;
                    result.Planes = JxlEncReconstruct.ReconstructImage(frameHeader, encState, Configuration.Default.MemoryAllocator);
                },
            },
        };

        try
        {
            result.Encoded = JxlEncoderRoundTrip.Encode(image, encoder);
            Assert.NotNull(result.Planes);
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    // Our decoder's planes for the file with the XYB stage off, against the reconstruction, bit for bit
    // over the frame.
    private static void AssertEqualsDecodedOpsin(Reconstruction reconstruction, string label)
    {
        using MemoryStream stream = new(reconstruction.Encoded);
        using JxlDecodedImage decoded = new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken.None, skipColorTransform: true);
        Assert.Equal(reconstruction.XSize, decoded.Width);
        Assert.Equal(reconstruction.YSize, decoded.Height);
        JxlDecodedFrame frame = Assert.Single(decoded.Frames);
        Assert.Equal(3, frame.Planes.Count);
        Assert.Equal(3, reconstruction.Planes.Length);

        for (int c = 0; c < 3; c++)
        {
            int mismatches = 0;
            string first = null;
            for (int y = 0; y < reconstruction.YSize; y++)
            {
                Span<float> expected = frame.Planes[c].DangerousGetRowSpan(y);
                Span<float> actual = reconstruction.Planes[c].DangerousGetRowSpan(y);
                for (int x = 0; x < reconstruction.XSize; x++)
                {
                    if (BitConverter.SingleToInt32Bits(expected[x]) != BitConverter.SingleToInt32Bits(actual[x]))
                    {
                        mismatches++;
                        first ??= $"({x}, {y}): decoded {expected[x]:R}, reconstructed {actual[x]:R}";
                    }
                }
            }

            Assert.True(mismatches == 0, $"{label}, channel {c}: {mismatches} samples differ, first at {first}");
        }
    }

    private sealed class Reconstruction : IDisposable
    {
        public byte[] Encoded { get; set; }

        public JxlFrameHeader FrameHeader { get; set; }

        public Buffer2D<float>[] Planes { get; set; }

        public int XSize { get; set; }

        public int YSize { get; set; }

        public void Dispose()
        {
            if (this.Planes != null)
            {
                foreach (Buffer2D<float> plane in this.Planes)
                {
                    plane?.Dispose();
                }
            }
        }
    }
}
