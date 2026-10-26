// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using Xunit;

namespace PinataLabs.ImagePastel.SourceGenerators.Tests;

/// <summary>
/// Compares the generator output for each <c>ImageSharpGeneratedSources</c> target with the
/// expected files under <c>Expected/</c>. Set <c>IMAGESHARP_UPDATE_SNAPSHOTS=1</c> to rewrite them.
/// </summary>
public class GeneratorSnapshotTests
{
    [Theory]
    [InlineData("Library")]
    [InlineData("Tests")]
    public void Output_MatchesExpectedFiles(string target)
        => GeneratorDriverHarness.AssertMatchesSnapshots(target);
}
