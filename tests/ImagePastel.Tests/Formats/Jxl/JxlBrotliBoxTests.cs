// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using PinataLabs.ImagePastel.Formats.Jxl;
using PinataLabs.ImagePastel.Metadata;
using PinataLabs.ImagePastel.Tests.Compression.Brotli;
using Xunit;

namespace PinataLabs.ImagePastel.Tests.Formats.Jxl;

/// <summary>
/// Checks that a container's <c>brob</c> boxes read the same with the managed Brotli decoder, which
/// netstandard2.1 uses, as with <c>System.IO.Compression.BrotliDecoder</c>. The tests run the managed
/// decoder on the test targets through <see cref="JxlContainer.Read{TBrotliDecoder}"/>, and through
/// <c>Image.Identify</c> of the netstandard2.1 build when the build output has it.
/// </summary>
[Trait("Format", "Jxl")]
public class JxlBrotliBoxTests
{
    private const int BoxCap = JxlContainer.BlockSizeLimit;

    private static string GeneratedDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Generated");

    private static byte[] BrobFile => File.ReadAllBytes(Path.Combine(GeneratedDirectory, "exif_xmp_brob.jxl"));

    private static byte[] PlainFile => File.ReadAllBytes(Path.Combine(GeneratedDirectory, "exif_xmp.jxl"));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_BrotliCompressedBoxes_GiveTheExifAndXmpOfThePlainBoxes(bool managed)
    {
        JxlContainerContents expected = Read(PlainFile, managed);

        JxlContainerContents contents = Read(BrobFile, managed);

        Assert.NotEmpty(expected.Exif);
        Assert.NotEmpty(expected.Xmp);
        Assert.Equal(expected.Exif, contents.Exif);
        Assert.Equal(expected.Xmp, contents.Xmp);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 70000)]
    [InlineData(true, 70000)]
    [InlineData(false, 5 << 20)]
    [InlineData(true, 5 << 20)]
    public void Read_BrotliCompressedXmpBox_GivesItsContents(bool managed, int length)
    {
        byte[] xmp = BrotliTestStreams.Input("mixed", length, seed: length);

        JxlContainerContents contents = Read(WithBoxes(Brob("xml ", BrotliTestStreams.Compress(xmp, quality: 5, window: 22))), managed);

        Assert.Equal(xmp, contents.Xmp);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Read_BrotliCompressedBoxOfNoBytes_IsDropped(bool managed)
    {
        JxlContainerContents contents = Read(WithBoxes(Brob("xml ", BrotliTestStreams.Compress(ReadOnlySpan<byte>.Empty, quality: 5, window: 22))), managed);

        Assert.Null(contents.Xmp);
    }

    public static TheoryData<bool, string, byte[]> UnreadableStreams()
    {
        byte[] stream = BrotliTestStreams.Compress(BrotliTestStreams.Input("text", 3000, seed: 1), quality: 5, window: 22);
        TheoryData<bool, string, byte[]> data = new();
        foreach (bool managed in new[] { false, true })
        {
            data.Add(managed, "not a Brotli stream", Enumerable.Repeat((byte)0xFF, 16).ToArray());
            data.Add(managed, "no stream", Array.Empty<byte>());
            data.Add(managed, "a stream cut off", stream.AsSpan(0, stream.Length / 2).ToArray());
            data.Add(managed, "a stream with a byte after it", stream.Append((byte)0).ToArray());
            data.Add(managed, "a stream with a flipped bit", stream.Select((b, i) => i == 40 ? (byte)(b ^ 0x10) : b).ToArray());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(UnreadableStreams))]
    public void Read_UnreadableBrotliBox_IsDroppedAndTheBoxBeforeItStays(bool managed, string what, byte[] stream)
    {
        byte[] plain = Encoding.ASCII.GetBytes("<x:xmpmeta/>");

        JxlContainerContents alone = Read(WithBoxes(Brob("xml ", stream)), managed);
        JxlContainerContents afterPlain = Read(WithBoxes(Box("xml ", plain), Brob("xml ", stream)), managed);

        Assert.True(alone.Xmp == null, what);
        Assert.Equal(plain, afterPlain.Xmp);
        Assert.NotEmpty(alone.Codestream);
    }

    // The managed decoder allocates its ring buffer as it grows to the window: 8 MiB in all for this
    // stream, and 32 MiB for the largest window. BrotliDecoder holds native memory, which this does
    // not count.
    [Theory]
    [InlineData("Exif")]
    [InlineData("xml ")]
    public void Read_ManagedDecoder_BoxThatExpandsOverTheCap_ThrowsBeforeTheCapIsHeld(string type)
    {
        byte[] file = WithBoxes(Brob(type, BrotliTestStreams.Compress(new byte[BoxCap + 1], quality: 1, window: 22)));

        long before = GC.GetAllocatedBytesForCurrentThread();
        InvalidImageContentException ex = Assert.Throws<InvalidImageContentException>(() => Read(file, managed: true));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.Contains("too large", ex.Message);
        Assert.True(allocated < 16 << 20, $"Allocated {allocated} bytes");
    }

    [Fact]
    public void Read_ManagedDecoder_BoxOfTheCap_IsRead()
    {
        byte[] file = WithBoxes(Brob("xml ", BrotliTestStreams.Compress(new byte[BoxCap], quality: 1, window: 22)));

        JxlContainerContents contents = Read(file, managed: true);

        Assert.Equal(BoxCap, contents.Xmp.Length);
        Assert.True(contents.Xmp.AsSpan().IndexOfAnyExcept((byte)0) < 0);
    }

    // 4 groups of 250 mutated boxes. A box that cannot be read is dropped, with either decoder.
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Read_MutatedBrotliBoxes_ReadTheSameWithBothDecoders(int group)
    {
        Random random = new(group);
        byte[] xmp = BrotliTestStreams.Input(group % 2 == 0 ? "text" : "mixed", 2000 + (group * 3000), seed: group);
        byte[] stream = BrotliTestStreams.Compress(xmp, quality: 2 + (group * 3), window: 16 + group);
        int dropped = 0;
        for (int i = 0; i < 250; i++)
        {
            byte[] mutated = (byte[])stream.Clone();
            if (i % 3 == 0)
            {
                mutated = mutated.AsSpan(0, random.Next(mutated.Length)).ToArray();
            }
            else
            {
                mutated[random.Next(mutated.Length)] ^= (byte)(1 << random.Next(8));
            }

            byte[] file = WithBoxes(Brob("xml ", mutated));

            byte[] expected = Read(file, managed: false).Xmp;
            byte[] actual = Read(file, managed: true).Xmp;

            Assert.True(expected == null ? actual == null : actual != null && expected.AsSpan().SequenceEqual(actual), $"group {group}, mutation {i}");
            dropped += actual == null ? 1 : 0;
        }

        Assert.InRange(dropped, 50, 250);
    }

    [Fact]
    public void NetStandardBuild_Identify_BrotliCompressedBoxes_GiveTheSameProfiles()
    {
        (byte[] expectedExif, byte[] expectedXmp) = Profiles(Image.Identify(PlainFile).Metadata);

        (byte[] exif, byte[] xmp) = IdentifyWithTheNetStandardBuild(BrobFile);

        Assert.NotEmpty(expectedExif);
        Assert.NotEmpty(expectedXmp);
        Assert.Equal(expectedExif, exif);
        Assert.Equal(expectedXmp, xmp);
    }

    [Fact]
    public void NetStandardBuild_Identify_UnreadableBrotliBox_GivesNoProfile()
    {
        byte[] file = WithBoxes(Brob("xml ", Enumerable.Repeat((byte)0xFF, 16).ToArray()));

        (byte[] exif, byte[] xmp) = IdentifyWithTheNetStandardBuild(file);

        Assert.Null(exif);
        Assert.Null(xmp);
    }

    private static JxlContainerContents Read(byte[] file, bool managed)
        => managed
            ? JxlContainer.Read<JxlManagedBrotliDecoder>(file, readMetadata: true, CancellationToken.None)
            : JxlContainer.Read<JxlSystemBrotliDecoder>(file, readMetadata: true, CancellationToken.None);

    private static (byte[] Exif, byte[] Xmp) Profiles(ImageMetadata metadata)
        => (metadata.ExifProfile?.ToByteArray(), metadata.XmpProfile?.ToByteArray());

    // Loads the netstandard2.1 build of the library next to the one under test, and calls its
    // Image.Identify. The test project does not build it: a build of the solution does, as CI runs.
    private static (byte[] Exif, byte[] Xmp) IdentifyWithTheNetStandardBuild(byte[] file)
    {
        // artifacts/bin/tests/ImagePastel.Tests/<configuration>/<target framework>
        DirectoryInfo output = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string library = Path.Combine(output.Parent.Parent.Parent.Parent.FullName, "src", "ImagePastel", output.Parent.Name);
        string path = Path.Combine(library, "netstandard2.1", "PinataLabs.ImagePastel.dll");
        Assert.SkipUnless(File.Exists(path), $"The netstandard2.1 build is not at {path}. Build the solution to run this test.");

        // A build of the test project alone leaves an older netstandard2.1 build as it is.
        DateTime current = File.GetLastWriteTimeUtc(Path.Combine(library, output.Name, "PinataLabs.ImagePastel.dll"));
        Assert.SkipWhen(
            File.GetLastWriteTimeUtc(path) < current - TimeSpan.FromMinutes(10),
            "The netstandard2.1 build is older than the build under test. Build the solution to run this test.");

        AssemblyLoadContext context = new("netstandard2.1", isCollectible: true);
        try
        {
            Assembly assembly = context.LoadFromAssemblyPath(path);
            Assert.Equal(".NETStandard,Version=v2.1", assembly.GetCustomAttribute<TargetFrameworkAttribute>().FrameworkName);

            // The library ships with no dependency that brings a Brotli decoder.
            Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name.Contains("Brotli", StringComparison.OrdinalIgnoreCase));

            MethodInfo identify = assembly.GetType("PinataLabs.ImagePastel.Image", throwOnError: true).GetMethod("Identify", new[] { typeof(byte[]) });
            object metadata = Property(identify.Invoke(null, new object[] { file }), "Metadata");
            return (ToByteArray(Property(metadata, "ExifProfile")), ToByteArray(Property(metadata, "XmpProfile")));
        }
        finally
        {
            context.Unload();
        }
    }

    private static object Property(object instance, string name) => instance.GetType().GetProperty(name).GetValue(instance);

    private static byte[] ToByteArray(object profile) => (byte[])profile?.GetType().GetMethod("ToByteArray").Invoke(profile, null);

    // The codestream of exif_xmp.jxl in a container, with the boxes before it.
    private static byte[] WithBoxes(params byte[][] boxes)
    {
        byte[] ftyp = new byte[12];
        Encoding.ASCII.GetBytes("jxl ").CopyTo(ftyp, 0);
        Encoding.ASCII.GetBytes("jxl ").CopyTo(ftyp, 8);

        List<byte> data = new(JxlConstants.ContainerSignature.ToArray());
        data.AddRange(Box("ftyp", ftyp));
        foreach (byte[] box in boxes)
        {
            data.AddRange(box);
        }

        data.AddRange(Box("jxlc", JxlBoxList.Codestream(PlainFile)));
        return data.ToArray();
    }

    // A brob box: the type of the box that is compressed, then the Brotli stream.
    private static byte[] Brob(string type, byte[] stream) => Box("brob", Encoding.ASCII.GetBytes(type).Concat(stream).ToArray());

    private static byte[] Box(string type, byte[] contents)
    {
        byte[] box = new byte[8 + contents.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        contents.CopyTo(box, 8);
        return box;
    }
}
