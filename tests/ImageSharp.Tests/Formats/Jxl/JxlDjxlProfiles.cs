// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Headers;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Tests.Formats.Png;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Reads the colour files of <c>Input/Jxl/Generated</c> and the profiles <c>djxl</c> built for them, which it
/// writes into the <c>iCCP</c> chunk of its PNGs.
/// </summary>
internal static class JxlDjxlProfiles
{
    /// <summary>The number of grid points per input of the 3D LUT in libjxl's PQ and HLG profiles.</summary>
    public const int LutGridPoints = 9;

    /// <summary>Returns the colour encoding in the header of <c>colour_<paramref name="name"/>.jxl</c>.</summary>
    /// <param name="name">The name after <c>colour_</c>.</param>
    /// <returns>The colour encoding.</returns>
    public static JxlColorEncodingHeader ColourEncoding(string name)
    {
        // The files are bare codestreams, so the header bundle is read straight from them.
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "colour_" + name + ".jxl");
        JxlColorEncodingHeader c = JxlCodestreamFrames.ReadColorEncoding(File.ReadAllBytes(path));
        Assert.False(c.WantIcc);
        return c;
    }

    /// <summary>
    /// Returns the profile <c>djxl</c> built for <c>colour_<paramref name="name"/>.jxl</c>, from the
    /// <c>iCCP</c> chunk of its PNG. For spaces that ITU-T H.273 names, <c>djxl</c> also writes a
    /// <c>cICP</c> chunk, which takes precedence over <c>iCCP</c> when the PNG decodes, so it is dropped.
    /// </summary>
    /// <param name="name">The name after <c>colour_</c>.</param>
    /// <returns>The profile.</returns>
    public static IccProfile Profile(string name)
    {
        string path = Path.Combine(TestEnvironment.ReferenceOutputDirectoryFullPath, "JxlDecoderTests", "colour_" + name + ".png");
        byte[][] chunks = PngTestFileBuilder.ReadChunks(File.ReadAllBytes(path))
            .Where(c => c.Type != "cICP")
            .Select(c => PngTestFileBuilder.Chunk(c.Type, c.Data))
            .ToArray();

        IccProfile profile = Image.Identify(PngTestFileBuilder.Build(chunks)).Metadata.IccProfile;
        Assert.NotNull(profile);
        return profile;
    }

    /// <summary>
    /// Returns the CLUT of the <c>A2B0</c> tag of a libjxl PQ or HLG profile: an <c>mft1</c> (lut8) table
    /// with three inputs, three outputs and <see cref="LutGridPoints"/> grid points, read from the profile's
    /// bytes. Node (ix, iy, ib) holds its three PCS Lab bytes at <c>((ix * 9 + iy) * 9 + ib) * 3</c>.
    /// </summary>
    /// <param name="profile">The profile.</param>
    /// <returns>The 9 × 9 × 9 × 3 CLUT bytes.</returns>
    public static byte[] A2B0Clut(IccProfile profile)
    {
        byte[] data = profile.ToByteArray();
        int tagCount = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(128));
        for (int i = 0; i < tagCount; i++)
        {
            int entry = 132 + (i * 12);
            if (Encoding.ASCII.GetString(data, entry, 4) != "A2B0")
            {
                continue;
            }

            int offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 4));
            Assert.Equal("mft1", Encoding.ASCII.GetString(data, offset, 4));
            Assert.Equal(3, data[offset + 8]);
            Assert.Equal(3, data[offset + 9]);
            Assert.Equal(LutGridPoints, data[offset + 10]);

            // The type, reserved bytes, the channel and grid counts and padding (12), the 3 × 3 matrix (36)
            // and three input tables of 256 bytes come before the CLUT.
            int clut = offset + 12 + 36 + (3 * 256);
            return data.AsSpan(clut, LutGridPoints * LutGridPoints * LutGridPoints * 3).ToArray();
        }

        Assert.Fail("The profile has no A2B0 tag.");
        return null;
    }
}
