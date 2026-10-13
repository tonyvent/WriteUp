using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;

namespace WriteUp.Services;

internal static class WindowsAppLaunch
{
    public static string? RegistrationError { get; private set; }
    public static bool HasIdentity
    {
        get { try { _ = Windows.ApplicationModel.Package.Current.Id.Name; return true; } catch { return false; } }
    }

    // Use the same registered app layout as the Windows build, even when the
    // user double-clicks the EXE or starts it from Visual Studio.
    public static async Task<bool> ActivateRegisteredAsync(string arguments = "")
    {
        if (HasIdentity || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 26100)) return false;
        try
        {
            WriteLayout(AppContext.BaseDirectory);
            const string script = """
                $ErrorActionPreference = 'Stop'
                $layout = $env:WriteUpAppLayout
                $manifest = Join-Path $layout 'AppxManifest.xml'
                [xml]$xml = Get-Content -LiteralPath $manifest
                $version = $xml.Package.Identity.Version
                $package = Get-AppxPackage -Name WriteUp.Desktop
                if (-not $package -or $package.Version.ToString() -ne $version -or $package.InstallLocation -ne $layout.TrimEnd('\')) {
                    Add-AppxPackage -Register $manifest
                    $package = Get-AppxPackage -Name WriteUp.Desktop
                }
                if (-not $package) { throw 'Windows did not register the app.' }
                $package.PackageFamilyName
                """;
            var start = new ProcessStartInfo("powershell.exe")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive");
            start.ArgumentList.Add("-EncodedCommand");
            start.ArgumentList.Add(Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script)));
            start.Environment["WriteUpAppLayout"] = AppContext.BaseDirectory;
            using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows app setup could not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch { try { process.Kill(true); } catch { } throw; }
            string family = (await output).Trim();
            if (process.ExitCode != 0) throw new InvalidOperationException(await error);
            if (!family.StartsWith("WriteUp.Desktop_", StringComparison.Ordinal) || family.Any(char.IsWhiteSpace))
                throw new InvalidOperationException("Windows returned an invalid app identity.");
            var manager = (IApplicationActivationManager)Activator.CreateInstance(
                Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C"))!)!;
            try { Marshal.ThrowExceptionForHR(manager.ActivateApplication(family + "!WriteUp", arguments, 0, out _)); }
            finally { Marshal.ReleaseComObject(manager); }
            return true;
        }
        catch (Exception ex)
        {
            RegistrationError = "Windows could not finish installing transcription support for WriteUp.";
            try { File.WriteAllText(Path.Combine(SettingsStore.AppDataDir, "speech-error.txt"), "App registration: " + ex); } catch { }
            return false;
        }
    }

    internal static void WriteLayout(string directory)
    {
        using var manifest = typeof(WindowsAppLaunch).Assembly.GetManifestResourceStream("WriteUp.AppxManifest.xml")
            ?? throw new InvalidOperationException("Windows app manifest is missing from this build.");
        using (var file = File.Create(Path.Combine(directory, "AppxManifest.xml"))) manifest.CopyTo(file);
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/WriteUp;component/Assets/dynamic-logo.png"))
            ?? throw new InvalidOperationException("App icon is missing.");
        using var stream = resource.Stream;
        using var source = System.Drawing.Image.FromStream(stream);
        string assets = Path.Combine(directory, "Assets"); Directory.CreateDirectory(assets);
        foreach (var (name, size) in new[] { ("StoreLogo.png", 50), ("Logo150.png", 150), ("Logo44.png", 44) })
        {
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.Clear(System.Drawing.Color.Transparent);
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            float scale = Math.Min((float)size / source.Width, (float)size / source.Height);
            float width = source.Width * scale, height = source.Height * scale;
            graphics.DrawImage(source, (size-width)/2, (size-height)/2, width, height);
            bitmap.Save(Path.Combine(assets, name), System.Drawing.Imaging.ImageFormat.Png);
        }
    }
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }

}
