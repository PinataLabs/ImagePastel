// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/fields.cc (InitVisitor, SetDefaultVisitor, AllDefaultVisitor, ReadVisitor, CanEncodeVisitor,
// Bundle::Init, Bundle::SetDefault, Bundle::AllDefault, Bundle::CanEncode, Bundle::Read).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

using System.Collections.Generic;
using SixLabors.ImageSharp.Formats.Jxl.Base;

namespace SixLabors.ImageSharp.Formats.Jxl.Fields;

/// <summary>
/// Reads and writes bundles, and gives them their defaults. <see cref="Write"/> is in
/// <c>JxlBundle.Write.cs</c>, as libjxl keeps it in <c>enc_fields.cc</c>.
/// </summary>
internal static partial class JxlBundle
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
    /// Returns whether all fields, including nested bundles and <c>extensions</c>, equal their defaults.
    /// </summary>
    /// <param name="fields">The bundle.</param>
    /// <returns>Whether all fields are default.</returns>
    public static bool AllDefault(JxlFields fields)
    {
        AllDefaultVisitor visitor = new();
        visitor.Visit(fields);
        return visitor.IsAllDefault;
    }

    /// <summary>
    /// Returns whether all fields of a bundle can be encoded, that is, have a valid representation.
    /// If so, <paramref name="totalBits"/> is the exact number of bits <see cref="Write"/> writes.
    /// </summary>
    /// <param name="fields">The bundle.</param>
    /// <param name="totalBits">The number of bits.</param>
    /// <returns>Whether the bundle can be encoded.</returns>
    public static bool CanEncode(JxlFields fields, out long totalBits)
    {
        CanEncodeVisitor visitor = new();
        try
        {
            visitor.Visit(fields);
        }
        catch (InvalidImageContentException)
        {
            // A check in VisitFields that the reader shares rejected a value.
            totalBits = 0;
            return false;
        }

        totalBits = visitor.TotalBits;
        return visitor.Error == null;
    }

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

    private sealed class AllDefaultVisitor : JxlVisitor
    {
        public bool IsAllDefault { get; private set; } = true;

        public override uint Bits(int bits, uint defaultValue, uint value)
        {
            this.IsAllDefault &= value == defaultValue;
            return value;
        }

        public override uint U32(JxlU32Enc enc, uint defaultValue, uint value)
        {
            this.IsAllDefault &= value == defaultValue;
            return value;
        }

        public override ulong U64(ulong defaultValue, ulong value)
        {
            this.IsAllDefault &= value == defaultValue;
            return value;
        }

        public override float F16(float defaultValue, float value)
        {
            this.IsAllDefault &= System.Math.Abs(value - defaultValue) < 1E-6f;
            return value;
        }

        // Visit all fields so we can compute the actual all-default value.
        public override bool AllDefault(JxlFields fields) => false;
    }

    /// <summary>
    /// Checks that every field can be encoded and counts the bits. libjxl keeps one extension size
    /// for the whole visit and ascribes it to the first bundle with extensions, so it cannot write two
    /// bundles that both have extensions, such as a frame header and its loop filter. This port
    /// keeps a size for each bundle with extensions instead, in visit order, which <see cref="WriteVisitor"/>
    /// writes in the same order. Each size still goes to the bundle's first extension, with zeros for
    /// the others, as in libjxl.
    /// <para>
    /// The reader, like libjxl's, keeps one running extension size for the whole visit, not one per
    /// bundle, so it skips the right number of bits only when no bundle with extensions follows one
    /// with a nonzero size. No bundle has extension fields yet, so every size the encoder writes for
    /// a real bundle is zero.
    /// </para>
    /// </summary>
    private sealed class CanEncodeVisitor : JxlVisitor
    {
        // One entry for each BeginExtensions not yet ended: the index of its size in
        // extensionBits (-1 if extensions == 0), the bit count after the extensions field, and the
        // extensions. Bundles nest, so this is a stack.
        private readonly Stack<(int Index, long Start, ulong Extensions)> open = new();

        private readonly List<ulong> extensionBits = new();

        /// <summary>Gets the first reason a value cannot be encoded, or <see langword="null"/>.</summary>
        public string Error { get; private set; }

        /// <summary>Gets the number of bits, including the extension sizes.</summary>
        public long TotalBits { get; private set; }

        /// <summary>Gets the extension sizes of the bundles with extensions, in visit order.</summary>
        public IReadOnlyList<ulong> ExtensionBits => this.extensionBits;

        public override uint Bits(int bits, uint defaultValue, uint value)
        {
            if (!JxlFieldCoders.CanEncodeBits(bits, value, out int encodedBits))
            {
                this.Fail($"Value {value} too large for {bits} bits");
            }

            this.TotalBits += encodedBits;
            return value;
        }

        public override uint U32(JxlU32Enc enc, uint defaultValue, uint value)
        {
            if (!JxlFieldCoders.CanEncodeU32(enc, value, out int encodedBits))
            {
                this.Fail($"No feasible selector for {value}");
            }

            this.TotalBits += encodedBits;
            return value;
        }

        public override ulong U64(ulong defaultValue, ulong value)
        {
            this.TotalBits += JxlFieldCoders.U64EncodedBits(value);
            return value;
        }

        public override float F16(float defaultValue, float value)
        {
            if (!JxlFieldCoders.CanEncodeF16(value, out int encodedBits))
            {
                this.Fail($"F16 cannot hold {value}");
            }

            this.TotalBits += encodedBits;
            return value;
        }

        // libjxl stores the result in the bundle's all_default field for the write visitor. The
        // bundles here have no such field, so the write visitor works it out again.
        public override bool AllDefault(JxlFields fields)
        {
            bool allDefault = JxlBundle.AllDefault(fields);
            this.Bool(true, allDefault);
            return allDefault;
        }

        public override ulong BeginExtensions(ulong extensions)
        {
            extensions = base.BeginExtensions(extensions);
            int index = -1;
            if (extensions != 0)
            {
                index = this.extensionBits.Count;
                this.extensionBits.Add(0);
            }

            this.open.Push((index, this.TotalBits, extensions));
            return extensions;
        }

        public override void EndExtensions()
        {
            base.EndExtensions();
            (int index, long start, ulong extensions) = this.open.Pop();
            if (index < 0)
            {
                return;
            }

            // The bits of the extension fields, which excludes this bundle's sizes but includes
            // those of nested bundles.
            ulong bits = (ulong)(this.TotalBits - start);
            this.extensionBits[index] = bits;

            // Also need to encode the sizes and bill them to the total. All bits go to the first
            // extension; the others get zero.
            this.TotalBits += JxlFieldCoders.U64EncodedBits(bits);
            for (ulong remaining = extensions & (extensions - 1); remaining != 0; remaining &= remaining - 1)
            {
                this.TotalBits += JxlFieldCoders.U64EncodedBits(0);
            }
        }

        private void Fail(string error) => this.Error ??= error;
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
