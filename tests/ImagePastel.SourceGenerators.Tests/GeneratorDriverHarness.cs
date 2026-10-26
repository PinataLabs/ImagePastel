// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace SixLabors.ImageSharp.SourceGenerators.Tests;

/// <summary>
/// Runs <see cref="ImageSharpSourceGenerator"/> the way the compiler does, and compares its
/// output with the expected files under <c>Expected/&lt;Target&gt;/</c>.
/// </summary>
internal static class GeneratorDriverHarness
{
    /// <summary>
    /// The environment variable that turns on the refresh mode. Set it to <c>1</c>.
    /// </summary>
    public const string UpdateSnapshotsVariable = "IMAGESHARP_UPDATE_SNAPSHOTS";

    /// <summary>
    /// Gets the encoding of the expected files: UTF-8 without a byte order mark.
    /// </summary>
    public static UTF8Encoding Utf8NoBom { get; } = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Gets the directory that holds the expected files of every target, in the source tree.
    /// </summary>
    public static string ExpectedRoot { get; } = typeof(GeneratorDriverHarness).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(a => a.Key == "ExpectedDirectory")
        .Value;

    /// <summary>
    /// Gets a value indicating whether the refresh mode is on.
    /// </summary>
    public static bool UpdateSnapshots
        => Environment.GetEnvironmentVariable(UpdateSnapshotsVariable) == "1";

    /// <summary>
    /// Runs the generator on an empty compilation.
    /// </summary>
    /// <param name="gateValue">
    /// The <c>ImageSharpGeneratedSources</c> property value, or <see langword="null"/> to leave the
    /// property out.
    /// </param>
    /// <returns>The result of the generator run.</returns>
    public static GeneratorRunResult Run(string gateValue)
    {
        CSharpCompilation compilation = CSharpCompilation.Create(
            "GeneratorInput",
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        Dictionary<string, string> options = [];
        if (gateValue is not null)
        {
            options[ImageSharpSourceGenerator.GateProperty] = gateValue;
        }

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new ImageSharpSourceGenerator().AsSourceGenerator()],
            optionsProvider: new GlobalOptionsProvider(options));

        driver = driver.RunGenerators(compilation, TestContext.Current.CancellationToken);
        return driver.GetRunResult().Results.Single();
    }

    /// <summary>
    /// Runs the generator for <paramref name="target"/> and checks its output against
    /// <c>Expected/&lt;target&gt;/</c>, or rewrites that directory in the refresh mode.
    /// </summary>
    /// <param name="target">The <c>ImageSharpGeneratedSources</c> value.</param>
    public static void AssertMatchesSnapshots(string target)
    {
        GeneratorRunResult result = Run(target);
        Assert.Null(result.Exception);

        AssertMatchesSnapshots(
            result.Diagnostics,
            result.GeneratedSources.ToDictionary(s => s.HintName, s => s.SourceText.ToString(), StringComparer.Ordinal),
            Path.Combine(ExpectedRoot, target),
            UpdateSnapshots);
    }

    /// <summary>
    /// Checks generator output against the expected files in <paramref name="expectedDirectory"/>.
    /// Both sides have their line endings normalized to <c>"\n"</c> before the comparison.
    /// </summary>
    /// <param name="diagnostics">The diagnostics the generator reported. There must be none.</param>
    /// <param name="sources">The generated text of each hint name.</param>
    /// <param name="expectedDirectory">
    /// The directory with one expected file per hint name. A missing directory has no files.
    /// </param>
    /// <param name="update">
    /// <see langword="true"/> to rewrite <paramref name="expectedDirectory"/> from
    /// <paramref name="sources"/> and then fail, so that a refresh never passes silently.
    /// </param>
    public static void AssertMatchesSnapshots(
        IReadOnlyCollection<Diagnostic> diagnostics,
        IReadOnlyDictionary<string, string> sources,
        string expectedDirectory,
        bool update)
    {
        Assert.Empty(diagnostics);

        if (update)
        {
            WriteSnapshots(sources, expectedDirectory);
            Assert.Fail(
                $"{UpdateSnapshotsVariable}=1: rewrote {sources.Count} file(s) in {expectedDirectory}. " +
                $"Review the diff, then run the tests again without {UpdateSnapshotsVariable}.");
        }

        string[] expectedNames = ListExpectedFiles(expectedDirectory);
        Assert.Equal(expectedNames, sources.Keys.OrderBy(n => n, StringComparer.Ordinal));

        foreach (KeyValuePair<string, string> source in sources)
        {
            // Decode without dropping a BOM, so a BOM in the expected file is a difference.
            byte[] expectedBytes = File.ReadAllBytes(Path.Combine(expectedDirectory, source.Key));
            string expected = Utf8NoBom.GetString(expectedBytes);
            Assert.Equal(Normalize(expected), Normalize(source.Value));
        }
    }

    private static void WriteSnapshots(IReadOnlyDictionary<string, string> sources, string expectedDirectory)
    {
        // Git keeps no empty directories, so a target without outputs has no directory.
        if (sources.Count > 0)
        {
            Directory.CreateDirectory(expectedDirectory);
        }

        foreach (string stale in ListExpectedFiles(expectedDirectory).Where(n => !sources.ContainsKey(n)))
        {
            File.Delete(Path.Combine(expectedDirectory, stale));
        }

        foreach (KeyValuePair<string, string> source in sources)
        {
            File.WriteAllBytes(Path.Combine(expectedDirectory, source.Key), Utf8NoBom.GetBytes(Normalize(source.Value)));
        }
    }

    private static string[] ListExpectedFiles(string expectedDirectory)
        => Directory.Exists(expectedDirectory)
            ? [.. Directory.EnumerateFiles(expectedDirectory).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal)]
            : [];

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal);

    private sealed class GlobalOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty = new DictionaryOptions(ImmutableDictionary<string, string>.Empty);

        public GlobalOptionsProvider(IDictionary<string, string> globalOptions)
            => this.GlobalOptions = new DictionaryOptions(globalOptions.ToImmutableDictionary());

        public override AnalyzerConfigOptions GlobalOptions { get; }

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }

    private sealed class DictionaryOptions : AnalyzerConfigOptions
    {
        private readonly ImmutableDictionary<string, string> values;

        public DictionaryOptions(ImmutableDictionary<string, string> values) => this.values = values;

        public override bool TryGetValue(string key, out string value) => this.values.TryGetValue(key, out value);
    }
}
