// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Transforms;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Transforms;

[Trait("Category", "Processors")]
public class EntropyCropTest : BaseImageOperationsExtensionTest
{
    [Theory]
    [InlineData(0.5F)]
    [InlineData(.2F)]
    public void EntropyCropThresholdFloatEntropyCropProcessorWithThreshold(float threshold)
    {
        this.operations.EntropyCrop(threshold);
        EntropyCropProcessor processor = this.Verify<EntropyCropProcessor>();

        Assert.Equal(threshold, processor.Threshold);
    }
}
