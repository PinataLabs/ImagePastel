// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using SixLabors.ImageSharp.SourceGenerators.Library;
using SixLabors.ImageSharp.SourceGenerators.Tests;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// The project that the generator writes sources for, set by the consuming project's
/// <c>ImageSharpGeneratedSources</c> MSBuild property, with the emitters of its sources.
/// </summary>
internal sealed class GeneratedSourcesTarget
{
    /// <summary>
    /// The ImageSharp library.
    /// </summary>
    public static readonly GeneratedSourcesTarget Library = new(
        "Library",
        [
            new GuardNumericEmitter(),
            new ImageExtensionsSaveEmitter(),
            new PixelOperationsEmitter(),
            new PixelBlendersEmitter(),
        ]);

    /// <summary>
    /// The ImageSharp test project.
    /// </summary>
    public static readonly GeneratedSourcesTarget Tests = new(
        "Tests",
        [
            new PixelOperationsTestsEmitter(),
        ]);

    private GeneratedSourcesTarget(string name, IEmitter[] emitters)
    {
        this.Name = name;
        this.Emitters = emitters;
    }

    /// <summary>
    /// Gets every target.
    /// </summary>
    public static IReadOnlyList<GeneratedSourcesTarget> All { get; } = [Library, Tests];

    /// <summary>
    /// Gets the property value that selects this target.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the emitters of the sources for this target.
    /// </summary>
    public IReadOnlyList<IEmitter> Emitters { get; }

    /// <summary>
    /// Parses a property value. The comparison ignores case and surrounding white space, as
    /// MSBuild does.
    /// </summary>
    /// <param name="value">The property value.</param>
    /// <param name="target">The target, when the value names one.</param>
    /// <returns><see langword="true"/> when the value names a target.</returns>
    public static bool TryParse(string value, out GeneratedSourcesTarget target)
    {
        string trimmed = value?.Trim();
        foreach (GeneratedSourcesTarget candidate in All)
        {
            if (string.Equals(trimmed, candidate.Name, StringComparison.OrdinalIgnoreCase))
            {
                target = candidate;
                return true;
            }
        }

        target = null;
        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => this.Name;
}
