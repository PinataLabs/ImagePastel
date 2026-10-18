// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Sdk;

namespace SixLabors.ImageSharp.SourceGenerators.Tests;

/// <summary>
/// The snapshot comparison must fail on every kind of difference, or a green snapshot run
/// would not prove that the output is unchanged.
/// </summary>
public class GeneratorDriverHarnessTests : IDisposable
{
    private static readonly Diagnostic[] NoDiagnostics = [];

    private readonly string directory = Path.Combine(Path.GetTempPath(), "imagesharp-snapshots-" + Guid.NewGuid().ToString("N"));

    public GeneratorDriverHarnessTests() => Directory.CreateDirectory(this.directory);

    public void Dispose() => Directory.Delete(this.directory, recursive: true);

    [Fact]
    public void Passes_WhenEveryOutputMatchesItsFile()
    {
        this.WriteExpected("A.cs", "class A\n{\n}\n");

        GeneratorDriverHarness.AssertMatchesSnapshots(NoDiagnostics, Sources(("A.cs", "class A\n{\n}\n")), this.directory, update: false);
    }

    [Fact]
    public void Passes_WhenOutputHasCrLfLineEndings()
    {
        this.WriteExpected("A.cs", "class A\n{\n}\n");

        GeneratorDriverHarness.AssertMatchesSnapshots(NoDiagnostics, Sources(("A.cs", "class A\r\n{\r\n}\r\n")), this.directory, update: false);
    }

    [Fact]
    public void Passes_WhenTheExpectedFileHasCrLfLineEndings()
    {
        this.WriteExpected("A.cs", "class A\r\n{\r\n}\r\n");

        GeneratorDriverHarness.AssertMatchesSnapshots(NoDiagnostics, Sources(("A.cs", "class A\n{\n}\n")), this.directory, update: false);
    }

    [Fact]
    public void Fails_WhenAnOutputIsMissing()
    {
        this.WriteExpected("A.cs", "class A { }\n");
        this.WriteExpected("B.cs", "class B { }\n");

        Assert.ThrowsAny<XunitException>(() => GeneratorDriverHarness.AssertMatchesSnapshots(
            NoDiagnostics, Sources(("A.cs", "class A { }\n")), this.directory, update: false));
    }

    [Fact]
    public void Fails_WhenThereIsAnExtraOutput()
    {
        this.WriteExpected("A.cs", "class A { }\n");

        Assert.ThrowsAny<XunitException>(() => GeneratorDriverHarness.AssertMatchesSnapshots(
            NoDiagnostics, Sources(("A.cs", "class A { }\n"), ("B.cs", "class B { }\n")), this.directory, update: false));
    }

    [Fact]
    public void Fails_WhenTheGeneratorReportsADiagnostic()
    {
        this.WriteExpected("A.cs", "class A { }\n");
        Diagnostic diagnostic = Diagnostic.Create(
            new DiagnosticDescriptor("TEST001", "Test", "Test", "Test", DiagnosticSeverity.Warning, isEnabledByDefault: true),
            Location.None);

        Assert.ThrowsAny<XunitException>(() => GeneratorDriverHarness.AssertMatchesSnapshots(
            [diagnostic], Sources(("A.cs", "class A { }\n")), this.directory, update: false));
    }

    [Theory]
    [InlineData("﻿class A { }\n")]
    [InlineData("class A { }")]
    [InlineData("class A  { }\n")]
    public void Fails_WhenTheTextDiffers(string expectedFileText)
    {
        this.WriteExpected("A.cs", expectedFileText);

        Assert.ThrowsAny<XunitException>(() => GeneratorDriverHarness.AssertMatchesSnapshots(
            NoDiagnostics, Sources(("A.cs", "class A { }\n")), this.directory, update: false));
    }

    [Fact]
    public void Refresh_RewritesTheExpectedFiles_AndFails()
    {
        this.WriteExpected("A.cs", "old\r\n");
        this.WriteExpected("Stale.cs", "stale\n");

        Assert.ThrowsAny<XunitException>(() => GeneratorDriverHarness.AssertMatchesSnapshots(
            NoDiagnostics, Sources(("A.cs", "new\r\n"), ("B.cs", "b\n")), this.directory, update: true));

        Assert.Equal("new\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(this.directory, "A.cs")));
        Assert.Equal("b\n"u8.ToArray(), File.ReadAllBytes(Path.Combine(this.directory, "B.cs")));
        Assert.False(File.Exists(Path.Combine(this.directory, "Stale.cs")));

        // The rewritten files are what a normal run then expects.
        GeneratorDriverHarness.AssertMatchesSnapshots(
            NoDiagnostics, Sources(("A.cs", "new\r\n"), ("B.cs", "b\n")), this.directory, update: false);
    }

    private static Dictionary<string, string> Sources(params (string HintName, string Text)[] sources)
    {
        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach ((string hintName, string text) in sources)
        {
            result.Add(hintName, text);
        }

        return result;
    }

    private void WriteExpected(string name, string text)
        => File.WriteAllText(Path.Combine(this.directory, name), text, GeneratorDriverHarness.Utf8NoBom);
}
