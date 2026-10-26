// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using PinataLabs.ImagePastel.Formats.Jpeg.Components;

namespace PinataLabs.ImagePastel.Tests.Formats.Jpg.Utils;

/// <summary>
/// Utilities to read raw libjpeg data for reference conversion.
/// </summary>
internal static partial class LibJpegTools
{
    public static (double Total, double Average) CalculateDifference(ComponentData expected, ComponentData actual)
    {
        BigInteger totalDiff = 0;
        if (actual.WidthInBlocks < expected.WidthInBlocks)
        {
            throw new Exception("actual.WidthInBlocks < expected.WidthInBlocks");
        }

        if (actual.HeightInBlocks < expected.HeightInBlocks)
        {
            throw new Exception("actual.HeightInBlocks < expected.HeightInBlocks");
        }

        int w = expected.WidthInBlocks;
        int h = expected.HeightInBlocks;
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                Block8x8 aa = expected.SpectralBlocks[x, y];
                Block8x8 bb = actual.SpectralBlocks[x, y];

                long diff = Block8x8.TotalDifference(ref aa, ref bb);
                totalDiff += diff;
            }
        }

        int count = w * h;
        double total = (double)totalDiff;
        double average = (double)totalDiff / (count * Block8x8.Size);
        return (total, average);
    }

    // Generous: the tool takes well under a second per image, but CI machines can be slow.
    private static readonly TimeSpan DumpToolTimeout = TimeSpan.FromMinutes(2);

    private static string DumpToolFullPath => Path.Combine(
        TestEnvironment.ToolsDirectoryFullPath,
        @"jpeg\dump-jpeg-coeffs.exe");

    /// <summary>
    /// Executes 'dump-jpeg-coeffs.exe' for the given jpeg image file, saving the libjpeg spectral data into 'destFile'. Windows only!
    /// See:
    /// <see>
    ///     <cref>https://github.com/SixLabors/Imagesharp.Tests.Images/blob/main/tools/jpeg/README.md</cref>
    /// </see>
    /// </summary>
    public static void RunDumpJpegCoeffsTool(string sourceFile, string destFile)
    {
        if (!TestEnvironment.IsWindows)
        {
            throw new InvalidOperationException("Can't run dump-jpeg-coeffs.exe in non-Windows environment. Skip this test on Linux/Unix!");
        }

        string args = $@"""{sourceFile}"" ""{destFile}""";
        var startInfo = new ProcessStartInfo(DumpToolFullPath, args)
        {
            // The tool prints progress to stdout. An xUnit v3 test process reports results to the
            // runner over stdout, so inherited output would corrupt those messages. Capture it.
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using Process process = Process.Start(startInfo);
        Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
        Task<string> standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(DumpToolTimeout))
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(DumpToolTimeout);
            throw new TimeoutException(
                $"{DumpToolFullPath} {args} did not exit within {DumpToolTimeout}." + FormatOutput(standardOutput, standardError));
        }

        // Make sure the output is complete before reading the exit code and the result file.
        Task.WaitAll(new Task[] { standardOutput, standardError }, DumpToolTimeout);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{DumpToolFullPath} {args} exited with code {process.ExitCode}." + FormatOutput(standardOutput, standardError));
        }
    }

    private static string FormatOutput(Task<string> standardOutput, Task<string> standardError)
        => $"{Environment.NewLine}STDOUT: {CompletedText(standardOutput)}{Environment.NewLine}STDERR: {CompletedText(standardError)}";

    private static string CompletedText(Task<string> read)
        => read.Wait(TimeSpan.FromSeconds(5)) ? read.Result : "(not available)";

    /// <summary>
    /// Extract libjpeg <see cref="SpectralData"/> from the given jpg file with  'dump-jpeg-coeffs.exe'. Windows only!
    /// See:
    /// https://github.com/SixLabors/Imagesharp.Tests.Images/blob/main/tools/jpeg/README.md
    /// </summary>
    public static SpectralData ExtractSpectralData(string inputFile)
    {
        TestFile testFile = TestFile.Create(inputFile);

        string outDir = TestEnvironment.CreateOutputDirectory(".Temp", "JpegCoeffs");
        string fn = $"{Path.GetFileName(inputFile)}-{new Random().Next(1000)}.dctcoeffs";
        string coeffFileFullPath = Path.Combine(outDir, fn);

        try
        {
            RunDumpJpegCoeffsTool(testFile.FullPath, coeffFileFullPath);

            using (var dumpStream = new FileStream(coeffFileFullPath, FileMode.Open))
            using (var rdr = new BinaryReader(dumpStream))
            {
                int componentCount = rdr.ReadInt16();
                var result = new ComponentData[componentCount];

                for (int i = 0; i < componentCount; i++)
                {
                    int widthInBlocks = rdr.ReadInt16();
                    int heightInBlocks = rdr.ReadInt16();
                    var resultComponent = new ComponentData(widthInBlocks, heightInBlocks, i);
                    result[i] = resultComponent;
                }

                var buffer = new byte[64 * sizeof(short)];

                for (int i = 0; i < result.Length; i++)
                {
                    ComponentData c = result[i];

                    for (int y = 0; y < c.HeightInBlocks; y++)
                    {
                        for (int x = 0; x < c.WidthInBlocks; x++)
                        {
                            rdr.Read(buffer, 0, buffer.Length);

                            short[] block = MemoryMarshal.Cast<byte, short>(buffer.AsSpan()).ToArray();
                            c.MakeBlock(block, y, x);
                        }
                    }
                }

                return new SpectralData(result);
            }
        }
        finally
        {
            if (File.Exists(coeffFileFullPath))
            {
                File.Delete(coeffFileFullPath);
            }
        }
    }
}
