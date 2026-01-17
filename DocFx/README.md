# DocFX Documentation Build Guide

This folder contains the DocFX configuration for generating API documentation for XrplCSharp.

## Prerequisites

Install DocFX as a .NET global tool:

```bash
dotnet tool install -g docfx
```

## Quick Start

Generate and serve documentation locally:

```bash
cd DocFx
docfx docfx.json --serve
```

Open http://localhost:8080 in your browser.

## Build Commands

### Generate metadata and build documentation

```bash
docfx docfx.json
```

### Generate metadata only (from source code)

```bash
docfx metadata docfx.json
```

### Build HTML from metadata

```bash
docfx build docfx.json
```

### Serve documentation locally

```bash
docfx serve ../docs
```

Or with custom port:

```bash
docfx serve ../docs -p 9000
```

## Clean and Rebuild

If documentation appears outdated or broken, clean the generated files and rebuild:

### Windows (PowerShell)

```powershell
Remove-Item -Recurse -Force reference -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force ..\docs -ErrorAction SilentlyContinue
docfx docfx.json --serve
```

### Windows (CMD)

```cmd
rmdir /s /q reference
rmdir /s /q ..\docs
docfx docfx.json --serve
```

### Linux/macOS

```bash
rm -rf reference ../docs
docfx docfx.json --serve
```

## Folder Structure

```
DocFx/
├── docfx.json          # Main configuration file
├── filterConfig.yml    # API filter rules
├── toc.yml             # Top-level navigation
├── index.md            # Documentation home page
├── Connection-Guide.md # Connection guide (EN)
├── Connection-Guide.ru.md # Connection guide (RU)
├── reference/          # Generated API metadata (YAML)
└── templates/          # Custom templates and styles
    └── unity/
        ├── partials/
        └── styles/

../docs/                # Generated HTML documentation output
```

## Adding New Documentation Pages

1. Create a new `.md` file in `DocFx/` folder
2. Add a link to it in `index.md` or `toc.yml`
3. Rebuild documentation

## Troubleshooting

### No API pages generated

Ensure XML documentation is enabled in all `.csproj` files:

```xml
<PropertyGroup>
  <GenerateDocumentationFile>true</GenerateDocumentationFile>
</PropertyGroup>
```

Then rebuild the projects before running DocFX:

```bash
dotnet build ../Xrpl.sln
docfx docfx.json --serve
```

### Navigation not showing

Clean and rebuild (see "Clean and Rebuild" section above).

### Changes not appearing

DocFX caches metadata. Use `--force` flag or clean the `reference/` folder:

```bash
docfx metadata docfx.json --force
docfx build docfx.json
```
