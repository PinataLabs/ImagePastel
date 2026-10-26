# How to contribute to PinataLabs.ImagePastel

## Report a bug

1. Search the [open and closed issues](https://github.com/PinataLabs/ImagePastel/issues?q=is%3Aissue) to find out if somebody reported the bug before.
2. If no issue exists, [open a new issue](https://github.com/PinataLabs/ImagePastel/issues/new/choose).
3. Give the package version, a clear description, and a code sample or a test case that shows the problem.

To report a security problem, read [SECURITY.md](https://github.com/PinataLabs/ImagePastel/blob/main/SECURITY.md). Do not open a public issue.

## Change the code

The public API must stay the same as in the baseline commit named in `ci-apicompat.ps1`.
The `apicompat` job in CI compares the API in strict mode, and it fails on each addition or removal.
To propose a change to the public API, open an issue first.
An agreed break is listed, with its reason, in `apicompat-suppressions.xml`.

To send a fix:

1. Open a pull request with the change.
2. In the description, give the problem, the solution, and the related issue number.
3. Make sure that your code follows the coding patterns of the repository. StyleCop rules apply.
4. Add tests for the change, where possible.

## Build and test

You need the .NET 10 SDK and [Git LFS](https://git-lfs.com/).
Install Git LFS before you clone the repository. If you do not, the test images are missing.

1. Get the test images: `git lfs pull`.
2. Build the solution: `dotnet build ImagePastel.slnx -c Release`.
3. Run the tests: `dotnet test tests/ImagePastel.Tests -c Release --no-build`.
4. Run the source generator tests: `dotnet test tests/ImagePastel.SourceGenerators.Tests -c Release --no-build`.
5. Compare the public API with the baseline: `pwsh ./ci-apicompat.ps1`.

The expected output of `ImagePastel.Tests` is in `tests/Images/External`, in Git LFS.

The build treats all warnings as errors. A suppression of an analyzer rule must give its reason.

## Edit generated code

Some code is written at build time by the source generator in `gen/ImagePastel.SourceGenerators`.
This code is not in the repository.
After a build, you can read it in `artifacts/obj/<category>/<project>/<configuration>/<framework>/generated/`.

Each project selects its generated files with the `ImageSharpGeneratedSources` property: `Library` or `Tests`.
An unknown value causes error `ISG001`. A project without the property gets no generated files.

Each generated file comes from an emitter: a class in `gen/ImagePastel.SourceGenerators/Library/` or `Tests/` that implements `IEmitter`.
An emitter writes C# raw string literals (`$$"""…"""`) to a `SourceWriter`. In these strings, `{{name}}` inserts a value.

The expected output of each emitter is in `tests/ImagePastel.SourceGenerators.Tests/Expected/<Target>/`, one file for each generated file.
The tests fail if the output is different from these files, if a file is missing or extra, or if the generator reports a diagnostic.

To change generated code:

1. Edit the emitter.
2. Refresh the expected files. In Bash: `IMAGESHARP_UPDATE_SNAPSHOTS=1 dotnet test tests/ImagePastel.SourceGenerators.Tests`.
   In PowerShell: `$env:IMAGESHARP_UPDATE_SNAPSHOTS = '1'; dotnet test tests/ImagePastel.SourceGenerators.Tests; Remove-Item env:IMAGESHARP_UPDATE_SNAPSHOTS`.
   This run writes the expected files and then fails, so that a refresh cannot pass by accident.
3. Read the diff of the expected files. It shows the change to the generated code.
4. Run the tests again without the variable. They must pass.
5. Commit the emitter and the expected files together.

To add a generated file, add it to the `Files` of an emitter.
Each item gives the hint name of the file and the method that writes it. The hint name is the file name, without a directory.
A new emitter must also be added to the emitter list of its target in `GeneratedSourcesTarget`.

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
