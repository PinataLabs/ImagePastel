// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using SixLabors.ImageSharp.ColorManagement;
using SixLabors.ImageSharp.Metadata.Profiles.Icc;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.ColorManagement;
using SixLabors.ImageSharp.Tests.Processing.Processors.ColorManagement;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Processing.ColorManagement;

[Trait("Category", "Processors")]
[Trait("Profile", "Icc")]
public class ConvertColorProfileTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void ConvertColorProfile_Target_ProcessorHasTargetAndDefaultOptions()
    {
        IccProfile target = ConvertColorProfileProcessorTests.LoadProfile(TestImages.Icc.CompactDisplayP3V4);

        this.operations.ConvertColorProfile(target);
        ConvertColorProfileProcessor processor = this.Verify<ConvertColorProfileProcessor>();

        Assert.Same(target, processor.TargetProfile);
        Assert.Equal(IccRenderingIntent.Perceptual, processor.Options.RenderingIntent);
        Assert.False(processor.Options.BlackPointCompensation);
    }

    [Fact]
    public void ConvertColorProfile_TargetAndOptions_ProcessorHasThem()
    {
        IccProfile target = ConvertColorProfileProcessorTests.LoadProfile(TestImages.Icc.CompactDisplayP3V4);
        IccColorTransformOptions options = new();

        this.operations.ConvertColorProfile(target, options);
        ConvertColorProfileProcessor processor = this.Verify<ConvertColorProfileProcessor>();

        Assert.Same(target, processor.TargetProfile);
        Assert.Same(options, processor.Options);
    }

    [Fact]
    public void ConvertColorProfile_NullTarget_Throws()
        => Assert.Throws<ArgumentNullException>(() => this.operations.ConvertColorProfile(null));

    [Fact]
    public void ConvertColorProfileToSrgb_ProcessorHasNoTarget()
    {
        this.operations.ConvertColorProfileToSrgb();
        ConvertColorProfileProcessor processor = this.Verify<ConvertColorProfileProcessor>();

        Assert.Null(processor.TargetProfile);
    }
}
