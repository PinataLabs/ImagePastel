// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.PixelFormats;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// A cancelled token stops an encode with <see cref="OperationCanceledException"/> at each long-running
/// site (issue #337), as <see cref="JxlCancellationTests"/> does for the decoder.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderCancellationTests
{
    // The token is cancelled from the lossy frame's probe, after the EPF search, so the encode is past its
    // start when it is cancelled: the AC groups' tokenization is the next site to check it.
    [Fact]
    public async Task SaveAsJxlAsync_CancelledDuringTheEncode_ThrowsAndWritesNothing()
    {
        using Image<Rgb24> image = JxlEncoderRoundTrip.CreateImage<Rgb24>("noise", 512, 512);
        using CancellationTokenSource source = new();
        bool probed = false;
        JxlEncoder encoder = new()
        {
            Distance = 1,
            Effort = JxlEffort.Thorough,
            TestHooks = new()
            {
                ArHeuristicsProbe = (_, _) =>
                {
                    probed = true;
                    source.Cancel();
                },
            },
        };
        using MemoryStream stream = new();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsJxlAsync(stream, encoder, source.Token));
        Assert.True(probed);
        Assert.Equal(0, stream.Length);
    }
}
