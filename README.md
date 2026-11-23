# PinataLabs.ImagePastel

[![CI](https://github.com/PinataLabs/ImagePastel/actions/workflows/ci.yml/badge.svg)](https://github.com/PinataLabs/ImagePastel/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PinataLabs.ImagePastel.svg)](https://www.nuget.org/packages/PinataLabs.ImagePastel/)
[![License: Apache 2.0](https://img.shields.io/badge/license-Apache%202.0-blue.svg)](https://github.com/PinataLabs/ImagePastel/blob/main/LICENSE)

PinataLabs.ImagePastel is a fully managed, cross-platform 2D graphics library for .NET.
It decodes, processes, and encodes BMP, GIF, JPEG, JPEG XL, PBM, PNG, TGA, TIFF, and WebP images.

## Security

To report a security problem in this fork, read [SECURITY.md](https://github.com/PinataLabs/ImagePastel/blob/main/SECURITY.md).

## Install

Add the package to your project:

```bash
dotnet add package PinataLabs.ImagePastel
```

## Target frameworks

| Target | For |
|---|---|
| `net11.0` | .NET 11 and later. This build uses hardware intrinsics (SSE, AVX, AVX2). |
| `net10.0` | .NET 10. This build uses hardware intrinsics (SSE, AVX, AVX2). |
| `netstandard2.1` | Other .NET runtimes, for example .NET 8, .NET 9, Mono, and Unity. This build does not use hardware intrinsics. |

The package does not support .NET Framework.

### Differences from SixLabors.ImageSharp 2.1.13

- The package ID and the assembly name are `PinataLabs.ImagePastel`.
- The strong-name key is different. The public key token is `4ebeb91ace1f6a56`.
- The package targets `net11.0`, `net10.0` and `netstandard2.1` only.
- The dependencies are current versions.
- The public API is the same. CI compares it with SixLabors.ImageSharp 2.1.13 in strict mode on each change.

## Versions

Versions continue the 2.x line and start at 2.2.0. The project uses [semantic versioning](https://semver.org/).

Releases and pre-releases are on [nuget.org](https://www.nuget.org/packages/PinataLabs.ImagePastel/).

## Documentation

Documentation for SixLabors.ImageSharp 2.1.x applies to this package.
The source of that version is on the [`release/2.1.x` branch](https://github.com/SixLabors/ImageSharp/tree/release/2.1.x) of the Six Labors repository.
Later Six Labors versions have a different API, so their documentation does not always apply.

## Build from source

You need the [.NET 11 SDK](https://dotnet.microsoft.com/download), the .NET 10 runtime, and [Git LFS](https://git-lfs.com/).
The tests run on `net10.0` and `net11.0`. The test images are in Git LFS.

1. On Windows, enable long file paths in Git: `git config --system core.longpaths true`.
2. Clone the repository: `git clone https://github.com/PinataLabs/ImagePastel`.
3. Get the test images: `git lfs pull`.
4. Build the solution: `dotnet build ImagePastel.slnx -c Release`.
5. Run the tests: `dotnet test tests/ImagePastel.Tests -c Release --no-build`.

The build treats all warnings as errors.

## Contributing

Read the [contribution guide](https://github.com/PinataLabs/ImagePastel/blob/main/.github/CONTRIBUTING.md) before you open an issue or a pull request.

This project uses the code of conduct of the [Contributor Covenant](https://contributor-covenant.org/).
For more information, read the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).

## History

Forked from SixLabors.ImageSharp last Apache 2.0 release.

## License

PinataLabs.ImagePastel is licensed under the [Apache License, Version 2.0](https://github.com/PinataLabs/ImagePastel/blob/main/LICENSE).
The [NOTICE](https://github.com/PinataLabs/ImagePastel/blob/main/NOTICE) file gives the origin of the code and the changes that Pinata Labs made.
Third-party code is listed in [THIRD-PARTY-NOTICES.TXT](https://github.com/PinataLabs/ImagePastel/blob/main/THIRD-PARTY-NOTICES.TXT).

## Credits

James Jackson-South, Dirk Lemstra, Anton Firsov, Scott Williams, Brian Popow, and the other contributors at Six Labors wrote ImageSharp.
This fork exists because of their work.
