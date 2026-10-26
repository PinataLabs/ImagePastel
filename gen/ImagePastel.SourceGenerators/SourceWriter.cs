// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Text;

namespace SixLabors.ImageSharp.SourceGenerators;

/// <summary>
/// Builds the text of one generated file. Line breaks are always <c>"\n"</c>, whatever the
/// line endings of the emitter's source file.
/// </summary>
internal sealed class SourceWriter
{
    private const string IndentUnit = "    ";

    private readonly StringBuilder builder = new();
    private int indentLevel;

    /// <summary>
    /// Writes an empty line. An empty line has no indentation.
    /// </summary>
    public void WriteLine() => this.builder.Append('\n');

    /// <summary>
    /// Writes one or more lines, each followed by a line break. Every line that is not empty
    /// is prefixed with the current indentation.
    /// </summary>
    /// <param name="text">
    /// The text, usually a raw string literal. Its line breaks may be <c>"\n"</c> or <c>"\r\n"</c>.
    /// </param>
    public void WriteLine(string text)
    {
        int start = 0;
        while (start <= text.Length)
        {
            int end = text.IndexOf('\n', start);
            if (end < 0)
            {
                end = text.Length;
            }

            int length = end - start;
            if (length > 0 && text[end - 1] == '\r')
            {
                length--;
            }

            if (length > 0)
            {
                for (int i = 0; i < this.indentLevel; i++)
                {
                    this.builder.Append(IndentUnit);
                }

                this.builder.Append(text, start, length);
            }

            this.builder.Append('\n');
            start = end + 1;
        }
    }

    /// <summary>
    /// Indents the lines written until the returned scope is disposed by one more level.
    /// </summary>
    /// <returns>The scope that ends the indentation.</returns>
    public IDisposable Indent()
    {
        this.indentLevel++;
        return new IndentScope(this);
    }

    /// <summary>
    /// Returns the text written so far.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString() => this.builder.ToString();

    private sealed class IndentScope : IDisposable
    {
        private SourceWriter writer;

        public IndentScope(SourceWriter writer) => this.writer = writer;

        public void Dispose()
        {
            if (this.writer is not null)
            {
                this.writer.indentLevel--;
                this.writer = null;
            }
        }
    }
}
