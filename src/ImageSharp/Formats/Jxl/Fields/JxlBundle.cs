// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/fields.cc (InitVisitor, SetDefaultVisitor, ReadVisitor, Bundle::Init, Bundle::SetDefault, Bundle::Read).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Fields;

/// <summary>
/// Reads bundles and gives them their defaults. Only libjxl's read and default visitors are ported;
/// the write, size and all-default visitors belong to the encoder.
/// </summary>
internal static class JxlBundle
{
    private const int MaxExtensions = 64;

    /// <summary>
    /// Sets every field to its default, but leaves nested bundles alone: their own constructors
    /// already initialised them. Call it at the end of each bundle constructor.
    /// </summary>
    /// <param name="fields">The bundle.</param>
    public static void Init(JxlFields fields) => new InitVisitor().Visit(fields);

    /// <summary>Sets every field, including nested bundles, to its default.</summary>
    /// <param name="fields">The bundle.</param>
    public static void SetDefault(JxlFields fields) => new SetDefaultVisitor().Visit(fields);

    /// <summary>
    /// Reads <paramref name="fields"/> from <paramref name="reader"/>. Throws
    /// <see cref="InvalidImageContentException"/> if the fields are invalid or the bytes run out.
    /// </summary>
    /// <param name="reader">The reader.</param>
    /// <param name="fields">The bundle.</param>
    public static void Read(JxlBitReader reader, JxlFields fields)
    {
        ReadVisitor visitor = new(reader);
        visitor.Visit(fields);
        if (!visitor.Ok)
        {
            JxlThrowHelper.ThrowInvalidImageContentException("F16 infinity or NaN are not supported");
        }
    }

    private class DefaultVisitor : JxlVisitor
    {
        public override uint Bits(int bits, uint defaultValue, uint value) => defaultValue;

        public override uint U32(JxlU32Enc enc, uint defaultValue, uint value) => defaultValue;

        public override ulong U64(ulong defaultValue, ulong value) => defaultValue;

        public override bool Bool(bool defaultValue, bool value) => defaultValue;

        public override float F16(float defaultValue, float value) => defaultValue;

        // Always visit conditional fields to ensure they are initialized.
        public override bool Conditional(bool condition) => true;

        // Initialize the flag and don't skip initializing the others.
        public override bool AllDefault(JxlFields fields) => false;
    }

    private sealed class InitVisitor : DefaultVisitor
    {
        // Nested bundles were initialized by their own constructors.
        public override void VisitNested(JxlFields fields)
        {
        }
    }

    private sealed class SetDefaultVisitor : DefaultVisitor
    {
    }

    private sealed class ReadVisitor : JxlVisitor
    {
        private readonly JxlBitReader reader;

        // May be 0 even if the corresponding extension is present.
        private readonly ulong[] extensionBits = new ulong[MaxExtensions];

        private ulong totalExtensionBits;

        // 0 iff extensions == 0.
        private long posAfterExtSize;

        public ReadVisitor(JxlBitReader reader) => this.reader = reader;

        /// <summary>Gets a value indicating whether no error other than "not enough bytes" occurred.</summary>
        public bool Ok { get; private set; } = true;

        public override bool IsReading => true;

        public override uint Bits(int bits, uint defaultValue, uint value)
        {
            uint result = this.reader.ReadBits32(bits);
            this.reader.EnsureAllReadsWithinBounds("header");
            return result;
        }

        public override uint U32(JxlU32Enc enc, uint defaultValue, uint value)
        {
            uint result = JxlFieldCoders.ReadU32(enc, this.reader);
            this.reader.EnsureAllReadsWithinBounds("header");
            return result;
        }

        public override ulong U64(ulong defaultValue, ulong value)
        {
            ulong result = JxlFieldCoders.ReadU64(this.reader);
            this.reader.EnsureAllReadsWithinBounds("header");
            return result;
        }

        public override float F16(float defaultValue, float value)
        {
            // As in libjxl, an invalid F16 fails the whole bundle only at the end.
            this.Ok &= JxlFieldCoders.TryReadF16(this.reader, out float result);
            this.reader.EnsureAllReadsWithinBounds("header");
            return result;
        }

        public override void SetDefault(JxlFields fields) => JxlBundle.SetDefault(fields);

        public override ulong BeginExtensions(ulong extensions)
        {
            extensions = base.BeginExtensions(extensions);
            if (extensions == 0)
            {
                return extensions;
            }

            // For each extension that is present, read the number of bits it uses, so that
            // extensions this decoder does not know can be skipped.
            for (ulong remaining = extensions; remaining != 0; remaining &= remaining - 1)
            {
                int index = JxlBits.FloorLog2Nonzero(remaining & (~remaining + 1));
                this.extensionBits[index] = this.U64(0, 0);
                ulong sum = unchecked(this.totalExtensionBits + this.extensionBits[index]);
                if (sum < this.totalExtensionBits)
                {
                    JxlThrowHelper.ThrowInvalidImageContentException("Extension bits overflowed, invalid codestream");
                }

                this.totalExtensionBits = sum;
            }

            // Used by EndExtensions to skip past any remaining extensions.
            this.posAfterExtSize = this.reader.TotalBitsConsumed;
            return extensions;
        }

        public override void EndExtensions()
        {
            base.EndExtensions();

            // Happens if extensions == 0: there is nothing to skip.
            if (this.posAfterExtSize == 0)
            {
                return;
            }

            // Skip new fields this decoder doesn't know about, if any.
            long bitsRead = this.reader.TotalBitsConsumed;
            ulong end = unchecked((ulong)this.posAfterExtSize + this.totalExtensionBits);
            if (end < (ulong)this.posAfterExtSize)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Invalid extension size, caused overflow");
            }

            if ((ulong)bitsRead > end)
            {
                JxlThrowHelper.ThrowInvalidImageContentException("Read more extension bits than budgeted");
            }

            ulong remainingBits = end - (ulong)bitsRead;
            if (remainingBits != 0)
            {
                // A skip longer than the whole input is out of bounds anyway.
                this.reader.SkipBits((long)System.Math.Min(remainingBits, (ulong)long.MaxValue / 2));
                this.reader.EnsureAllReadsWithinBounds("header");
            }
        }
    }
}
