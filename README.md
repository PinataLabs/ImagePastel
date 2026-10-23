# PinataLabs.ImagePastel

[![CI](https://github.com/PinataLabs/ImagePastel/actions/workflows/ci.yml/badge.svg)](https://github.com/PinataLabs/ImagePastel/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PinataLabs.ImagePastel.svg)](https://www.nuget.org/packages/PinataLabs.ImagePastel/)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/PinataLabs/ImagePastel/blob/main/LICENSE)

PinataLabs.ImagePastel is a fully managed, cross-platform 2D graphics library for .NET.
It decodes, processes, and encodes BMP, GIF, JPEG, PBM, PNG, TGA, TIFF, and WebP images.
It also decodes JPEG XL images. JPEG XL is decode only until the encoder is released.

> [!IMPORTANT]
> This fork exists to fix security issues and uplift in the ImageSharp v2 line and to keep that line under the Apache License 2.0.
> Pinata Labs does not offer support for this package.
> For a supported version, please use the official [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp/) package from [Six Labors](https://github.com/SixLabors/ImageSharp).

## Security

This fork starts from the Six Labors `release/2.1.x` branch at commit `14efb84e8` (2026-03-18).

To report a security problem in this fork, read [SECURITY.md](https://github.com/PinataLabs/ImagePastel/blob/main/SECURITY.md).

## Install

Add the package to your project:

```bash
dotnet add package PinataLabs.ImagePastel
```

## Move from SixLabors.ImageSharp 2.1.x

The public API and the `SixLabors.ImageSharp` namespace are the same as in SixLabors.ImageSharp 2.1.13.
You do not have to change your code.

1. Remove the `SixLabors.ImageSharp` package reference.
2. Add the `PinataLabs.ImagePastel` package reference.
3. Build and test your application.

Do not reference both packages in one application.
The two assemblies contain the same types, and the compiler cannot select between them (error CS0433).

## Move from PinataLabs.ImageSharp 2.1.14

Version 2.1.14 of this fork was published as `PinataLabs.ImageSharp`.
That package gets no more releases. Later versions are published as `PinataLabs.ImagePastel`.
The public API and the `SixLabors.ImageSharp` namespace do not change.

1. Remove the `PinataLabs.ImageSharp` package reference.
2. Add the `PinataLabs.ImagePastel` package reference.
3. Build and test your application.

The assembly name changes from `PinataLabs.ImageSharp` to `PinataLabs.ImagePastel`.
If your code or configuration names the assembly (for example, in reflection, `InternalsVisibleTo`, or binding redirects), change it to the new name.

Do not reference both packages in one application.
The two assemblies contain the same types, and the compiler cannot select between them (error CS0433).

## Target frameworks

| Target | For |
|---|---|
| `net10.0` | .NET 10 and later. This build uses hardware intrinsics (SSE, AVX, AVX2). |
| `netstandard2.1` | Other .NET runtimes, for example .NET 8, .NET 9, Mono, and Unity. This build does not use hardware intrinsics. |

The package does not support .NET Framework.

## Differences from SixLabors.ImageSharp 2.1.13

- The package ID and the assembly name are `PinataLabs.ImagePastel`.
- The strong-name key is different. The public key token is `4ebeb91ace1f6a56`.
- The package targets `net10.0` and `netstandard2.1` only.
- The dependencies are current versions.
- The public API is the same. CI compares it with SixLabors.ImageSharp 2.1.13 in strict mode on each change.

## Versions

Versions continue the 2.x line and start at 2.2.0. The project uses [semantic versioning](https://semver.org/).

Releases and pre-releases are on [nuget.org](https://www.nuget.org/packages/PinataLabs.ImagePastel/).
A maintainer starts each one by hand. A push to `main` does not publish a package.

## Documentation

Documentation for SixLabors.ImageSharp 2.1.x applies to this package.
The source of that version is on the [`release/2.1.x` branch](https://github.com/SixLabors/ImageSharp/tree/release/2.1.x) of the Six Labors repository.
Later Six Labors versions have a different API, so their documentation does not always apply.

## Build from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download) and [Git LFS](https://git-lfs.com/).
The test images are in Git LFS.

1. On Windows, enable long file paths in Git: `git config --system core.longpaths true`.
2. Clone the repository: `git clone https://github.com/PinataLabs/ImagePastel`.
3. Get the test images: `git lfs pull`.
4. Build the solution: `dotnet build ImageSharp.slnx -c Release`.
5. Run the tests: `dotnet test tests/ImageSharp.Tests -c Release --no-build`.

The build treats all warnings as errors.

To also build and test `net11.0`, select a .NET 11 SDK in `global.json` (CI uses 11.0.100-rc.2.26504.105).
The package does not include `net11.0` yet, because releases use the .NET 10 SDK.

## Contributing

Read the [contribution guide](https://github.com/PinataLabs/ImagePastel/blob/main/.github/CONTRIBUTING.md) before you open an issue or a pull request.

This project uses the code of conduct of the [Contributor Covenant](https://contributor-covenant.org/).
For more information, read the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## License

PinataLabs.ImagePastel is licensed under the [Apache License, Version 2.0](https://github.com/PinataLabs/ImagePastel/blob/main/LICENSE).
The [NOTICE](https://github.com/PinataLabs/ImagePastel/blob/main/NOTICE) file gives the origin of the code and the changes that Pinata Labs made.
Third-party code is listed in [THIRD-PARTY-NOTICES.TXT](https://github.com/PinataLabs/ImagePastel/blob/main/THIRD-PARTY-NOTICES.TXT).

## Credits

James Jackson-South, Dirk Lemstra, Anton Firsov, Scott Williams, Brian Popow, and the other contributors at Six Labors wrote ImageSharp.
This fork exists because of their work.
