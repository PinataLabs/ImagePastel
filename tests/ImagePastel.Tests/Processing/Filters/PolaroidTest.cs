// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;

using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class PolaroidTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Polaroid_amount_PolaroidProcessorDefaultsSet()
    {
        this.operations.Polaroid();
        var processor = this.Verify<PolaroidProcessor>();
        Assert.Equal(processor.GraphicsOptions, this.options);
    }

    [Fact]
    public void Polaroid_amount_rect_PolaroidProcessorDefaultsSet()
    {
        this.operations.Polaroid(this.rect);
        var processor = this.Verify<PolaroidProcessor>(this.rect);
        Assert.Equal(processor.GraphicsOptions, this.options);
    }
}
