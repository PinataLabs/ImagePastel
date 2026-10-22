// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Memory;
using Xunit;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl;

/// <summary>
/// Slice D14g: libjxl's OSS-Fuzz regression set (the inputs of libjxl <c>tools/djxl_fuzzer_test.cc</c>,
/// in <c>tests/Images/Input/Jxl/libjxl/oss-fuzz/</c>). Issue #112's rule: each input decodes, or throws
/// <see cref="InvalidImageContentException"/>, <see cref="InvalidMemoryOperationException"/> or
/// <see cref="OperationCanceledException"/>, within the time budget, through <c>Image.Load</c> and
/// <c>Image.Identify</c>. The files are fed as stored: libjxl's fuzzer reads the last 4 bytes as fuzzer
/// flags, but stripping them changes no input's outcome class (D14 probe). <c>Image.Load</c> decodes the
/// inputs that <c>djxl</c> v0.12.0 decodes, and only those.
/// </summary>
[Trait("Format", "Jxl")]
[Collection(nameof(JxlOssFuzzTests))]
public class JxlOssFuzzTests
{
    // The budget is 10 s per call, as #112 asks, in Release, which is how every CI job runs the tests
    // (ci.yml: the build, net11.0 and jxl-oracle jobs all pass -c Release). A Debug build is 13 to 16
    // times slower on the slowest input (6748486320652288: about 2 s in Release and 28 to 32 s in Debug,
    // as it decodes many of its 39 frames before it fails, most of the time in the EPF), so a Debug run
    // gets 16 times the budget. It still catches a hang.
    //
    // The budget is wall-clock time, so the class runs alone, after the parallel tests. Run in parallel
    // with the rest of the JPEG XL suite on a 32-thread machine, the slowest input took over 10 s in
    // Release.
#if DEBUG
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10 * 16);
#else
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);
#endif

    private const string Prefix = "clusterfuzz-testcase-minimized-djxl_fuzzer-";

    // The inputs that djxl v0.12.0 decodes. It rejects the other 37.
    private static readonly string[] DjxlDecodes =
    {
        "5112097090961408",
        "5382562858532864",
        "5582808628723712",
        "5685623166468096",
        "5849986531721216",
        "6013780411154432",
        "6329817929220096",
    };

    // The inputs whose header has a preview (D14a). Before D14a, Identify and Load threw
    // NotSupportedException for each. Now each identifies, and Load rejects each, as djxl does.
    private static readonly string[] PreviewInputs =
    {
        "4816304719134720",
        "4848606801166336",
        "5381727462227968",
        "5467620336336896",
        "5914266367557632",
        "6355777170833408",
        "6735607318052864",
        "6748486320652288",
    };

    private static string InputDirectory => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "libjxl", "oss-fuzz");

    // The OSS-Fuzz test case IDs, which name the files.
    public static TheoryData<string> Inputs()
    {
        TheoryData<string> data = new();
        foreach (string file in Directory.GetFiles(InputDirectory).Select(Path.GetFileName).OrderBy(f => f, StringComparer.Ordinal))
        {
            Assert.StartsWith(Prefix, file, StringComparison.Ordinal);
            data.Add(file[Prefix.Length..]);
        }

        return data;
    }

    [Fact]
    public void Inputs_AreTheFortyFourFilesOfTheRegressionSet()
        => Assert.Equal(44, Inputs().Count);

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task Identify_DecodesOrThrowsAnAllowedExceptionWithinTheBudget(string id)
    {
        bool identified = await RunWithinTheBudget(id, async (configuration, stream, token) =>
        {
            IImageInfo info = await Image.IdentifyAsync(configuration, stream, token);
            Assert.NotNull(info);
            return $"{info.Width}x{info.Height}";
        });

        Assert.True(identified || !PreviewInputs.Contains(id), $"{id} has a preview, which Identify skips");
    }

    [Theory]
    [MemberData(nameof(Inputs))]
    public async Task Load_DecodesOrThrowsAnAllowedExceptionWithinTheBudget(string id)
    {
        bool decoded = await RunWithinTheBudget(id, async (configuration, stream, token) =>
        {
            using Image image = await Image.LoadAsync(configuration, stream, token);
            return $"{image.Width}x{image.Height}, {image.Frames.Count} frames";
        });

        Assert.Equal(DjxlDecodes.Contains(id), decoded);
    }

    // Returns true when the action completes, and false when it throws an allowed exception.
    private static async Task<bool> RunWithinTheBudget(string id, Func<Configuration, Stream, CancellationToken, Task<string>> action)
    {
        byte[] data = await File.ReadAllBytesAsync(Path.Combine(InputDirectory, Prefix + id), TestContext.Current.CancellationToken);
        Configuration configuration = CreateConfiguration();

        // The token runs out at the budget, so a slow input stops at the decoder's next check. An
        // OperationCanceledException from this token is a failure, not an allowed outcome.
        using CancellationTokenSource budget = new(Budget);
        Stopwatch stopwatch = Stopwatch.StartNew();
        string outcome;
        bool completed = false;
        try
        {
            using MemoryStream stream = new(data);
            outcome = await Task.Run(() => action(configuration, stream, budget.Token), TestContext.Current.CancellationToken);
            completed = true;
        }
        catch (OperationCanceledException) when (budget.IsCancellationRequested)
        {
            outcome = null;
            Assert.Fail($"{id}: over the budget of {Budget.TotalSeconds} s");
        }
        catch (Exception ex) when (ex is InvalidImageContentException or InvalidMemoryOperationException or OperationCanceledException)
        {
            outcome = $"{ex.GetType().Name}: {ex.Message}";
        }

        stopwatch.Stop();
        TestContext.Current.TestOutputHelper?.WriteLine($"{outcome} ({stopwatch.ElapsedMilliseconds} ms)");
        Assert.True(stopwatch.Elapsed <= Budget, $"{id}: {stopwatch.ElapsedMilliseconds} ms, over the budget of {Budget.TotalSeconds} s");
        return completed;
    }

    private static Configuration CreateConfiguration()
    {
        Configuration configuration = Configuration.CreateDefaultInstance();
        configuration.ImageFormatsManager.SetDecoder(JxlFormat.Instance, new JxlDecoder());
        configuration.ImageFormatsManager.AddImageFormatDetector(new JxlImageFormatDetector());
        return configuration;
    }

    // Runs the class alone, so other tests do not take its time budget.
    [CollectionDefinition(nameof(JxlOssFuzzTests), DisableParallelization = true)]
    public class NonParallelCollection
    {
    }
}
