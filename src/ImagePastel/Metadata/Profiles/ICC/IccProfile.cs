// Copyright (c) Six Labors.
// Licensed under the Apache License, Version 2.0.

using System;
using System.Security.Cryptography;

namespace SixLabors.ImageSharp.Metadata.Profiles.Icc;

/// <summary>
/// Represents an ICC profile
/// </summary>
public sealed class IccProfile : IDeepCloneable<IccProfile>
{
    /// <summary>
    /// The byte array to read the ICC profile from
    /// </summary>
    private readonly byte[] data;

    /// <summary>
    /// The backing file for the <see cref="Entries"/> property
    /// </summary>
    private IccTagDataEntry[] entries;

    /// <summary>
    /// ICC profile header
    /// </summary>
    private IccProfileHeader header;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfile"/> class.
    /// </summary>
    public IccProfile()
        : this((byte[])null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfile"/> class.
    /// </summary>
    /// <param name="data">The raw ICC profile data</param>
    public IccProfile(byte[] data) => this.data = data;

    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfile"/> class.
    /// </summary>
    /// <param name="header">The profile header</param>
    /// <param name="entries">The actual profile data</param>
    internal IccProfile(IccProfileHeader header, IccTagDataEntry[] entries)
    {
        this.header = header ?? throw new ArgumentNullException(nameof(header));
        this.entries = entries ?? throw new ArgumentNullException(nameof(entries));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IccProfile"/> class
    /// by making a copy from another ICC profile.
    /// </summary>
    /// <param name="other">The other ICC profile, where the clone should be made from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is null.</exception>>
    private IccProfile(IccProfile other)
    {
        Guard.NotNull(other, nameof(other));

        this.data = other.ToByteArray();
    }

    /// <summary>
    /// Gets or sets the profile header
    /// </summary>
    public IccProfileHeader Header
    {
        get
        {
            this.InitializeHeader();
            return this.header;
        }

        set => this.header = value;
    }

    /// <summary>
    /// Gets the actual profile data
    /// </summary>
    public IccTagDataEntry[] Entries
    {
        get
        {
            this.InitializeEntries();
            return this.entries;
        }
    }

    /// <summary>
    /// Gets the color space of the device data the profile describes, for example
    /// <see cref="IccColorSpaceType.Rgb"/>, <see cref="IccColorSpaceType.Cmyk"/> or <see cref="IccColorSpaceType.Gray"/>.
    /// This is a shortcut for <see cref="IccProfileHeader.DataColorSpace"/>.
    /// </summary>
    public IccColorSpaceType DataColorSpace => this.Header.DataColorSpace;

    /// <summary>
    /// Gets the profile class, for example <see cref="IccProfileClass.DisplayDevice"/> or
    /// <see cref="IccProfileClass.OutputDevice"/>.
    /// This is a shortcut for <see cref="IccProfileHeader.Class"/>.
    /// </summary>
    public IccProfileClass Class => this.Header.Class;

    /// <summary>
    /// Gets the profile description (the <c>desc</c> tag) as text, for example "Display P3".
    /// </summary>
    /// <remarks>
    /// Version 2 profiles store the description as <c>textDescriptionType</c>: the ASCII text is
    /// returned, or the Unicode text when there is no ASCII text. Version 4 profiles store it as
    /// <c>multiLocalizedUnicodeType</c>: the US English text is returned, then any English text,
    /// then the first text. Either type is accepted whatever the profile version.
    /// </remarks>
    /// <value>The description, or <see langword="null"/> when the profile has no readable description.</value>
    public string Description
    {
        get
        {
            if (!this.TryGetTag(IccProfileTag.ProfileDescription, out IccTagDataEntry entry))
            {
                return null;
            }

            string text = entry switch
            {
                IccTextDescriptionTagDataEntry desc => string.IsNullOrEmpty(desc.Ascii) ? desc.Unicode : desc.Ascii,
                IccMultiLocalizedUnicodeTagDataEntry mluc => SelectText(mluc.Texts),
                IccTextTagDataEntry plain => plain.Text,
                _ => null
            };

            text = text?.TrimEnd('\0');
            return string.IsNullOrEmpty(text) ? null : text;
        }
    }

    /// <summary>
    /// Finds the tag with the given signature and returns its data as <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// Every tag is found by its own signature, also when the profile stores one copy of data that
    /// several tags share (for example <c>rTRC</c>, <c>gTRC</c> and <c>bTRC</c>). When a malformed
    /// profile has more than one tag with the signature, the first one is returned.
    /// Pass <see cref="IccTagDataEntry"/> as <typeparamref name="T"/> to get the tag whatever its
    /// type, for example a tone curve that may be stored as <c>curveType</c> or <c>parametricCurveType</c>.
    /// </remarks>
    /// <typeparam name="T">The type of tag data to get.</typeparam>
    /// <param name="signature">The tag signature, for example <see cref="IccProfileTag.RedMatrixColumn"/>.</param>
    /// <param name="entry">
    /// When this method returns <see langword="true"/>, the tag data; otherwise <see langword="null"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the profile has the tag and its data is a <typeparamref name="T"/>;
    /// <see langword="false"/> if the tag is missing, could not be read, or has another type.
    /// </returns>
    public bool TryGetTag<T>(IccProfileTag signature, out T entry)
        where T : IccTagDataEntry
    {
        foreach (IccTagDataEntry candidate in this.Entries)
        {
            if (candidate.TagSignature == signature)
            {
                entry = candidate as T;
                return entry is not null;
            }
        }

        entry = null;
        return false;
    }

    /// <summary>
    /// Checks whether the profile is equivalent to sRGB (IEC 61966-2-1): same primaries, white point
    /// and tone curve, within the tolerances below.
    /// </summary>
    /// <remarks>
    /// The profile must be an RGB matrix/TRC profile with an XYZ connection space:
    /// <list type="bullet">
    /// <item><description>
    /// Primaries and white point: each component of the <c>rXYZ</c>, <c>gXYZ</c> and <c>bXYZ</c>
    /// colorants is within 0.002 of the sRGB colorants, which are the sRGB primaries with a D65 white
    /// Bradford-adapted to the D50 connection space (red 0.4361, 0.2225, 0.0139; green 0.3851, 0.7169,
    /// 0.0971; blue 0.1431, 0.0606, 0.7142). The colorants of the sRGB profiles from Little CMS, qcms
    /// and HP differ from these by less than 0.0004.
    /// </description></item>
    /// <item><description>
    /// Media white point: the <c>wtpt</c> tag, when present, is within 0.002 of D65 (0.9505, 1, 1.0891),
    /// as version 2 profiles often store it, or of the D50 connection-space white (0.9642, 1, 0.8249).
    /// </description></item>
    /// <item><description>
    /// Tone curve: for each of <c>rTRC</c>, <c>gTRC</c> and <c>bTRC</c> and every 8-bit code value,
    /// decoding with the profile's curve and encoding with the sRGB curve gives back the code value
    /// within less than half a code value. So converting 8-bit pixels between the profile and sRGB
    /// changes none of them.
    /// </description></item>
    /// <item><description>
    /// The profile has no <c>AToB</c>, <c>BToA</c>, <c>DToB</c> or <c>BToD</c> tags, because a color
    /// engine would use those tables in place of the matrix and curves.
    /// </description></item>
    /// </list>
    /// LUT-based profiles that describe sRGB, gray profiles and CMYK profiles are not equivalent.
    /// </remarks>
    /// <returns><see langword="true"/> if the profile is equivalent to sRGB; otherwise <see langword="false"/>.</returns>
    public bool IsSrgb() => IccSrgbCheck.IsSrgb(this);

    /// <inheritdoc/>
    public IccProfile DeepClone() => new IccProfile(this);

    /// <summary>
    /// Calculates the MD5 hash value of an ICC profile
    /// </summary>
    /// <param name="data">The data of which to calculate the hash value</param>
    /// <returns>The calculated hash</returns>
    public static IccProfileId CalculateHash(byte[] data)
    {
        Guard.NotNull(data, nameof(data));
        Guard.IsTrue(data.Length >= 128, nameof(data), "Data length must be at least 128 to be a valid profile header");

        const int profileFlagPos = 44;
        const int renderingIntentPos = 64;
        const int profileIdPos = 84;

        // need to copy some values because they need to be zero for the hashing
        var temp = new byte[24];
        Buffer.BlockCopy(data, profileFlagPos, temp, 0, 4);
        Buffer.BlockCopy(data, renderingIntentPos, temp, 4, 4);
        Buffer.BlockCopy(data, profileIdPos, temp, 8, 16);

        using (var md5 = MD5.Create())
        {
            try
            {
                // Zero out some values
                Array.Clear(data, profileFlagPos, 4);
                Array.Clear(data, renderingIntentPos, 4);
                Array.Clear(data, profileIdPos, 16);

                // Calculate hash
                byte[] hash = md5.ComputeHash(data);

                // Read values from hash
                var reader = new IccDataReader(hash);
                return reader.ReadProfileId();
            }
            finally
            {
                Buffer.BlockCopy(temp, 0, data, profileFlagPos, 4);
                Buffer.BlockCopy(temp, 4, data, renderingIntentPos, 4);
                Buffer.BlockCopy(temp, 8, data, profileIdPos, 16);
            }
        }
    }

    /// <summary>
    /// Checks for signs of a corrupt profile.
    /// </summary>
    /// <remarks>This is not an absolute proof of validity but should weed out most corrupt data.</remarks>
    /// <returns>True if the profile is valid; False otherwise</returns>
    public bool CheckIsValid()
    {
        const int minSize = 128;
        const int maxSize = 50_000_000; // it's unlikely there is a profile bigger than 50MB

        bool arrayValid = true;
        if (this.data != null)
        {
            arrayValid = this.data.Length >= minSize &&
                         this.data.Length >= this.Header.Size;
        }

        return arrayValid &&
               Enum.IsDefined(typeof(IccColorSpaceType), this.Header.DataColorSpace) &&
               Enum.IsDefined(typeof(IccColorSpaceType), this.Header.ProfileConnectionSpace) &&
               Enum.IsDefined(typeof(IccRenderingIntent), this.Header.RenderingIntent) &&
               this.Header.Size >= minSize &&
               this.Header.Size < maxSize;
    }

    /// <summary>
    /// Converts this instance to a byte array.
    /// </summary>
    /// <returns>The <see cref="T:byte[]"/></returns>
    public byte[] ToByteArray()
    {
        if (this.data != null)
        {
            var copy = new byte[this.data.Length];
            Buffer.BlockCopy(this.data, 0, copy, 0, copy.Length);
            return copy;
        }
        else
        {
            var writer = new IccWriter();
            return writer.Write(this);
        }
    }

    private static string SelectText(IccLocalizedString[] texts)
    {
        if (texts is null || texts.Length == 0)
        {
            return null;
        }

        foreach (IccLocalizedString text in texts)
        {
            if (text.Culture.Name == "en-US")
            {
                return text.Text;
            }
        }

        foreach (IccLocalizedString text in texts)
        {
            if (text.Culture.TwoLetterISOLanguageName == "en")
            {
                return text.Text;
            }
        }

        return texts[0].Text;
    }

    private void InitializeHeader()
    {
        if (this.header != null)
        {
            return;
        }

        if (this.data is null)
        {
            this.header = new IccProfileHeader();
            return;
        }

        var reader = new IccReader();
        this.header = reader.ReadHeader(this.data);
    }

    private void InitializeEntries()
    {
        if (this.entries != null)
        {
            return;
        }

        if (this.data is null)
        {
            this.entries = Array.Empty<IccTagDataEntry>();
            return;
        }

        var reader = new IccReader();
        this.entries = reader.ReadTagData(this.data);
    }
}
