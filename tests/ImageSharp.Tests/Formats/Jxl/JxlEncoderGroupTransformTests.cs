// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Jxl.Modular.Transforms;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// The per-group transforms of lossless Default, as libjxl effort 5 runs them: the RCT search and the
/// local palette. Each test reads the encoder's choice back through the decoder's parse. An image in one
/// group has its transforms in the global stream; a larger one has them in each group's stream.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlEncoderGroupTransformTests
{
    private static JxlEncoder LosslessDefault => JxlEncoderRoundTrip.Lossless(JxlEffort.Default);

    [Fact]
    public void Encode_DefaultPhotoInOneGroup_WritesAnRct()
    {
        using Image<Rgb24> image = JxlEncoderTransformTests.LoadPhoto();

        List<JxlTransform> transforms = JxlEncodedFrame.ReadGlobalTransforms(JxlEncoderRoundTrip.Encode(image, LosslessDefault));

        JxlTransform rct = Assert.Single(transforms, t => t.Id == JxlTransformId.Rct);
        Assert.NotEqual(0u, rct.RctType);
    }
}
