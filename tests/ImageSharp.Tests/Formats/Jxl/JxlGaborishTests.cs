// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.IO;
using System.Threading;
using SixLabors.ImageSharp.Formats.Jxl;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>The Gaborish loop filter.</summary>
[Trait("Format", "Jxl")]
public class JxlGaborishTests
{
    private static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    // vardct_xyb_gaborish has Gaborish and no EPF, so it is rejected by the Gaborish check alone.
    [Fact]
    public void Decode_GaborishOnly_IsRejectedByTheGaborishCheck()
    {
        using FileStream stream = File.OpenRead(InputPath("vardct_xyb_gaborish"));

        NotSupportedException ex = Assert.Throws<NotSupportedException>(() => new JxlDecoderCore(Configuration.Default).DecodeToPlanes(stream, CancellationToken));
        Assert.Contains("Loop filters (Gaborish)", ex.Message, StringComparison.Ordinal);
    }

    private static string InputPath(string name) => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated", name + ".jxl");
}
