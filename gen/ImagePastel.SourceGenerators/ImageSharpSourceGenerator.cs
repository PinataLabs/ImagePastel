// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace PinataLabs.ImagePastel.SourceGenerators;

/// <summary>
/// Generates ImageSharp source files with the emitters of the selected target.
/// </summary>
/// <remarks>
/// The consuming project picks its outputs with the <c>ImageSharpGeneratedSources</c> property,
/// which it must list as a <c>CompilerVisibleProperty</c>. A missing or empty value generates
/// nothing, and an unknown value reports ISG001.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed class ImageSharpSourceGenerator : IIncrementalGenerator
{
    /// <summary>
    /// The analyzer config key of the <c>ImageSharpGeneratedSources</c> property.
    /// </summary>
    internal const string GateProperty = "build_property.ImageSharpGeneratedSources";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

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

        foreach (IEmitter emitter in target.Emitters)
        {
            foreach (GeneratedFile file in emitter.Files)
            {
                context.CancellationToken.ThrowIfCancellationRequested();

                SourceWriter writer = new();
                file.Write(writer);
                context.AddSource(file.HintName, SourceText.From(writer.ToString(), Utf8NoBom));
            }
        }
    }
}
