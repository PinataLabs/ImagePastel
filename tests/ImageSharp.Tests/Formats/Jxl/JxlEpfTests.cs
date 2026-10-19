// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>The edge-preserving filter (EPF).</summary>
[Trait("Format", "Jxl")]
public class JxlEpfTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // vardct_xyb_epf has EPF and no Gaborish, so it is rejected by the EPF check alone.
    [Fact]
    public void Decode_EpfOnly_IsRejectedByTheEpfCheck()
    {
        using FileStream stream = File.OpenRead(InputPath("vardct_xyb_epf"));

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("Loop filters (EPF)", ex.Message, StringComparison.Ordinal);
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");
}
