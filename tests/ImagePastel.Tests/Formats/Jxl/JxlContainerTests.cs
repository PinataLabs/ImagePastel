// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>Where an ignored box sits relative to the codestream.</summary>
    public enum BoxPosition
    {
        /// <summary>Before the <c>jxlc</c> box.</summary>
        Before,

        /// <summary>After the <c>jxlc</c> box.</summary>
        After,

        /// <summary>Between two <c>jxlp</c> boxes.</summary>
        Between,
    }

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

    [Fact]
    public void Load_JxlpBoxes_DecodesLikeTheBareCodestream()
    {
        byte[][] parts = Split(Codestream, 3);
        byte[] container = Container(Jxlp(0, parts[0]), Box("Exif", new byte[12]), Jxlp(1, parts[1]), Jxlp(2, parts[2], last: true));

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    [Fact]
    public void Load_JxlpBoxesOutOfOrder_InFileFormatVersion1_DecodesLikeTheBareCodestream()
    {
        byte[][] parts = Split(Codestream, 3);
        byte[] container = Container(1, Jxlp(2, parts[2], last: true), Jxlp(0, parts[0]), Jxlp(1, parts[1]));

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    [Fact]
    public void Load_JxlcAfterJxlpThatIsNotLast_JoinsThem()
    {
        // libjxl accepts this: a jxlc box only has to be the only jxlc box and come before the last jxlp.
        byte[][] parts = Split(Codestream, 2);
        byte[] container = Container(Jxlp(0, parts[0]), Box("jxlc", parts[1]));

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);

        Assert.Equal(555, image.Width);
    }

    [Theory]
    [InlineData(new byte[] { 0, 0, 0, 0 })] // too short for a box header
    [InlineData(new byte[] { 0, 0, 1, 0, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0 })] // a box cut off
    public void Load_BrokenBytesAfterTheLastCodestreamBox_DecodesTheImage(byte[] trailer)
    {
        // libjxl stops reading once the codestream is done, so it never sees them.
        List<byte> data = new(Container(Box("jxlc", Codestream)));
        data.AddRange(trailer);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), data.ToArray());

        Assert.Equal(555, image.Width);
    }

    [Fact]
    public void Decode_JxlpOutOfOrder_InFileFormatVersion0_ThrowsInvalidImageContent()
    {
        byte[][] parts = Split(Codestream, 2);

        Assert.Throws<InvalidImageContentException>(() => Decode(Container(Jxlp(1, parts[1], last: true), Jxlp(0, parts[0]))));
    }

    [Theory]
    [InlineData(0)] // the same index twice, in order
    [InlineData(1)] // the same index twice, both buffered out of order
    public void Decode_DuplicateJxlpIndex_ThrowsInvalidImageContent(int version)
    {
        byte[][] parts = Split(Codestream, 2);
        byte[] container = version == 0
            ? Container(Jxlp(0, parts[0]), Jxlp(0, parts[0]), Jxlp(1, parts[1], last: true))
            : Container(1, Jxlp(1, parts[1], last: true), Jxlp(1, parts[1], last: true), Jxlp(0, parts[0]));

        Assert.Throws<InvalidImageContentException>(() => Decode(container));
    }

    [Theory]
    [InlineData("jxlp")] // after the last jxlp
    [InlineData("jxlc")] // a second codestream box
    public void Decode_CodestreamBoxAfterTheLast_ThrowsInvalidImageContent(string type)
    {
        byte[] extra = type == "jxlp" ? Jxlp(1, new byte[4]) : Box("jxlc", new byte[4]);

        Assert.Throws<InvalidImageContentException>(() => Decode(Container(Jxlp(0, Codestream, last: true), extra)));
    }

    [Fact]
    public void Decode_JxlpTooSmallForItsIndex_ThrowsInvalidImageContent()
        => Assert.Throws<InvalidImageContentException>(() => Decode(Container(Box("jxlp", new byte[3]), Box("jxlc", Codestream))));

    [Fact]
    public void Decode_CodestreamOnlyInABrobBox_ThrowsInvalidImageContent()
    {
        // libjxl skips brob boxes when it looks for the codestream, so this file has none.
        byte[] contents = new byte[4 + Codestream.Length];
        Encoding.ASCII.GetBytes("jxlc").CopyTo(contents, 0);
        Codestream.CopyTo(contents, 4);

        Assert.Throws<InvalidImageContentException>(() => Decode(Container(Box("brob", contents))));
    }

    [Fact]
    public void Identify_64BitBoxSize_ReadsTheCodestream()
    {
        // libjxl's test file: an 8x8 lossy image in a jxlc box with a 64-bit size.
        string path = Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "jxl", "boxes", "square-extended-size-container.jxl");

        IImageInfo info = Image.Identify(CreateConfiguration(), path, out _);

        Assert.Equal(8, info.Width);
        Assert.Equal(8, info.Height);
    }

    [Fact]
    public void Load_64BitBoxSize_DecodesLikeTheBareCodestream()
    {
        byte[] box = new byte[16 + Codestream.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes("jxlc").CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), (ulong)box.Length);
        Codestream.CopyTo(box, 16);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), Container(box));
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    [Fact]
    public void Decode_64BitBoxSizePastTheEnd_ThrowsInvalidImageContent()
    {
        byte[] box = new byte[16 + 4];
        BinaryPrimitives.WriteUInt32BigEndian(box, 1);
        Encoding.ASCII.GetBytes("Exif").CopyTo(box, 4);
        BinaryPrimitives.WriteUInt64BigEndian(box.AsSpan(8), ulong.MaxValue);

        Assert.Throws<InvalidImageContentException>(() => Decode(Container(box, Box("jxlc", Codestream))));
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

    // libjxl reads a jbrd box only when the caller asks for JPEG reconstruction, which is out of scope,
    // and has no use for jxll or a type it does not know; so none of them changes the decoded image,
    // wherever it sits.
    [Theory]
    [InlineData("jbrd", BoxPosition.Before)]
    [InlineData("jbrd", BoxPosition.After)]
    [InlineData("jbrd", BoxPosition.Between)]
    [InlineData("jxll", BoxPosition.Before)]
    [InlineData("jxll", BoxPosition.After)]
    [InlineData("jxll", BoxPosition.Between)]
    [InlineData("zZz0", BoxPosition.Before)]
    [InlineData("zZz0", BoxPosition.After)]
    [InlineData("zZz0", BoxPosition.Between)]
    public void Load_IgnoredBox_DecodesLikeTheBareCodestream(string type, BoxPosition position)
    {
        byte[] container = WithIgnoredBox(Box(type, IgnoredBoxContents(type)), position);

        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), container);
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    [Theory]
    [InlineData("jbrd", BoxPosition.Before)]
    [InlineData("jxll", BoxPosition.Between)]
    [InlineData("zZz0", BoxPosition.After)]
    public void Identify_IgnoredBox_IdentifiesLikeTheBareCodestream(string type, BoxPosition position)
    {
        using MemoryStream stream = new(WithIgnoredBox(Box(type, IgnoredBoxContents(type)), position));
        using MemoryStream bare = new(Codestream);

        IImageInfo info = Image.Identify(CreateConfiguration(), stream);
        IImageInfo expected = Image.Identify(CreateConfiguration(), bare);

        Assert.Equal(expected.Width, info.Width);
        Assert.Equal(expected.Height, info.Height);
        Assert.Equal(expected.PixelType.BitsPerPixel, info.PixelType.BitsPerPixel);
        Assert.Null(info.Metadata.ExifProfile);
        Assert.Null(info.Metadata.XmpProfile);
    }

    [Fact]
    public void Load_EveryIgnoredBoxEverywhere_DecodesLikeTheBareCodestream()
    {
        using Image<Rgb24> image = Image.Load<Rgb24>(CreateConfiguration(), EveryIgnoredBoxEverywhere());
        using Image<Rgb24> expected = Image.Load<Rgb24>(CreateConfiguration(), Codestream);

        ImageComparer.Exact.VerifySimilarity(expected, image);
    }

    // djxl takes the same container, so the boxes are ones a conforming decoder ignores.
    [Fact]
    public async Task Djxl_EveryIgnoredBoxEverywhere_Decodes()
    {
        string directory = TestEnvironment.CreateOutputDirectory("Jxl", nameof(JxlContainerTests));
        string input = Path.Combine(directory, "every_ignored_box.jxl");
        await File.WriteAllBytesAsync(input, EveryIgnoredBoxEverywhere(), TestContext.Current.CancellationToken);

        await JxlTools.RunAsync("djxl", TestContext.Current.CancellationToken, input, Path.ChangeExtension(input, ".png"));
    }

    // jxll, jbrd and an unknown box before, between and after two jxlp boxes.
    private static byte[] EveryIgnoredBoxEverywhere()
    {
        byte[][] parts = Split(Codestream, 2);
        return Container(
            Box("jxll", IgnoredBoxContents("jxll")),
            Box("jbrd", IgnoredBoxContents("jbrd")),
            Box("zZz0", IgnoredBoxContents("zZz0")),
            Jxlp(0, parts[0]),
            Box("jbrd", IgnoredBoxContents("jbrd")),
            Box("zZz0", IgnoredBoxContents("zZz0")),
            Jxlp(1, parts[1], last: true),
            Box("jxll", IgnoredBoxContents("jxll")),
            Box("jbrd", IgnoredBoxContents("jbrd")),
            Box("zZz0", IgnoredBoxContents("zZz0")));
    }

    // jxll holds the level, one byte; jbrd and the unknown box get bytes that no reader could take for
    // a JPEG bitstream reconstruction or a codestream.
    private static byte[] IgnoredBoxContents(string type)
        => type == "jxll" ? new byte[] { 10 } : new byte[] { 0xFF, 0x0A, 0xDE, 0xAD, 0xBE, 0xEF, 0x00, 0x01 };

    // delta_palette's codestream with the box before its jxlc box, after it, or between two jxlp boxes.
    private static byte[] WithIgnoredBox(byte[] box, BoxPosition position)
    {
        byte[][] parts = Split(Codestream, 2);
        return position switch
        {
            BoxPosition.Before => Container(box, Box("jxlc", Codestream)),
            BoxPosition.After => Container(Box("jxlc", Codestream), box),
            BoxPosition.Between => Container(Jxlp(0, parts[0]), box, Jxlp(1, parts[1], last: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(position), position, null),
        };
    }

    private static void Decode(byte[] data)
    {
        using MemoryStream stream = new(data);
        using Image<Rgb24> image = new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, CancellationToken.None);
    }

    // The signature box, the file type box, then the boxes.
    private static byte[] Container(params byte[][] boxes) => Container(0, boxes);

    private static byte[] Container(uint version, params byte[][] boxes)
    {
        List<byte> data = new(JxlConstants.ContainerSignature);
        data.AddRange(Ftyp("jxl ", version));
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

    // A partial codestream box: its index, with the top bit set on the last box, then the bytes.
    private static byte[] Jxlp(uint index, byte[] part, bool last = false)
    {
        byte[] contents = new byte[4 + part.Length];
        BinaryPrimitives.WriteUInt32BigEndian(contents, index | (last ? 0x80000000u : 0u));
        part.CopyTo(contents, 4);
        return Box("jxlp", contents);
    }

    private static byte[][] Split(byte[] data, int count)
    {
        byte[][] parts = new byte[count][];
        int start = 0;
        for (int i = 0; i < count; i++)
        {
            int end = (int)((long)data.Length * (i + 1) / count);
            parts[i] = data.AsSpan(start, end - start).ToArray();
            start = end;
        }

        return parts;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }
}
