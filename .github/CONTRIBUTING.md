# How to contribute to PinataLabs.ImageSharp

## Report a bug

1. Search the [open and closed issues](https://github.com/PinataLabs/ImageSharp/issues?q=is%3Aissue) to find out if somebody reported the bug before.
2. If no issue exists, [open a new issue](https://github.com/PinataLabs/ImageSharp/issues/new/choose).
3. Give the package version, a clear description, and a code sample or a test case that shows the problem.

To report a security problem, read [SECURITY.md](https://github.com/PinataLabs/ImageSharp/blob/main/SECURITY.md). Do not open a public issue.

## Change the code

The public API must stay the same as in SixLabors.ImageSharp 2.1.13.
The `apicompat` job in CI compares the API in strict mode, and it fails on each addition or removal.
To propose a change to the public API, open an issue first.

To send a fix:

1. Open a pull request with the change.
2. In the description, give the problem, the solution, and the related issue number.
3. Make sure that your code follows the coding patterns of the repository. StyleCop rules apply.
4. Add tests for the change, where possible.

## Build and test

You need the .NET 10 SDK and [Git LFS](https://git-lfs.com/).
Install Git LFS before you clone the repository. If you do not, the test images are missing.

1. Get the test images: `git lfs pull`.
2. Build the solution: `dotnet build ImageSharp.sln -c Release`.
3. Run the tests: `dotnet test tests/ImageSharp.Tests -c Release --no-build`.
4. Compare the public API with the baseline: `pwsh ./ci-apicompat.ps1`.

The build treats all warnings as errors. A suppression of an analyzer rule must give its reason.

The expected test output is in `tests/Images/External`, in Git LFS.

## Code of conduct

This project uses the code of conduct of the [Contributor Covenant](https://contributor-covenant.org/).
For more information, read the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).
