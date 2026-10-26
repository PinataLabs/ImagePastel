// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class ContrastTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Contrast_amount_ContrastProcessorDefaultsSet()
    {
        this.operations.Contrast(1.5F);
        ContrastProcessor processor = this.Verify<ContrastProcessor>();

        Assert.Equal(1.5F, processor.Amount);
    }

    [Fact]
    public void Contrast_amount_rect_ContrastProcessorDefaultsSet()
    {
        this.operations.Contrast(1.5F, this.rect);
        ContrastProcessor processor = this.Verify<ContrastProcessor>(this.rect);

        Assert.Equal(1.5F, processor.Amount);
    }
}
