# ZTSecurity Windows build

ZTSecurity targets .NET Framework 4.7.2 and should be built on Windows with MSBuild/Visual Studio tooling.

The repository intentionally excludes externally supplied runtime payloads. Before a local build, prepare those inputs with:

`python scripts/prepare_dependencies.py`

Run the script from the directory containing `ZTSecurity/`. It validates manifest-approved DLLs and runtime assets and places them into the locations expected by the project.

The source tree contains the dependency manifest and preparation tooling, but not the third-party DLL payloads or `world-administrative-boundaries.shp` itself. The prepared DLLs are used as compile inputs under `dependencies/`; the Release output and packaged runtime are flat with all runtime DLLs beside the executable.
