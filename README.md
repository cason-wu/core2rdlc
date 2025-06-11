# Core2RDLC

A command-line tool and library for programmatically editing RDLC (Report Definition Language Client-side) files by injecting DataSources and DataSets based on .NET assembly types.  
Built with C# 12 and .NET 8.

## Features

- Add or update DataSources and DataSets in RDLC files using reflection from a .NET assembly.
- Supports batch processing of multiple types.
- Option to preview changes before saving.
- Clean and modern codebase targeting .NET 8.

## Usage

### Parameters

- `rdlcPath`  Path to the RDLC file to modify.
- `assemblyPath` Path to the .NET assembly (DLL) containing your data types.
- `typeList` Comma-separated list of type names to inject as DataSets.

### Options

- `--preview` Preview the changes in the console without saving to the RDLC file.

