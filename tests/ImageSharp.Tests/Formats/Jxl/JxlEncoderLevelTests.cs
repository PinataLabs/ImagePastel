// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The level-10 container the encoder writes when the codestream needs level 10 (E6c, #333), checked
/// through <c>SaveAsJxl</c> with <see cref="Configuration.Default"/>.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderLevelTests
{
    [Fact]
    public void SaveAsJxl_LosslessGrey16_WritesAJxllContainer()
    {
        using Image<L16> image = JxlEncoderRoundTrip.CreateImage<L16>("gradient", 17, 23);

        AssertLevel10Container(Save(image, new JxlEncoder { Distance = 0 }));
    }

    [Fact]
    public void SaveAsJxl_LosslessRgba64_WritesAJxllContainer()
    {
        using Image<Rgba64> image = JxlEncoderRoundTrip.CreateImage<Rgba64>("gradient", 17, 23);

        AssertLevel10Container(Save(image, new JxlEncoder { Distance = 0 }));
    }

    // libjxl's level-10 layout without boxes: the signature, ftyp, jxll holding the level, then the whole
    // codestream in one jxlc box.
    private static void AssertLevel10Container(byte[] encoded)
    {
        IReadOnlyList<JxlBox> boxes = JxlBoxList.Read(encoded);

        Assert.Equal(new[] { "JXL ", "ftyp", "jxll", "jxlc" }, JxlBoxList.Types(boxes));
        Assert.Equal("jxl \0\0\0\0jxl "u8.ToArray(), boxes[1].Contents);
        Assert.Equal(new byte[] { 10 }, boxes[2].Contents);
        Assert.Equal(new byte[] { 0xFF, 0x0A }, boxes[3].Contents[..2]);
    }

    private static byte[] Save(Image image, JxlEncoder encoder)
    {
        using MemoryStream stream = new();
        image.SaveAsJxl(stream, encoder);
        return stream.ToArray();
    }
}
