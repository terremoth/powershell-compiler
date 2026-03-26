# PowerShell Compiler
Yet Another PowerShell 5.1+ Compiler to EXE.  
Fully vibe-coded with Claude (Sonnet 4.6)

Compiles a PowerShell script (.ps1) into a standalone Windows EXE.

The generated EXE:
  - Embeds your script as Base64 inside the binary
  - Extracts it to %TEMP% at runtime, runs it via powershell.exe, then deletes it
  - Requires NO extra files, SDKs, or runtimes on the target machine
    (only needs powershell.exe, which ships with every Windows 7+)
  - Runs without a visible terminal window by default (GUI-safe)


## HOW TO BUILD ps1compiler.exe

Requirements: .NET Framework (installed by default on Windows 7+)
No Visual Studio or SDK needed.

  1. Double-click BUILD.bat  (or run it from a command prompt)
  2. Done. ps1compiler.exe appears in the same folder.

Alternatively, if you know where csc.exe is:
  "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /optimize+ /target:exe /out:ps1compiler.exe ps1compiler.cs


## USAGE

`ps1compiler.exe <script.ps1> [output.exe] [options]`

Options:
  --icon     <file.ico>        Embed an icon into the generated EXE
  --manifest <file.manifest>   Embed an application manifest (UAC, DPI, etc.)
  --no-hidden                  Show the PowerShell terminal window when running
                               (default: hidden, good for GUI scripts)

### Examples:

```sh
ps1compiler.exe myscript.ps1
```
Generates myscript.exe (no terminal window)

```sh
  ps1compiler.exe myscript.ps1 --icon myapp.ico
```
Generates myscript.exe with custom icon

```sh
  ps1compiler.exe myscript.ps1 out.exe --icon app.ico --manifest app.manifest
```
Generates out.exe with icon + manifest

```sh
  ps1compiler.exe myscript.ps1 --no-hidden
```
Generates myscript.exe, terminal stays visible (useful for CLI scripts)


## MANIFEST NOTES

Use a manifest to control:
  - UAC elevation: change "asInvoker" to "requireAdministrator" for admin prompt
  - DPI awareness: PerMonitorV2 makes GUI apps sharp on high-DPI screens
  - Visual styles: enables modern Windows controls (buttons, scrollbars, etc.)

An example manifest is provided: example.manifest


## HOW IT WORKS INTERNALLY

1. ps1compiler reads your .ps1, encodes it as Base64
2. Generates a small C# stub that contains the Base64 blob
3. Compiles the stub with csc.exe (the C# compiler in .NET Framework)
4. The resulting EXE, when run:
   a. Decodes the Base64 back to bytes
   b. Writes a temp .ps1 file to `%TEMP%`
   c. Runs: `powershell.exe -NonInteractive -WindowStyle Hidden -ExecutionPolicy Bypass -File <temp>`
   d. Waits for it to finish, deletes the temp file
   e. Returns the same exit code

PowerShell 7 (pwsh.exe) is used automatically if installed; otherwise falls
back to the built-in Windows PowerShell 5.x.


## LIMITATIONS

- The generated EXE is x86/AnyCPU - works on 32-bit and 64-bit Windows
- The script runs with ExecutionPolicy Bypass, so no policy changes needed
- If your script uses $PSScriptRoot, it will point to %TEMP% (where the
  extracted .ps1 lives). For scripts that need to reference sibling files,
  bundle everything into the .ps1 or use a different distribution method.
- Antivirus software may flag generic PS1-runner stubs as suspicious.
  This is a false positive common to all PS1-to-EXE tools.
