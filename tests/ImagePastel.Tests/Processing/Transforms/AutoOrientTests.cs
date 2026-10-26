// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Transforms;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Transforms;

[Trait("Category", "Processors")]
public class AutoOrientTests : BaseImageOperationsExtensionTest
{
    [Fact]
    public void AutoOrient_AutoOrientProcessor()
    {
        this.operations.AutoOrient();
        this.Verify<AutoOrientProcessor>();
    }
}
