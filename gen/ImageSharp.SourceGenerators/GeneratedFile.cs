// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// One file that an emitter writes: its hint name and the action that writes its text.
/// </summary>
internal sealed class GeneratedFile
{
    private readonly Action<SourceWriter> write;

    /// <summary>
    /// Initializes a new instance of the <see cref="GeneratedFile"/> class.
    /// </summary>
    /// <param name="hintName">The file name, without a directory.</param>
    /// <param name="write">Writes the text of the file.</param>
    public GeneratedFile(string hintName, Action<SourceWriter> write)
    {
        this.HintName = hintName;
        this.write = write;
    }

    /// <summary>
    /// Gets the hint name: the file name, without a directory.
    /// </summary>
    public string HintName { get; }

    /// <summary>
    /// Writes the text of the file.
    /// </summary>
    /// <param name="writer">The writer for this file.</param>
    public void Write(SourceWriter writer) => this.write(writer);
}
