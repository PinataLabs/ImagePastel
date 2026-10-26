// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class BlackWhiteTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void BlackWhite_CorrectProcessor()
    {
        this.operations.BlackWhite();
        this.Verify<BlackWhiteProcessor>();
    }

    [Fact]
    public void BlackWhite_rect_CorrectProcessor()
    {
        this.operations.BlackWhite(this.rect);
        this.Verify<BlackWhiteProcessor>(this.rect);
    }
}
