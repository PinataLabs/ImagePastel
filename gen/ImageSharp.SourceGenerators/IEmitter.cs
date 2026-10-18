// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// Writes the generated files of one template family.
/// </summary>
internal interface IEmitter
{
    /// <summary>
    /// Gets the hint names of the files this emitter writes. Each is a file name,
    /// without a directory.
    /// </summary>
    IReadOnlyList<string> HintNames { get; }

    /// <summary>
    /// Writes the file with the given hint name.
    /// </summary>
    /// <param name="hintName">One of <see cref="HintNames"/>.</param>
    /// <param name="writer">The writer for that file.</param>
    void Emit(string hintName, SourceWriter writer);
}
