# Bundled wheels

These let ProcWatch install and build with no internet connection.
`run.bat` and `build_exe.bat` install from this folder with `pip --no-index`.

* `psutil-*-win_amd64.whl`, `psutil-*-win_arm64.whl`: the only library the app
  needs, for Intel/AMD and ARM Windows PCs, Python 3.7 and newer.
* `build/`: PyInstaller and its dependencies, used only by `build_exe.bat` to
  turn the app into a single `ProcWatch.exe` (64-bit Windows, Python 3.10 and newer).

All files are unmodified wheels downloaded from pypi.org.
