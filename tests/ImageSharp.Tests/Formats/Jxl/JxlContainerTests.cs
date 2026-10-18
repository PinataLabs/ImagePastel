// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Tests.Formats.Jxl.Conformance;
using SixLabors.ImageSharp.Tests.TestUtilities.ImageComparison;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Checks the container read: <c>delta_palette</c>'s bare codestream, wrapped in boxes.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlContainerTests
{
    private static readonly byte[] Codestream = File.ReadAllBytes(JxlConformanceVector.Load("delta_palette").CommittedInputPath);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Load_OneJxlcBox_DecodesLikeTheBareCodestream(bool unbounded)
    {
        byte[] container = Container(Box("jxll", new byte[] { 10 }), Box("jxlc", Codestream, unbounded));

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    [Fact]
    public void Load_SkipsUnknownBoxesBeforeTheCodestream()
    {
        byte[] container = Container(Box("Exif", new byte[12]), Box("xml ", new byte[3]), Box("jxlc", Codestream));

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);

        Assert.Equal(555, image.Width);
    }

    [Fact]
    public void Identify_ReadsTheCodestreamInTheContainer()
    {
        using MemoryStream stream = new(Container(Box("jxlc", Codestream)));
        IImageInfo info = Image.Identify(CreateConfiguration(), stream);

        Assert.Equal(555, info.Width);
        Assert.Equal(751, info.Height);
    }

    [Theory]
    [InlineData("jxlp")]
    [InlineData("brob")]
    public void Decode_UnsupportedBox_ThrowsNotSupported(string type)
    {
        byte[] container = Container(Box(type, new byte[8]), Box("jxlc", Codestream));

        Assert.Throws<NotSupportedException>(() => Decode(container));
    }

    [Fact]
    public void Decode_64BitBoxSize_ThrowsNotSupported()
    {
        byte[] box = new byte[16 + Codestream.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes("jxlc").CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        Codestream.CopyTo(box, 16);

        Assert.Throws<NotSupportedException>(() => Decode(Container(box)));
    }

    [Theory]
    [InlineData(7)] // smaller than its header
    [InlineData(100)] // past the end of the file
    public void Decode_BadBoxSize_ThrowsInvalidImageContent(uint size)
    {
        byte[] box = Box("Exif", new byte[12]);
        BinaryPrimitives.WriteUInt32BigEndian(box, size);

        Assert.Throws<InvalidImageContentException>(() => Decode(Container(box)));
    }

    [Fact]
    public void Decode_TruncatedBoxHeader_ThrowsInvalidImageContent()
        => Assert.Throws<InvalidImageContentException>(() => Decode(Container(new byte[] { 0, 0, 0 })));

    [Fact]
    public void Decode_NoCodestream_ThrowsInvalidImageContent()
        => Assert.Throws<InvalidImageContentException>(() => Decode(Container(Box("Exif", new byte[12]))));

    [Fact]
    public void Decode_SecondBoxIsNotFtyp_ThrowsInvalidImageContent()
    {
        List<byte> data = new(JxlConstants.ContainerSignature);
        data.AddRange(Box("jxlc", Codestream));

        Assert.Throws<InvalidImageContentException>(() => Decode(data.ToArray()));
    }

    [Theory]
    [InlineData("jxl ", 2u)] // unknown version
    [InlineData("jpx ", 0u)] // wrong major brand
    public void Decode_BadFtyp_ThrowsInvalidImageContent(string brand, uint version)
    {
        List<byte> data = new(JxlConstants.ContainerSignature);
        data.AddRange(Ftyp(brand, version));
        data.AddRange(Box("jxlc", Codestream));

        Assert.Throws<InvalidImageContentException>(() => Decode(data.ToArray()));
    }

    private static void Decode(byte[] data)
    {
        using MemoryStream stream = new(data);
        using Image<Rgb24> image = new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);
    }

    // The signature box, the file type box, then the boxes.
    private static byte[] Container(params byte[][] boxes)
    {
        List<byte> data = new(JxlConstants.ContainerSignature);
        data.AddRange(Ftyp("jxl ", 0));
        foreach (byte[] box in boxes)
        {
            data.AddRange(box);
        }

        return data.ToArray();
    }

    private static byte[] Ftyp(string brand, uint version)
    {
        byte[] contents = new byte[12];
        Encoding.ASCII.GetBytes(brand).CopyTo(contents, 0);
        BinaryPrimitives.WriteUInt32BigEndian(contents.AsSpan(4), version);
        Encoding.ASCII.GetBytes("jxl ").CopyTo(contents, 8);
        return Box("ftyp", contents);
    }

    // A box with a 32-bit size, or size 0 for a box that runs to the end of the file.
    private static byte[] Box(string type, byte[] contents, bool unbounded = false)
    {
        byte[] box = new byte[8 + contents.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, unbounded ? 0u : (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        contents.CopyTo(box, 8);
        return box;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
