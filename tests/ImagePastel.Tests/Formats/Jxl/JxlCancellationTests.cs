// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.IO.Compression;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Formats.Jxl.Base;
using PinataLabs.ImagePastel.Formats.Jxl.Fields;
using PinataLabs.ImagePastel.Formats.Jxl.Icc;
using PinataLabs.ImagePastel.Formats.Jxl.Modular;
using PinataLabs.ImagePastel.Formats.Jxl.Modular.Transforms;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// A cancelled token stops a decode or an identify with <see cref="OperationCanceledException"/> at
/// each long-running site (issue #293). The sites below the frame loop are driven directly, as the
/// frame loop's own check would otherwise stop a cancelled decode first.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlCancellationTests
{
    [Fact]
    public void Identify_CancelledToken_Throws()
    {
        using MemoryStream stream = new(EncodeGradient(16, 16));

        Assert.ThrowsAny<OperationCanceledException>(() => new JxlDecoder().Identify(Configuration.Default, stream, Cancelled()));
    }

    // A 75 KB profile: its decode passes many of the reader's 512-symbol checkpoints.
    [Fact]
    public void IccDecode_CancelledToken_Throws()
    {
        byte[] icc = File.ReadAllBytes(TestFile.GetInputFileFullPath(TestImages.Icc.LittleCmsRgbLut16V2));
        JxlBitWriter writer = new();
        JxlIccWriter.WriteIcc(icc, writer);
        writer.ZeroPadToByte();
        byte[] encoded = writer.ToArray();

        Assert.Equal(icc, JxlIccReader.Read(new JxlBitReader(encoded), CancellationToken.None));
        Assert.ThrowsAny<OperationCanceledException>(() => JxlIccReader.Read(new JxlBitReader(encoded), Cancelled()));
    }

    [Fact]
    public void Decode_CancelledToken_Throws()
    {
        using MemoryStream stream = new(EncodeGradient(16, 16));

        Assert.ThrowsAny<OperationCanceledException>(() => new JxlDecoder().Decode<Rgb24>(Configuration.Default, stream, Cancelled()));
    }

    // A frame of one group: the global Modular stream holds every channel of the image.
    [Fact]
    public void GlobalModularDecode_CancelledToken_Throws()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", 256, 256);
        byte[] codestream = JxlEncoderRoundTrip.Encode(image);

        JxlEncodedFrame.ReadGlobalInfo(codestream, CancellationToken.None).Dispose();
        Assert.ThrowsAny<OperationCanceledException>(() => JxlEncodedFrame.ReadGlobalInfo(codestream, Cancelled()).Dispose());
    }

    // The stream ends after its group header, so the decode fails once the transforms are applied,
    // when it reads the tree; a cancelled token stops it before that.
    [Fact]
    public void ModularDecode_CancelledToken_ThrowsBeforeTheTreeIsRead()
    {
        JxlGroupHeader header = new();
        header.Transforms.Add(new JxlTransform { Id = JxlTransformId.Squeeze });
        JxlBitWriter writer = new();
        JxlBundle.Write(header, writer);
        writer.ZeroPadToByte();
        byte[] stream = writer.ToArray();

        Assert.Throws<InvalidImageContentException>(() => Decompress(stream, CancellationToken.None));
        Assert.ThrowsAny<OperationCanceledException>(() => Decompress(stream, Cancelled()));
    }

    // A squeeze with no steps signalled takes the default steps, which halve a 256x256 image down to 8x8.
    [Fact]
    public void Squeeze_MetaApply_CancelledToken_Throws()
    {
        JxlTransform squeeze = new() { Id = JxlTransformId.Squeeze };
        using JxlModularImage image = CreateImage();

        Assert.ThrowsAny<OperationCanceledException>(() => squeeze.MetaApply(image, Cancelled()));
    }

    [Fact]
    public void UndoTransforms_CancelledToken_Throws()
    {
        using JxlModularImage image = CreateSqueezedImage();

        Assert.ThrowsAny<OperationCanceledException>(() => image.UndoTransforms(new JxlWeightedHeader(), Cancelled()));
        image.UndoTransforms(new JxlWeightedHeader(), CancellationToken.None);
        Assert.Equal(3, image.Channel.Count);
    }

    [Fact]
    public void Squeeze_Inverse_CancelledToken_Throws()
    {
        using JxlModularImage image = CreateSqueezedImage();

        Assert.ThrowsAny<OperationCanceledException>(() => image.Transform[0].Inverse(image, new JxlWeightedHeader(), Cancelled()));
    }

    private static void Decompress(byte[] stream, CancellationToken cancellationToken)
    {
        using JxlModularImage image = CreateImage();
        JxlModularDecoder.ModularGenericDecompress(new JxlBitReader(stream), image, null, 0, new JxlModularOptions(), true, null, null, null, cancellationToken);
    }

    private static JxlModularImage CreateImage() => JxlModularImage.Create(Configuration.Default.MemoryAllocator, 256, 256, 8, 3);

    private static JxlModularImage CreateSqueezedImage()
    {
        JxlModularImage image = CreateImage();
        JxlTransform squeeze = new() { Id = JxlTransformId.Squeeze };
        squeeze.MetaApply(image, CancellationToken.None);
        image.Transform.Add(squeeze);
        Assert.True(image.Channel.Count > 3);
        return image;
    }

    // A container with an Exif box. The walk over the boxes checks the token per box (D14 review).
    [Fact]
    public void ContainerRead_CancelledToken_Throws()
    {
        byte[] file = File.ReadAllBytes(Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", "orientation_exif.jxl"));

        Assert.NotNull(JxlContainer.Read(file, readMetadata: true, CancellationToken.None).Exif);
        Assert.ThrowsAny<OperationCanceledException>(() => JxlContainer.Read(file, readMetadata: true, Cancelled()));
    }

    // 1 MiB of zeros, Brotli-compressed: measuring it takes 16 buffers, and the token is checked before
    // each (D14 review).
    [Fact]
    public void BrotliMeasure_CancelledToken_Throws()
    {
        byte[] compressed = new byte[1024];
        Assert.True(BrotliEncoder.TryCompress(new byte[1 << 20], compressed, out int written));
        compressed = compressed[..written];

        Assert.Equal(1 << 20, JxlContainer.MeasureDecompressedSize(compressed, "XMP", CancellationToken.None));
        Assert.ThrowsAny<OperationCanceledException>(() => JxlContainer.MeasureDecompressedSize(compressed, "XMP", Cancelled()));
    }

    private static CancellationToken Cancelled() => new(canceled: true);

    private static byte[] EncodeGradient(int width, int height)
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("gradient", width, height);
        return JxlEncoderRoundTrip.Encode(image);
    }
}
