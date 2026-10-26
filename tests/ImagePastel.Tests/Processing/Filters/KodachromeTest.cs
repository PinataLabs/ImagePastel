// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using PinataLabs.ImagePastel.Processing;
using PinataLabs.ImagePastel.Processing.Processors.Filters;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Processing.Filters;

[Trait("Category", "Processors")]
public class KodachromeTest : BaseImageOperationsExtensionTest
{
    [Fact]
    public void Kodachrome_amount_KodachromeProcessorDefaultsSet()
    {
        this.operations.Kodachrome();
        this.Verify<KodachromeProcessor>();
    }

    [Fact]
    public void Kodachrome_amount_rect_KodachromeProcessorDefaultsSet()
    {
        this.operations.Kodachrome(this.rect);
        this.Verify<KodachromeProcessor>(this.rect);
    }
}
