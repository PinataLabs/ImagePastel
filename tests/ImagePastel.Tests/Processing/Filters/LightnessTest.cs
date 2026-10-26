// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class LightnessTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Lightness_amount_LightnessProcessorDefaultsSet()
    {
        this.operations.Lightness(.5F);
        LightnessProcessor processor = this.Verify<LightnessProcessor>();

        Assert.Equal(.5F, processor.Amount);
    }

    [Fact]
    public void Lightness_amount_rect_LightnessProcessorDefaultsSet()
    {
        this.operations.Lightness(.5F, this.rect);
        LightnessProcessor processor = this.Verify<LightnessProcessor>(this.rect);

        Assert.Equal(.5F, processor.Amount);
    }
}
