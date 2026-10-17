// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using SixLabors.ImageSharp.Formats.Webp.BitWriter;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.WebP;

[Trait("Format", "Webp")]
public class Vp8BitWriterTests
{
    [Fact]
    public void Finish_WithLessDataThanExpected_KeepsExpectedSize()
    {
        // arrange
        var writer = new Vp8BitWriter(4096);
        for (int i = 0; i < 1000; i++)
        {
            writer.PutI16Mode(i % 4);
        }

        // act
        writer.Finish();

        // assert
        Assert.InRange(writer.NumBytes(), 1, 4096);
        Assert.Equal(4096, writer.Buffer.Length);
    }

    [Fact]
    public void PutI16Mode_WithMoreDataThanExpected_GrowsBufferGeometrically()
    {
        // arrange
        var writer = new Vp8BitWriter(1024);
        byte[] buffer = writer.Buffer;
        int resizes = 0;

        // act
        for (int i = 0; writer.NumBytes() < 256 * 1024; i++)
        {
            writer.PutI16Mode(i % 4);
            if (!ReferenceEquals(buffer, writer.Buffer))
            {
                resizes++;
                buffer = writer.Buffer;
            }
        }

        // assert
        // Growing by half each time takes about 14 resizes to get from 1 KiB to 256 KiB.
        // Growing by 1 KiB each time takes 255.
        Assert.InRange(resizes, 1, 20);
    }
}
