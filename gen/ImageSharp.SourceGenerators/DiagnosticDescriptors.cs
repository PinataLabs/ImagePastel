// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using Microsoft.CodeAnalysis;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// The diagnostics that the generator reports. Each new ID must also be listed in
/// <c>AnalyzerReleases.Unshipped.md</c>.
/// </summary>
internal static class DiagnosticDescriptors
{
    private const string Category = "ImageSharp.SourceGenerators";

    /// <summary>
    /// ISG001: the <c>ImageSharpGeneratedSources</c> property has a value that is not a target.
    /// </summary>
    public static readonly DiagnosticDescriptor UnknownGeneratedSourcesTarget = new(
        id: "ISG001",
        title: "Unknown ImageSharpGeneratedSources value",
        messageFormat: "The ImageSharpGeneratedSources property value '{0}' is not known. Use 'Library' or 'Tests', or remove the property.",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
