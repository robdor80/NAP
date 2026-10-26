using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.Formats.Webp;

// Verification only: no assets, ZIPs, host, persistent database or NAP writer.
if (args.Length != 1 || !OperatingSystem.IsWindows()) return 64;
var publish = Path.GetFullPath(args[0]);
foreach (var variable in new[] { "SIXLABORS_LICENSE_B64", "SIXLABORS_LICENSE_KEY", "SixLaborsLicenseFile", "SixLaborsLicenseKey" })
    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(variable))) throw new InvalidOperationException("Runtime must have no license provisioning variable.");
foreach (var root in new[] { publish, AppContext.BaseDirectory, Path.Combine(Environment.GetEnvironmentVariable("RUNNER_TEMP")!, "nap-sixlabors-license") })
    if (Directory.Exists(root) && Directory.EnumerateFiles(root, "*.lic", SearchOption.AllDirectories).Any())
        throw new InvalidOperationException("Runtime probe requires no private license files.");
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
var imageSharp = typeof(Image).Assembly.Location;
if (Hash(imageSharp) != Hash(Path.Combine(publish, "SixLabors.ImageSharp.dll"))) throw new InvalidOperationException("ImageSharp differs from published library.");
using var image = new Image<Rgba32>(2, 2, new Rgba32(23, 47, 91, 255));
foreach (var encoder in new SixLabors.ImageSharp.Formats.IImageEncoder[] { new PngEncoder(), new WebpEncoder { FileFormat = WebpFileFormatType.Lossless } })
{
    using var bytes = new MemoryStream(); image.Save(bytes, encoder); bytes.Position = 0;
    using var decoded = Image.Load<Rgba32>(bytes);
    if (decoded.Width != 2 || decoded.Height != 2 || decoded[0, 0] != image[0, 0]) throw new InvalidOperationException("Synthetic memory roundtrip failed.");
}
var native = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64", "native", "e_sqlite3.dll");
if (Hash(native) != Hash(Path.Combine(publish, "e_sqlite3.dll"))) throw new InvalidOperationException("SQLite native DLL differs from publication.");
using var db = new SqliteConnection("Data Source=:memory:"); db.Open();
using var command = db.CreateCommand(); command.CommandText = "select sqlite_version()";
Console.WriteLine(JsonSerializer.Serialize(new { runtime = Environment.Version.ToString(), imageSharpSha256 = Hash(imageSharp), pngRoundtrip = true, webpLosslessRoundtrip = true, sqliteVersion = command.ExecuteScalar(), sqliteNativeSha256 = Hash(native), licenseFilePresent = false, persistentWritesRequested = false }));
return 0;
