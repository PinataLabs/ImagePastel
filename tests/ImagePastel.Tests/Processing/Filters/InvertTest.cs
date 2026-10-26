// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class InvertTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Invert_InvertProcessorDefaultsSet()
    {
        this.operations.Invert();
        this.Verify<InvertProcessor>();
    }

    [Fact]
    public void Pixelate_rect_PixelateProcessorDefaultsSet()
    {
        this.operations.Invert(this.rect);
        this.Verify<InvertProcessor>(this.rect);
    }
}
