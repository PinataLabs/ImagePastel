# Security policy

## Supported versions

Pinata Labs supports the latest 2.x release of PinataLabs.ImagePastel.

Up to version 2.1.14, this fork was published as PinataLabs.ImageSharp.
That package gets no more releases, including security fixes. Move to PinataLabs.ImagePastel.

## Scope

This fork exists only to fix security issues in the ImageSharp v2 line.
It starts from the Six Labors `release/2.1.x` branch at commit `14efb84e8` (2026-03-18).

Pinata Labs fixes security issues in this v2 line.
If Six Labors publishes a security fix that applies to the v2 code, Pinata Labs ports the fix to this fork.

Pinata Labs does not offer support for this package.
For a supported version, use the official [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp/) package.

## Report a vulnerability

Do not open a public issue for a security problem.

1. Go to [Report a vulnerability](https://github.com/PinataLabs/ImagePastel/security/advisories/new) in this repository.
2. Describe the problem and the affected versions.
3. Attach a sample image or code that shows the problem, if you have one.

The maintainers reply in the private advisory.
