// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class SepiaTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Sepia_amount_SepiaProcessorDefaultsSet()
    {
        this.operations.Sepia();
        this.Verify<SepiaProcessor>();
    }

    [Fact]
    public void Sepia_amount_rect_SepiaProcessorDefaultsSet()
    {
        this.operations.Sepia(this.rect);
        this.Verify<SepiaProcessor>(this.rect);
    }
}
