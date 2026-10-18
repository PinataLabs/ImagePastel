// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using SixLabors.ImageSharp.SourceGenerators.Library;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// Generates the ImageSharp sources that were produced by T4 templates.
/// </summary>
/// <remarks>
/// The consuming project picks its outputs with the <c>ImageSharpGeneratedSources</c> property,
/// which it must list as a <c>CompilerVisibleProperty</c>. A missing or empty value generates
/// nothing, and an unknown value reports ISG001.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ImageSharpSourceGenerator : IIncrementalGenerator
{
    private const string GateProperty = "build_property.ImageSharpGeneratedSources";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly IEmitter[] LibraryEmitters =
    [
        new GuardNumericEmitter(),
        new ImageExtensionsSaveEmitter(),
        new PixelOperationsEmitter(),
    ];

    private static readonly IEmitter[] TestsEmitters = [];

    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // A string is equatable, so the output is cached until the property changes.
        IncrementalValueProvider<string> gate = context.AnalyzerConfigOptionsProvider.Select(
            static (options, _) =>
                options.GlobalOptions.TryGetValue(GateProperty, out string value)
                    ? value
                    : string.Empty);

        context.RegisterSourceOutput(gate, static (spc, value) => Emit(spc, value));
    }

    private static void Emit(SourceProductionContext context, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (!GeneratedSourcesTarget.TryParse(value, out GeneratedSourcesTarget target))
        {
            context.ReportDiagnostic(Diagnostic.Create(DiagnosticDescriptors.UnknownGeneratedSourcesTarget, Location.None, value));
            return;
        }

        IEmitter[] emitters = target == GeneratedSourcesTarget.Library ? LibraryEmitters : TestsEmitters;
        foreach (IEmitter emitter in emitters)
        {
            foreach (string hintName in emitter.HintNames)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                SourceWriter writer = new();
                emitter.Emit(hintName, writer);
                context.AddSource(hintName, SourceText.From(writer.ToString(), Utf8NoBom));
            }
        }
    }
}
