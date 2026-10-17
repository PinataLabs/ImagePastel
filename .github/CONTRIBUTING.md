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
2. Build the solution: `dotnet build ImageSharp.slnx -c Release`.
3. Run the tests: `dotnet test tests/ImageSharp.Tests -c Release --no-build`.
4. Compare the public API with the baseline: `pwsh ./ci-apicompat.ps1`.

The build treats all warnings as errors. A suppression of an analyzer rule must give its reason.

Some source files are generated from T4 templates (`*.tt`). Do not edit these files by hand.
After you change a template or a `*.ttinclude` file, run `pwsh ./ci-t4.ps1` and commit the changed output. CI fails if the output is not up to date.

The expected test output is in `tests/Images/External`, in Git LFS.

## Publish a package (maintainers)

The CI workflow builds, tests, and checks the public API. It does not publish.
Both publish workflows require CI to have passed on the head of `main`.

To publish a stable release:

1. In GitHub Actions, run **Publish Release** on `main`.
2. Enter the version, for example `2.2.1`, and keep **dry-run** selected.
3. Read the dry-run summary.
4. Run the workflow again with **dry-run** cleared.

The workflow tags the head of `main` and calls the **Release** workflow.
That workflow publishes the package to nuget.org and creates the GitHub Release.

To publish a pre-release, run **Pre-release** on `main` and select `alpha`, `beta`, or `rc`.
MinVer gives the version, for example `2.2.1-beta.4`.

## Code of conduct

This project uses the code of conduct of the [Contributor Covenant](https://contributor-covenant.org/).
For more information, read the [.NET Foundation Code of Conduct](https://dotnetfoundation.org/code-of-conduct).
