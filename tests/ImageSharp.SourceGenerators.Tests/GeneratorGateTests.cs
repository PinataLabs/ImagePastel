// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Globalization;
using Microsoft.CodeAnalysis;
using Xunit;

namespace SixLabors.ImageSharp.SourceGenerators.Tests;

/// <summary>
/// The <c>ImageSharpGeneratedSources</c> property picks the outputs of each consuming project.
/// </summary>
public class GeneratorGateTests
{
    [Fact]
    public void UnknownValue_ReportsISG001_AndGeneratesNothing()
    {
        GeneratorRunResult result = GeneratorDriverHarness.Run("Libary");

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("ISG001", diagnostic.Id);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("'Libary'", diagnostic.GetMessage(), StringComparison.Ordinal);
        Assert.Empty(result.GeneratedSources);
    }

    [Fact]
    public void UnknownValue_MessageListsTheValidValues()
    {
        GeneratorRunResult result = GeneratorDriverHarness.Run("Libary");

        Diagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(
            "The ImageSharpGeneratedSources property value 'Libary' is not known. Use 'Library' or 'Tests', or remove the property.",
            diagnostic.GetMessage(CultureInfo.InvariantCulture));
    }

    // CompilerVisibleProperty passes an unset property as an empty value.
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void MissingValue_GeneratesNothing(string value)
    {
        GeneratorRunResult result = GeneratorDriverHarness.Run(value);

        Assert.Null(result.Exception);
        Assert.Empty(result.Diagnostics);
        Assert.Empty(result.GeneratedSources);
    }
}
