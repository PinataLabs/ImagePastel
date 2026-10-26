// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Transforms;

using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Transforms;

[Trait("Category", "Processors")]
public class SkewTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void SkewXYCreateSkewProcessorWithAnglesSet()
    {
        this.operations.Skew(10, 20);
        SkewProcessor processor = this.Verify<SkewProcessor>();

        Assert.Equal(10, processor.DegreesX);
        Assert.Equal(20, processor.DegreesY);
    }
}
