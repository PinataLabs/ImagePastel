// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// The project that the generator writes sources for, set by the consuming project's
/// <c>ImageSharpGeneratedSources</c> MSBuild property.
/// </summary>
internal sealed class GeneratedSourcesTarget
{
    /// <summary>
    /// The ImageSharp library.
    /// </summary>
    public static readonly GeneratedSourcesTarget Library = new("Library");

    /// <summary>
    /// The ImageSharp test project.
    /// </summary>
    public static readonly GeneratedSourcesTarget Tests = new("Tests");

    private static readonly GeneratedSourcesTarget[] All = [Library, Tests];

    private GeneratedSourcesTarget(string name) => this.Name = name;

    /// <summary>
    /// Gets the property value that selects this target.
    /// </summary>
    public string Name { get; }

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
