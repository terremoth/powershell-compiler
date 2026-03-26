// ps1compiler.cs — Compiles a .ps1 script into a standalone .exe
// Build: see BUILD.bat
// Usage: ps1compiler.exe script.ps1 [--icon icon.ico] [--manifest app.manifest] [--console] [--no-hidden]
//
// Requirements: .NET Framework 3.5+ (present on every Windows since Vista/7).
// The generated .exe requires only powershell.exe on the target machine,
// which ships with every Windows since XP SP2 (PS1) / Win7 (PS2+).

using System;
using System.IO;
using System.Reflection;
using System.Text;

class PS1Compiler
{
    static int Main(string[] args)
    {
        Console.WriteLine("ps1compiler — PowerShell to EXE compiler");
        Console.WriteLine("==========================================");

        // ── Parse arguments ─────────────────────────────────────────────────
        string inputPs1   = null;
        string outputExe  = null;
        string iconPath   = null;
        string manifestPath = null;
        bool   noHidden   = false; // --no-hidden: show terminal (console mode)

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == "--icon"     && i + 1 < args.Length) { iconPath     = args[++i]; continue; }
            if (a == "--manifest" && i + 1 < args.Length) { manifestPath = args[++i]; continue; }
            if (a == "--no-hidden") { noHidden = true; continue; }
            if (a.StartsWith("-"))  { Console.Error.WriteLine("Unknown option: " + a); return 1; }
            if (inputPs1  == null)  { inputPs1  = a; continue; }
            if (outputExe == null)  { outputExe = a; continue; }
            Console.Error.WriteLine("Unexpected argument: " + a); return 1;
        }

        if (inputPs1 == null)
        {
            Console.WriteLine();
            Console.WriteLine("Usage:");
            Console.WriteLine("  ps1compiler.exe <script.ps1> [output.exe] [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --icon     <icon.ico>      Embed icon into the output EXE");
            Console.WriteLine("  --manifest <app.manifest>  Embed application manifest");
            Console.WriteLine("  --no-hidden                Show terminal/console window when running");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  ps1compiler.exe myscript.ps1");
            Console.WriteLine("  ps1compiler.exe myscript.ps1 --icon myicon.ico");
            Console.WriteLine("  ps1compiler.exe myscript.ps1 out.exe --icon app.ico --manifest app.manifest");
            return 1;
        }

        // ── Validate input ───────────────────────────────────────────────────
        inputPs1 = Path.GetFullPath(inputPs1);
        if (!File.Exists(inputPs1))
        {
            Console.Error.WriteLine("ERROR: Input file not found: " + inputPs1);
            return 1;
        }

        if (outputExe == null)
            outputExe = Path.ChangeExtension(inputPs1, ".exe");
        else
            outputExe = Path.GetFullPath(outputExe);

        if (iconPath != null)
        {
            iconPath = Path.GetFullPath(iconPath);
            if (!File.Exists(iconPath))
            { Console.Error.WriteLine("ERROR: Icon file not found: " + iconPath); return 1; }
        }

        if (manifestPath != null)
        {
            manifestPath = Path.GetFullPath(manifestPath);
            if (!File.Exists(manifestPath))
            { Console.Error.WriteLine("ERROR: Manifest file not found: " + manifestPath); return 1; }
        }

        // ── Read and encode the PS1 ──────────────────────────────────────────
        Console.WriteLine("Reading: " + inputPs1);
        string ps1Content = File.ReadAllText(inputPs1, Encoding.UTF8);
        // Base64-encode so we can safely embed any content in a C# string literal
        string ps1B64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(ps1Content));

        // ── Locate csc.exe ───────────────────────────────────────────────────
        string cscPath = FindCsc();
        if (cscPath == null)
        {
            Console.Error.WriteLine("ERROR: Could not find csc.exe (C# compiler).");
            Console.Error.WriteLine("       Make sure .NET Framework is installed (it comes with Windows).");
            return 1;
        }
        Console.WriteLine("Compiler: " + cscPath);

        // ── Generate stub source ─────────────────────────────────────────────
        string stubSource = BuildStubSource(ps1B64, noHidden);

        // ── Write stub to temp ───────────────────────────────────────────────
        string tempDir  = Path.Combine(Path.GetTempPath(), "ps1compiler_" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(tempDir);
        string tempCs   = Path.Combine(tempDir, "stub.cs");
        File.WriteAllText(tempCs, stubSource, Encoding.UTF8);

        // ── Build csc arguments ──────────────────────────────────────────────
        // /target:winexe  → no console window for the compiler stub itself
        // /target:exe     → console window (--no-hidden mode)
        string target  = noHidden ? "exe" : "winexe";
        var    cscArgs = new StringBuilder();
        cscArgs.Append("/nologo ");
        cscArgs.AppendFormat("/target:{0} ", target);
        cscArgs.AppendFormat("/out:\"{0}\" ", outputExe);
        cscArgs.AppendFormat("/optimize+ ");

        if (iconPath != null)
            cscArgs.AppendFormat("/win32icon:\"{0}\" ", iconPath);
        if (manifestPath != null)
            cscArgs.AppendFormat("/win32manifest:\"{0}\" ", manifestPath);

        cscArgs.AppendFormat("\"{0}\"", tempCs);

        // ── Run csc ──────────────────────────────────────────────────────────
        Console.WriteLine("Compiling...");
        string cscOut, cscErr;
        int exitCode = RunProcess(cscPath, cscArgs.ToString(), out cscOut, out cscErr);

        // Clean up temp
        try { Directory.Delete(tempDir, true); } catch { }

        if (!string.IsNullOrWhiteSpace(cscOut)) Console.WriteLine(cscOut);
        if (!string.IsNullOrWhiteSpace(cscErr)) Console.Error.WriteLine(cscErr);

        if (exitCode != 0 || !File.Exists(outputExe))
        {
            Console.Error.WriteLine("ERROR: Compilation failed (csc exit code " + exitCode + ").");
            return 1;
        }

        long size = new FileInfo(outputExe).Length;
        Console.WriteLine();
        Console.WriteLine("SUCCESS: " + outputExe);
        Console.WriteLine("Size   : " + (size / 1024) + " KB");
        Console.WriteLine();
        Console.WriteLine("The generated EXE embeds your script and runs it via powershell.exe.");
        Console.WriteLine("No extra files or SDKs needed on the target machine.");
        return 0;
    }

    // ── Stub template ────────────────────────────────────────────────────────
    // The stub extracts the embedded PS1 to %TEMP%, runs it via powershell.exe, then deletes it.
    // We build the source with a StringBuilder so there is no fragile verbatim/interpolation mixing.
    // Base64 is safe to embed as a C# string literal — it never contains quotes or backslashes.
    static string BuildStubSource(string ps1B64, bool noHidden)
    {
        string createNoWindow = noHidden ? "false" : "true";
        // The -WindowStyle Hidden flag suppresses the blue PS window.
        // We put it in the fixed prefix so it is a plain C# string literal in the stub.
        string windowStyle = noHidden ? "" : " -WindowStyle Hidden";

        var s = new StringBuilder();
        s.AppendLine("using System;");
        s.AppendLine("using System.Diagnostics;");
        s.AppendLine("using System.IO;");
        s.AppendLine("using System.Text;");
        s.AppendLine();
        s.AppendLine("class PS1Stub");
        s.AppendLine("{");
        // Embed the base64 blob — safe: base64 alphabet has no C# string special chars
        s.AppendLine("    const string PS1_B64 = \"" + ps1B64 + "\";");
        s.AppendLine();
        s.AppendLine("    static int Main(string[] args)");
        s.AppendLine("    {");
        s.AppendLine("        byte[] bytes = Convert.FromBase64String(PS1_B64);");
        s.AppendLine();
        s.AppendLine("        // Extract to a unique temp file");
        s.AppendLine("        string tmp = Path.Combine(");
        s.AppendLine("            Path.GetTempPath(),");
        s.AppendLine("            \"ps1stub_\" + Guid.NewGuid().ToString(\"N\").Substring(0, 12) + \".ps1\");");
        s.AppendLine("        File.WriteAllBytes(tmp, bytes);");
        s.AppendLine();
        s.AppendLine("        // Build powershell argument string");
        s.AppendLine("        var sb = new StringBuilder();");
        // Fixed flags — windowStyle already resolved at compile-of-compiler time
        s.AppendLine("        sb.Append(\"-NonInteractive" + windowStyle + " -ExecutionPolicy Bypass -File \\\"\");");
        s.AppendLine("        sb.Append(tmp);");
        s.AppendLine("        sb.Append(\"\\\"\");");
        s.AppendLine();
        s.AppendLine("        // Forward any CLI arguments to the script");
        s.AppendLine("        foreach (string a in args)");
        s.AppendLine("        {");
        s.AppendLine("            sb.Append(\" \\\"\");");
        s.AppendLine("            sb.Append(a.Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"\\\"\", \"\\\\\\\"\"));");
        s.AppendLine("            sb.Append(\"\\\"\");");
        s.AppendLine("        }");
        s.AppendLine();
        s.AppendLine("        string psExe = FindPowerShell();");
        s.AppendLine();
        s.AppendLine("        var psi = new ProcessStartInfo");
        s.AppendLine("        {");
        s.AppendLine("            FileName        = psExe,");
        s.AppendLine("            Arguments       = sb.ToString(),");
        s.AppendLine("            UseShellExecute = false,");
        s.AppendLine("            CreateNoWindow  = " + createNoWindow + "");
        s.AppendLine("        };");
        s.AppendLine();
        s.AppendLine("        int code = 0;");
        s.AppendLine("        try");
        s.AppendLine("        {");
        s.AppendLine("            using (var proc = Process.Start(psi))");
        s.AppendLine("            {");
        s.AppendLine("                proc.WaitForExit();");
        s.AppendLine("                code = proc.ExitCode;");
        s.AppendLine("            }");
        s.AppendLine("        }");
        s.AppendLine("        finally { try { File.Delete(tmp); } catch { } }");
        s.AppendLine("        return code;");
        s.AppendLine("    }");
        s.AppendLine();
        s.AppendLine("    static string FindPowerShell()");
        s.AppendLine("    {");
        s.AppendLine("        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);");
        s.AppendLine("        string[] candidates = {");
        s.AppendLine("            Path.Combine(pf, \"PowerShell\", \"7\",  \"pwsh.exe\"),");
        s.AppendLine("            Path.Combine(pf, \"PowerShell\", \"6\",  \"pwsh.exe\"),");
        s.AppendLine("            \"C:\\\\Windows\\\\System32\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\",");
        s.AppendLine("            \"C:\\\\Windows\\\\SysWOW64\\\\WindowsPowerShell\\\\v1.0\\\\powershell.exe\"");
        s.AppendLine("        };");
        s.AppendLine("        foreach (string c in candidates)");
        s.AppendLine("            if (File.Exists(c)) return c;");
        s.AppendLine("        return \"powershell.exe\"; // rely on PATH");
        s.AppendLine("    }");
        s.AppendLine("}");
        return s.ToString();
    }

    // ── Find csc.exe ─────────────────────────────────────────────────────────
    static string FindCsc()
    {
        // 1. Same directory as ps1compiler.exe
        string self = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string local = Path.Combine(self, "csc.exe");
        if (File.Exists(local)) return local;

        // 2. .NET Framework installations (newest first)
        string windir = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        string fwRoot = Path.Combine(windir, "Microsoft.NET", "Framework");
        string fwRoot64 = Path.Combine(windir, "Microsoft.NET", "Framework64");

        string found = SearchFrameworkDir(fwRoot64) ?? SearchFrameworkDir(fwRoot);
        if (found != null) return found;

        // 3. PATH
        string pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (string dir in pathEnv.Split(';'))
        {
            try
            {
                string candidate = Path.Combine(dir.Trim(), "csc.exe");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
        }
        return null;
    }

    static string SearchFrameworkDir(string root)
    {
        if (!Directory.Exists(root)) return null;
        // Enumerate version subdirectories, pick the highest v4 or v3 or v2
        string[] dirs;
        try { dirs = Directory.GetDirectories(root, "v*"); }
        catch { return null; }
        // Sort descending
        Array.Sort(dirs);
        Array.Reverse(dirs);
        foreach (string d in dirs)
        {
            string csc = Path.Combine(d, "csc.exe");
            if (File.Exists(csc)) return csc;
        }
        return null;
    }

    // ── Run a process, capture output ────────────────────────────────────────
    static int RunProcess(string exe, string arguments, out string stdout, out string stderr)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = exe,
            Arguments              = arguments,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true
        };
        using (var proc = System.Diagnostics.Process.Start(psi))
        {
            stdout = proc.StandardOutput.ReadToEnd();
            stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit();
            return proc.ExitCode;
        }
    }
}
