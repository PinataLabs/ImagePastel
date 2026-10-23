// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SixLabors.ImageSharp.Formats.Jxl;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace SixLabors.ImageSharp.Tests.Formats.Jxl.Quality;

/// <summary>
/// The cjxl calibration table, <c>tests/Images/Input/Jxl/Calibration/calibration.json</c>: for each
/// calibration image, effort and distance, the size of cjxl v0.12.0's output and the score that the
/// libjxl <c>ssimulacra2</c> tool gives djxl's 16-bit PNG of it. <c>make_calibration.py</c> next to it
/// made the table.
/// </summary>
internal sealed class JxlCalibrationTable
{
    private static readonly Lazy<JxlCalibrationTable> InstanceLazy = new(() => Load(Path.Combine(DirectoryPath, "calibration.json")));

    private readonly IReadOnlyList<string> cjxlArguments;

    private JxlCalibrationTable(string tools, IReadOnlyList<string> cjxlArguments, IReadOnlyList<string> djxlArguments, IReadOnlyList<JxlCalibrationImage> images, IReadOnlyList<JxlCalibrationRow> rows)
    {
        this.Tools = tools;
        this.cjxlArguments = cjxlArguments;
        this.DjxlArguments = djxlArguments;
        this.Images = images;
        this.Rows = rows;
    }

    public static string DirectoryPath => Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, "Jxl", "Calibration");

    public static JxlCalibrationTable Instance => InstanceLazy.Value;

    /// <summary>Gets the version line of the cjxl that made the table.</summary>
    public string Tools { get; }

    /// <summary>Gets the djxl arguments that decoded each row's output for scoring, after the input and output paths.</summary>
    public IReadOnlyList<string> DjxlArguments { get; }

    public IReadOnlyList<JxlCalibrationImage> Images { get; }

    public IReadOnlyList<JxlCalibrationRow> Rows { get; }

    /// <summary>Gets the row of one image at one libjxl effort (3, 5 or 7) and distance (1, 2 or 4).</summary>
    /// <param name="image">The image name, such as <c>bike</c>.</param>
    /// <param name="effort">The libjxl effort.</param>
    /// <param name="distance">The distance.</param>
    /// <returns>The row.</returns>
    public JxlCalibrationRow Find(string image, int effort, double distance)
        => this.Rows.Single(r => r.Image.Name == image && r.Effort == effort && r.Distance == distance);

    /// <summary>Gets the row of one image at the libjxl effort of one encoder effort, and one distance.</summary>
    /// <param name="image">The image name, such as <c>bike</c>.</param>
    /// <param name="effort">The encoder effort: Fastest is libjxl's 3, Default 5 and Thorough 7.</param>
    /// <param name="distance">The distance.</param>
    /// <returns>The row.</returns>
    public JxlCalibrationRow Find(string image, JxlEffort effort, double distance) => this.Find(image, LibjxlEffort(effort), distance);

    /// <summary>Gets the libjxl effort of an encoder effort.</summary>
    /// <param name="effort">The encoder effort.</param>
    /// <returns>3, 5 or 7.</returns>
    public static int LibjxlEffort(JxlEffort effort) => effort switch
    {
        JxlEffort.Fastest => 3,
        JxlEffort.Default => 5,
        JxlEffort.Thorough => 7,
        _ => throw new ArgumentOutOfRangeException(nameof(effort), effort, null),
    };

    /// <summary>Gets the cjxl arguments that made a row, after the input and output paths.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The arguments.</returns>
    public IEnumerable<string> CjxlArguments(JxlCalibrationRow row)
        => this.cjxlArguments.Select(a => a
            .Replace("{distance}", row.Distance.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{effort}", row.Effort.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal));

    private static JxlCalibrationTable Load(string path)
    {
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = json.RootElement;

        Dictionary<string, JxlCalibrationImage> images = new(StringComparer.Ordinal);
        foreach (JsonElement image in root.GetProperty("images").EnumerateArray())
        {
            string name = image.GetProperty("name").GetString();
            images.Add(name, new JxlCalibrationImage(name, image.GetProperty("path").GetString(), image.GetProperty("kind").GetString()));
        }

        List<JxlCalibrationRow> rows = new();
        foreach (JsonElement row in root.GetProperty("rows").EnumerateArray())
        {
            rows.Add(new JxlCalibrationRow(
                images[row.GetProperty("image").GetString()],
                row.GetProperty("effort").GetInt32(),
                row.GetProperty("distance").GetDouble(),
                row.GetProperty("size").GetInt64(),
                row.GetProperty("ssimulacra2").GetDouble()));
        }

        string[] arguments = root.GetProperty("cjxl_arguments").EnumerateArray().Select(a => a.GetString()).ToArray();
        string[] djxlArguments = root.GetProperty("djxl_arguments").EnumerateArray().Select(a => a.GetString()).ToArray();
        return new JxlCalibrationTable(root.GetProperty("tools").GetString(), arguments, djxlArguments, images.Values.ToArray(), rows);
    }
}

/// <summary>One calibration image.</summary>
/// <param name="Name">The name the rows use.</param>
/// <param name="Path">The path under <c>tests/Images/Input</c>, with forward slashes.</param>
/// <param name="Kind">What the image is: a photo, a gradient, text or line art.</param>
internal sealed record JxlCalibrationImage(string Name, string Path, string Kind)
{
    public string FullPath => System.IO.Path.Combine(TestEnvironment.InputImagesDirectoryFullPath, this.Path);

    /// <summary>
    /// Encodes the image as the pixel type its PNG stores, as cjxl reads it. The images are 8-bit sRGB with
    /// no colour chunks, so that is RGB without an alpha channel, or RGBA (only bike_alpha).
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <returns>The encoded file.</returns>
    public byte[] Encode(JxlEncoder encoder)
    {
        if (Image.Identify(this.FullPath).Metadata.GetPngMetadata().ColorType is PngColorType.RgbWithAlpha)
        {
            using Image<Rgba32> rgba = Image.Load<Rgba32>(this.FullPath);
            return JxlEncoderRoundTrip.Encode(rgba, encoder);
        }

        using Image<Rgb24> rgb = Image.Load<Rgb24>(this.FullPath);
        return JxlEncoderRoundTrip.Encode(rgb, encoder);
    }

    public override string ToString() => this.Name;
}

/// <summary>One cjxl encode of the calibration table.</summary>
/// <param name="Image">The image.</param>
/// <param name="Effort">The libjxl effort: 3, 5 or 7, the encoder's Fastest, Default and Thorough.</param>
/// <param name="Distance">The distance.</param>
/// <param name="Size">The size of cjxl's output in bytes.</param>
/// <param name="Score">The <c>ssimulacra2</c> score of cjxl's output, decoded by djxl to a 16-bit PNG, against the image.</param>
internal sealed record JxlCalibrationRow(JxlCalibrationImage Image, int Effort, double Distance, long Size, double Score)
{
    public override string ToString() => $"{this.Image.Name} e{this.Effort} d{this.Distance}";
}
