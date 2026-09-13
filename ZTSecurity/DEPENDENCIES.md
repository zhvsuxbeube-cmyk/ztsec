# Runtime dependencies

The repository intentionally does **not** store the third-party DLL payloads or the application map asset.

Before building or running ZTSecurity, prepare the external inputs with:

`python scripts/prepare_dependencies.py`

Run it from the directory containing `ZTSecurity/`. The script recursively searches the surrounding input tree for manifest-approved DLLs (including Costura `.compressed` payloads), validates them against `scripts/dependency_manifest.json`, and installs them into `ZTSecurity/dependencies/` as compile-time inputs. The Release build copies the validated DLLs beside the executable; the final runtime package contains no `dependencies/` directory.

The same preparation step also installs manifest-declared non-DLL runtime assets, including `world-administrative-boundaries.shp`, into the project root.

Only the manifest-approved files are accepted. Existing prepared files are replaced atomically when valid source inputs are available.

`dependencies/.gitkeep` exists only to preserve the expected directory in source control; the actual DLLs are externally supplied.
