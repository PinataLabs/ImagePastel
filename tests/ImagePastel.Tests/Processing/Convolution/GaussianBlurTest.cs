// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Convolution;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Convolution;

[Trait("Category", "Processors")]
public class GaussianBlurTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void GaussianBlur_GaussianBlurProcessorDefaultsSet()
    {
        this.operations.GaussianBlur();
        var processor = this.Verify<GaussianBlurProcessor>();

        Assert.Equal(3f, processor.Sigma);
    }

    [Fact]
    public void GaussianBlur_amount_GaussianBlurProcessorDefaultsSet()
    {
        this.operations.GaussianBlur(0.2f);
        var processor = this.Verify<GaussianBlurProcessor>();

        Assert.Equal(.2f, processor.Sigma);
    }

    [Fact]
    public void GaussianBlur_amount_rect_GaussianBlurProcessorDefaultsSet()
    {
        this.operations.GaussianBlur(0.6f, this.rect);
        var processor = this.Verify<GaussianBlurProcessor>(this.rect);

        Assert.Equal(.6f, processor.Sigma);
    }
}
