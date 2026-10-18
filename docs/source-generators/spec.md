# T4 templates to Roslyn source generators: handoff spec

This spec replaces the repository's T4 templates with one Roslyn incremental source generator. It holds a PRD and the ordered implementation slices. Raise one implementation issue per slice from the [slice table](#slices), and link the issue to its row.

The decisions below come from a design review. The parent issue is [#198](https://github.com/PinataLabs/ImagePastel/issues/198), and each slice has its own sub-issue.

## PRD

### Goal

Remove the T4 toolchain and make the generated code easy to edit and test:

- No `dotnet-t4` tool, no `ci-t4.ps1`, no CI drift job, and no generated `.cs` files committed under `src/` or `tests/ImageSharp.Tests/`.
- Generator code is ordinary C#. It gets IDE support, analyzers and unit tests.

The port is 1:1. Each generator output must be **byte-identical** to the `.cs` file that is committed today. The inputs stay as hardcoded lists, now in C#. The generator does not read symbols or syntax from the compilation.

### Current state

There are 20 templates and 2 `*.ttinclude` files. All are `hostspecific="false"` and read nothing from the project. Each one expands hardcoded string lists into text. Together they generate about 15,000 lines.

| Template | Template → output lines | Inputs |
|---|---|---|
| 13 × `PixelFormats/PixelImplementations/PixelOperations/Generated/*.PixelOperations.Generated.tt` | 16 → 291–345 each | One call, `GenerateAllDefaultConversionMethods("<Pixel>")`, and three lists in `_Common.ttinclude` (`CommonPixelTypes`, `OptimizedPixelTypes`, `Rgba32CompatibleTypes`) |
| `PixelFormats/PixelOperations{TPixel}.Generated.tt` | 151 → 948 | Pixel type list |
| `PixelFormats/PixelBlenders/PorterDuffFunctions.Generated.tt` | 270 → 3,661 | `composers` × `blenders` arrays |
| `PixelFormats/PixelBlenders/DefaultPixelBlenders.Generated.tt` | 111 → 4,129 | The same two arrays, duplicated |
| `Formats/ImageExtensions.Save.tt` (public API) | 151 → 842 | 8 format names |
| `Common/SharedInfrastructure/Guard.Numeric.tt` | 133 → 1,273 | 12 primitive type names |
| `Formats/Jpeg/Components/Block8x8F.Generated.tt` | 104 → 154 | Constants |
| `tests/ImageSharp.Tests/PixelFormats/PixelOperations/Generated/PixelOperationsTests.Specialized.Generated.tt` | 9 → 566 | Its own `_Common.ttinclude`, with different lists |

Supporting setup: `dotnet-t4` 3.0.0 in `.config/dotnet-tools.json`; `ci-t4.ps1`; the `t4` job in `.github/workflows/ci.yml`, which `pack` lists in `needs:`; `TextTemplatingFileGenerator` items in `ImageSharp.csproj` and `ImageSharp.Tests.csproj`; and `.github/CONTRIBUTING.md` lines 37–38.

### Source and licensing

- The emitters are ports of this repo's own Six Labors templates (Apache-2.0). Emitter files that hold ported template text keep `// Copyright (c) Six Labors.` because Apache-2.0 §4 requires derivative works to keep the original notices. New infrastructure files (the writer, the gate, the test harness) use `// Copyright (c) PinataLabs.`
- The generated output keeps its current headers, because it must be byte-identical.
- Never read SixLabors/ImageSharp upstream code (`CLAUDE.md`). Port only from the templates in this repo.

### Decisions

| Area | Decision |
|---|---|
| Scope | All 20 templates go. `Block8x8F` becomes hand-maintained source. The other 19 become generator output. |
| Inputs | A 1:1 port. The lists become `static readonly` arrays in the generator. No discovery from the compilation. |
| Equivalence | Every output is byte-identical to the committed `.cs` file before its template is deleted. |
| Authoring | C# raw interpolated strings (`$$"""…"""`) through a small indentation-aware writer. No template-engine dependency. |
| Project location | `gen/ImageSharp.SourceGenerators/`, with its own `gen/Directory.Build.props`. Tests in `tests/ImageSharp.SourceGenerators.Tests/`. Both are added to `ImageSharp.slnx`. |
| Roslyn version | `Microsoft.CodeAnalysis.CSharp` 4.14.0, `PrivateAssets="all"`, pinned in `Directory.Packages.props`. Central transitive pinning gives every project one Roslyn version, and BenchmarkDotNet 0.15.8 needs 4.14.0 or later. |
| Gating | One generator assembly. Each consuming project sets the `ImageSharpGeneratedSources` property to `Library` or `Tests`. |
| Generator structure | One `[Generator]` with an emitter list per target. |
| Hint names | Exactly the current output file names, with no directories. |
| Shared lists | Duplicated lists move into `PixelTypeNames` and `PixelBlenderNames`. The test project's lists stay separate. |
| Reviewability | Hand-rolled snapshot tests. The expected files are seeded from today's committed outputs, so the snapshot tests also prove byte-identity. |
| Local inspection | `EmitCompilerGeneratedFiles=true` in both consuming projects. |
| Bad gate value | Error `ISG001`. A missing property means no output. |
| Coverage | Generated sources are excluded from coverage. Slice SG1 compares the coverage report's file list before and after. |
| Docs | `CONTRIBUTING.md` is updated in SG1 and SG5. |

### Out of scope

- Discovering inputs from the compilation (for example a `[GeneratePixelOperations]` attribute, or scanning format folders). It can come later, one emitter at a time.
- Changing any generated code. Clean-ups to the output come after the migration, as ordinary changes reviewed through the snapshot diffs.
- Replacing `Guard.Numeric` with generic math. `netstandard2.1` has no `INumber<T>`.
- Shipping the generator to package consumers.

## Design

### Projects

**`gen/Directory.Build.props`** sets `SixLaborsProjectCategory` to `gen` and imports the root `Directory.Build.props`, the same way `src/` and `tests/` do. It does not import `src/Directory.Build.props`, so the generator gets no package metadata, MinVer, documentation file or `InternalsVisibleTo` list. StyleCop, strong-name signing, `LangVersion` 14.0 and `TreatWarningsAsErrors` still apply.

**`gen/ImageSharp.SourceGenerators/ImageSharp.SourceGenerators.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.0</TargetFramework>
    <IsRoslynComponent>true</IsRoslynComponent>
    <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

The package version, 4.14.0, comes from `Directory.Packages.props`.

`netstandard2.0` has no `IsExternalInit`, so the generator code avoids `record` types and `init` accessors. It does not need them.

**Consuming projects** (`src/ImageSharp/ImageSharp.csproj` with `Library`, `tests/ImageSharp.Tests/ImageSharp.Tests.csproj` with `Tests`):

```xml
<PropertyGroup>
  <ImageSharpGeneratedSources>Library</ImageSharpGeneratedSources>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
<ItemGroup>
  <CompilerVisibleProperty Include="ImageSharpGeneratedSources" />
  <ProjectReference Include="..\..\gen\ImageSharp.SourceGenerators\ImageSharp.SourceGenerators.csproj"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="false"
                    PrivateAssets="all" />
</ItemGroup>
```

Generated files are written under `artifacts/obj/<category>/<project>/…/generated/`, which git ignores.

### Why the gate exists

Both `ImageSharp` and `ImageSharp.Tests` reference the generator. Without a gate, the tests project would also get `Guard`, `PorterDuffFunctions` and the other library outputs. Those duplicate the library's internal types that the tests already see through `InternalsVisibleTo`. The result is CS0436, which `TreatWarningsAsErrors` turns into a build error. `RegisterPostInitializationOutput` cannot read options, so the generator uses `RegisterSourceOutput` on the gate value instead.

### Generator

```text
gen/ImageSharp.SourceGenerators/
  ImageSharpSourceGenerator.cs      [Generator]; reads the gate, runs the emitters for the target
  GeneratedSourcesTarget.cs         Library | Tests, and parsing of the property value
  DiagnosticDescriptors.cs          ISG001
  SourceWriter.cs                   indentation-aware text writer; emits "\n"
  IEmitter.cs                       hint name(s) and Emit(SourceWriter)
  Names/
    PixelTypeNames.cs               CommonPixelTypes, OptimizedPixelTypes, Rgba32CompatibleTypes
    PixelBlenderNames.cs            composers, blenders
  Library/
    GuardNumericEmitter.cs          Guard.Numeric.cs
    ImageExtensionsSaveEmitter.cs   ImageExtensions.Save.cs
    PixelOperationsEmitter.cs       13 × <Pixel>.PixelOperations.Generated.cs, PixelOperations{TPixel}.Generated.cs
    PixelBlendersEmitter.cs         PorterDuffFunctions.Generated.cs, DefaultPixelBlenders.Generated.cs
  Tests/
    PixelOperationsTestsEmitter.cs  PixelOperationsTests.Specialized.Generated.cs
```

The pipeline:

```csharp
IncrementalValueProvider<string> gate = context.AnalyzerConfigOptionsProvider.Select(
    static (options, _) =>
        options.GlobalOptions.TryGetValue("build_property.ImageSharpGeneratedSources", out string value)
            ? value
            : string.Empty);

context.RegisterSourceOutput(gate, static (spc, value) =>
{
    // Empty: no output. Unknown: report ISG001. Otherwise run every emitter for the target
    // and AddSource each hint name.
});
```

The gate is a `string`, which is equatable, so the source output is cached and only reruns when the property changes. The emitters take no other input.

Each emitter is a mechanical port of one template family. A `<# foreach #>` block becomes a C# `foreach` that writes `$$"""…"""` blocks, and `<#= x #>` becomes `{{x}}`. Whitespace, blank lines and line breaks must match the committed output exactly. The snapshot tests check this.

### Snapshot tests

`tests/ImageSharp.SourceGenerators.Tests/` targets `net10.0` and uses xUnit v3, like the other test projects. It references the generator project as a normal assembly reference.

```text
tests/ImageSharp.SourceGenerators.Tests/
  GeneratorSnapshotTests.cs
  GeneratorDriverHarness.cs
  Expected/
    Library/   one file per Library hint name
    Tests/     one file per Tests hint name
```

For each target, the harness:

1. Builds an empty `CSharpCompilation` and runs `CSharpGeneratorDriver` with a `build_property.ImageSharpGeneratedSources` global option for that target.
2. Asserts that the generator reported no diagnostics.
3. Asserts that the set of hint names equals the set of files in `Expected/<Target>/`, so a missing or extra output fails.
4. Asserts that each output's text, with line endings normalized to `\n`, equals its expected file.
5. When `IMAGESHARP_UPDATE_SNAPSHOTS=1` is set, writes the outputs to `Expected/<Target>/` instead, and then fails, so a refresh can't pass silently in CI.

There are also two small tests: one for `ISG001` on an unknown value, and one for no output when the property is missing.

The expected files are seeded by copying today's committed outputs, unchanged. A green run is the byte-identity check.

`.gitattributes` must give `Expected/**` a fixed `eol`, matching the existing generated files, so checkouts on every OS produce the same bytes.

### Coverage

Generated sources are excluded from the coverlet collector in `tests/coverlet.runsettings`: `ExcludeByFile` on the generated-files path, or `ExcludeByAttribute` if the outputs carry `[GeneratedCode]`. SG1 picks whichever works. SG1 records the coverage report's file list on `main` and on the branch, and confirms that the only change is the generated files.

## Delivery

### Merge strategy

Merge each slice to `main` on its own. While migration is in progress, the remaining templates still go through `ci-t4.ps1` and the CI `t4` job. The script lists `*.tt` files with `git ls-files`, so it ignores templates that have already been deleted.

### Rules for every slice

- Port from this repo's templates only. Never read upstream SixLabors/ImageSharp code.
- Copy the outputs into `Expected/` **before** deleting a template and its `.cs` output, and do it in the same commit as the emitter.
- Snapshot tests, the full test suite and ApiCompat pass.
- Run `dotnet pack` and check the nupkg: it contains no generator assembly and has no dependency on it.
- Delete the template's `TextTemplatingFileGenerator` item from the csproj in the same slice as the template.

## Slices

SG1 and SG4 can start at once. SG2 and SG3 can run in parallel after SG1. SG5 comes last.

| # | Blocked by | Ships | Done-check |
|---|---|---|---|
| SG1 ([#199](https://github.com/PinataLabs/ImagePastel/issues/199)) | None | `gen/` props and generator project; test project and harness; slnx entries; `Microsoft.CodeAnalysis.CSharp` 4.14.0 in `Directory.Packages.props`; `ImageSharp.csproj` wiring (`Library`); `ISG001`; `SourceWriter`; `GuardNumericEmitter`; `ImageExtensionsSaveEmitter`; coverage exclusion; `.gitattributes` for `Expected/**`; new `CONTRIBUTING.md` section on editing an emitter and refreshing snapshots. Deletes `Guard.Numeric.tt`/`.cs` and `ImageExtensions.Save.tt`/`.cs`. | Snapshot tests pass for both outputs; library builds for `net10.0` and `netstandard2.1`; ApiCompat passes; coverage file list compared and recorded in the PR; nupkg checked |
| SG2 ([#201](https://github.com/PinataLabs/ImagePastel/issues/201)) | SG1 | `PixelTypeNames`; `PixelOperationsEmitter` (14 outputs); `PixelOperationsTestsEmitter`; `ImageSharp.Tests.csproj` wiring (`Tests`). Deletes the 13 per-pixel templates, `PixelOperations{TPixel}.Generated.tt`, the tests template, both `_Common.ttinclude` files and their outputs. | Snapshot tests pass for all 15 outputs; the tests project has no CS0436; the `PixelOperations` test suite passes; ApiCompat passes |
| SG3 ([#202](https://github.com/PinataLabs/ImagePastel/issues/202)) | SG1 | `PixelBlenderNames`; `PixelBlendersEmitter` (2 outputs). Deletes both blender templates and their outputs. | Snapshot tests pass for both outputs; blender tests pass; ApiCompat passes |
| SG4 ([#200](https://github.com/PinataLabs/ImagePastel/issues/200)) | None | `Block8x8F.Generated.cs` renamed to `Block8x8F.Clamp.cs`, its `// <auto-generated />` marker dropped and analyzer warnings fixed. Deletes `Block8x8F.Generated.tt`. Does not touch the generator. | JPEG and `Block8x8F` tests pass; no analyzer suppressions added |
| SG5 ([#203](https://github.com/PinataLabs/ImagePastel/issues/203)) | SG2, SG3, SG4 | Deletes `dotnet-t4` from the tool manifest, `ci-t4.ps1`, the CI `t4` job and its `pack` `needs:` entry, any leftover `TextTemplatingFileGenerator` items, and the T4 paragraph in `CONTRIBUTING.md`. | No `*.tt`, `*.ttinclude`, `TextTemplatingFileGenerator`, `dotnet-t4` or `ci-t4.ps1` reference left in the repo; full CI run passes |
