using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using ForgeConflict.Core.Logging;
using ForgeConflict.Core.Update;

namespace ForgeConflict.Tests.Update;

public sealed class UpdateTests : IDisposable
{
    private const string GoodUrl = UpdateVerifier.ChannelPrefix + "v9.9.9/ForgeConflict.exe";

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "fc-update-" + Guid.NewGuid().ToString("N"));

    public UpdateTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        _key.Dispose();
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string PublicKey()
    {
        var p = _key.ExportParameters(false);
        return Convert.ToBase64String([.. p.Q.X!, .. p.Q.Y!]);
    }

    private (byte[] Manifest, byte[] Sig) Signed(string version = "9.9.9", string url = GoodUrl, long size = 10,
        string? sha = null)
    {
        sha ??= new string('a', 64);
        var json = $$"""{"version":"{{version}}","notes":"Nouveautés","url":"{{url}}","size":{{size}},"sha256":"{{sha}}"}""";
        var bytes = Encoding.UTF8.GetBytes(json);
        return (bytes, Encoding.ASCII.GetBytes(Convert.ToBase64String(_key.SignData(bytes, HashAlgorithmName.SHA256))));
    }

    [Fact]
    public void ValidManifestIsAccepted()
    {
        var (m, s) = Signed();
        var info = UpdateVerifier.Parse(m, s, PublicKey());
        Assert.Equal("9.9.9", info.Version);
        Assert.Equal("Nouveautés", info.Notes);
    }

    [Fact]
    public void TamperedManifestIsRejected()
    {
        var (m, s) = Signed();
        m[10] ^= 1;
        Assert.Throws<UpdateException>(() => UpdateVerifier.Parse(m, s, PublicKey()));
    }

    [Fact]
    public void SignatureFromAnotherKeyIsRejected()
    {
        var (m, s) = Signed();
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = other.ExportParameters(false);
        var otherKey = Convert.ToBase64String([.. p.Q.X!, .. p.Q.Y!]);
        Assert.Throws<UpdateException>(() => UpdateVerifier.Parse(m, s, otherKey));
    }

    [Fact]
    public void GarbageSignatureIsRejectedWithoutCrashing()
    {
        var (m, _) = Signed();
        Assert.Throws<UpdateException>(() => UpdateVerifier.Parse(m, "pas du base64 !"u8.ToArray(), PublicKey()));
    }

    [Theory]
    [InlineData("https://evil.example/Heiphaistos/ForgeConflict/releases/download/v9/ForgeConflict.exe")]
    [InlineData("http://github.com/Heiphaistos/ForgeConflict/releases/download/v9/ForgeConflict.exe")]
    [InlineData("https://github.com/Heiphaistos/Autre/releases/download/v9/ForgeConflict.exe")]
    [InlineData("https://github.com/Heiphaistos/ForgeConflict/releases/download/../../../evil/x.exe")]
    [InlineData("https://github.com.evil.example/Heiphaistos/ForgeConflict/releases/download/v9/x.exe")]
    [InlineData("https://user@github.com/Heiphaistos/ForgeConflict/releases/download/v9/x.exe")]
    [InlineData("https://github.com/Heiphaistos/ForgeConflict/releases/download/v9/x.exe?a=b")]
    [InlineData("")]
    public void UrlOutsideChannelIsRefused(string url)
    {
        Assert.False(UpdateVerifier.IsAllowedUrl(url));
        var (m, s) = Signed(url: url);
        Assert.Throws<UpdateException>(() => UpdateVerifier.Parse(m, s, PublicKey()));
    }

    [Fact]
    public void ChannelUrlIsAccepted() => Assert.True(UpdateVerifier.IsAllowedUrl(GoodUrl));

    [Theory]
    [InlineData("1.2.0", "1.2.1", true)]
    [InlineData("1.2.0", "1.10.0", true)]
    [InlineData("1.2.0", "1.2.0", false)]
    [InlineData("1.2.0", "1.1.9", false)]
    [InlineData("1.2.0", "v2.0.0", true)]
    [InlineData("1.2.0", "n'importe quoi", false)]
    public void VersionComparison(string current, string candidate, bool expected) =>
        Assert.Equal(expected, UpdateVerifier.IsNewer(current, candidate));

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) =>
            Task.FromResult(reply(r));
    }

    private AppUpdater Updater(byte[] manifest, byte[] sig, byte[]? exe = null) =>
        new(new HttpClient(new FakeHandler(r =>
            {
                var path = r.RequestUri!.AbsoluteUri;
                var body = path.EndsWith(".sig") ? sig : path.EndsWith("latest.json") ? manifest : exe ?? [];
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };
            })),
            new NullLog(), "https://example.test/latest.json", PublicKey());

    private sealed class NullLog : ILog
    {
        public void Write(LogLevel level, string message, Exception? exception = null) { }
    }

    [Fact]
    public async Task CheckReturnsNullWhenUpToDateAndInfoWhenNewer()
    {
        var (m, s) = Signed(version: "1.2.0");
        var updater = Updater(m, s);
        Assert.Null(await updater.CheckAsync("1.2.0"));
        Assert.Equal("1.2.0", (await updater.CheckAsync("1.1.0"))!.Version);
    }

    [Fact]
    public async Task DownloadWithWrongHashIsRejectedAndDeleted()
    {
        var exe = new byte[10];
        var (m, s) = Signed(size: 10, sha: new string('b', 64));
        var info = UpdateVerifier.Parse(m, s, PublicKey());
        var target = Path.Combine(_dir, "ForgeConflict.exe");

        var ex = await Assert.ThrowsAsync<UpdateException>(() => Updater(m, s, exe).DownloadAsync(info, target));
        Assert.Contains("SHA-256", ex.Message);
        Assert.False(File.Exists(target + ".new"));
    }

    [Fact]
    public async Task DownloadWithWrongSizeIsRejected()
    {
        var exe = new byte[12];
        var sha = Convert.ToHexString(SHA256.HashData(exe));
        var (m, s) = Signed(size: 10, sha: sha);
        var info = UpdateVerifier.Parse(m, s, PublicKey());
        var target = Path.Combine(_dir, "ForgeConflict.exe");

        await Assert.ThrowsAsync<UpdateException>(() => Updater(m, s, exe).DownloadAsync(info, target));
        Assert.False(File.Exists(target + ".new"));
    }

    [Fact]
    public async Task DownloadWithMatchingHashSucceedsWhenCurrentExeIsUnsigned()
    {
        var exe = new byte[10];
        exe[3] = 7;
        var (m, s) = Signed(size: 10, sha: Convert.ToHexString(SHA256.HashData(exe)).ToLowerInvariant());
        var info = UpdateVerifier.Parse(m, s, PublicKey());
        var target = Path.Combine(_dir, "ForgeConflict.exe");
        await File.WriteAllBytesAsync(target, [1, 2, 3]); // exe « courant » : non signe, controle Authenticode ignore

        var path = await Updater(m, s, exe).DownloadAsync(info, target);

        Assert.Equal(target + ".new", path);
        Assert.Equal(exe, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public void ScriptWaitsReplacesRelaunchesAndEscapesPercent()
    {
        var script = AppUpdater.BuildScript(@"C:\Mes 100% apps\F.exe.new", @"C:\Mes 100% apps\F.exe");
        Assert.Contains("move /y \"C:\\Mes 100%% apps\\F.exe.new\" \"C:\\Mes 100%% apps\\F.exe\"", script);
        Assert.Contains("if %N% GEQ 90 goto fin", script);
        Assert.Contains("start \"\" /d \"C:\\Mes 100%% apps\" \"C:\\Mes 100%% apps\\F.exe\"", script);
        Assert.Contains("del \"%~f0\"", script);
        Assert.True(script.IndexOf("move /y", StringComparison.Ordinal) < script.IndexOf("start \"\"", StringComparison.Ordinal));
    }

    [Fact]
    public void ScriptReallyReplacesTheFileWhenRun()
    {
        var exe = Path.Combine(_dir, "app test.exe");
        File.WriteAllText(exe, "ancien");
        File.WriteAllText(exe + ".new", "nouveau");
        var cmd = Path.Combine(_dir, "run.cmd");
        // « start » est retire : on ne relance pas un faux exe, on verifie le remplacement.
        var script = AppUpdater.BuildScript(exe + ".new", exe).Replace("start \"\"", "rem ");
        File.WriteAllText(cmd, script);

        using var p = Process.Start(new ProcessStartInfo("cmd.exe", $"/d /c \"\"{cmd}\"\"")
            { UseShellExecute = false, CreateNoWindow = true })!;
        Assert.True(p.WaitForExit(30_000));

        Assert.Equal("nouveau", File.ReadAllText(exe));
        Assert.False(File.Exists(exe + ".new"));
    }

    [Fact]
    public void AuthenticodeDecision()
    {
        var cur = new AuthenticodeGuard.Signature("UnknownError", "AAA", "CN=Heiphaistos");
        Assert.Null(AuthenticodeGuard.Decide(cur, new("UnknownError", "aaa", "CN=Heiphaistos")));
        Assert.NotNull(AuthenticodeGuard.Decide(cur, new("NotSigned", "", "")));
        Assert.NotNull(AuthenticodeGuard.Decide(cur, new("Valid", "BBB", "CN=Autre")));
        Assert.NotNull(AuthenticodeGuard.Decide(cur, new("HashMismatch", "AAA", "CN=Heiphaistos")));
        Assert.NotNull(AuthenticodeGuard.Decide(cur, null));
        Assert.Equal("Valid", AuthenticodeGuard.Parse("Valid|ABC|CN=X, O=Y\r\n")!.Status);
    }

    [Fact]
    public void SettingsDefaultOnAndPersist()
    {
        var path = Path.Combine(_dir, "set.json");
        Assert.True(new UpdateSettings(path).AutoCheck);
        Assert.True(new UpdateSettings(path).SetAutoCheck(false));
        Assert.False(new UpdateSettings(path).AutoCheck);
        File.WriteAllText(path, "{corrompu");
        Assert.True(new UpdateSettings(path).AutoCheck);
    }

    [Fact]
    public void PublishScriptSignatureVerifiesWithCSharp()
    {
        var repo = AppContext.BaseDirectory;
        while (repo is not null && !File.Exists(Path.Combine(repo, "tools", "publish-update.ps1")))
            repo = Path.GetDirectoryName(repo);
        Assert.NotNull(repo);

        // Cle de test au format de la vraie : PKCS#8 en base64.
        var keyFile = Path.Combine(_dir, "test.key");
        File.WriteAllText(keyFile, Convert.ToBase64String(_key.ExportPkcs8PrivateKey()));
        var exe = Path.Combine(_dir, "ForgeConflict.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "whoami.exe"), exe);

        var psi = new ProcessStartInfo("powershell.exe",
            $"-NoProfile -ExecutionPolicy Bypass -File \"{Path.Combine(repo!, "tools", "publish-update.ps1")}\" " +
            $"-Exe \"{exe}\" -Key \"{keyFile}\" -Changelog \"{Path.Combine(_dir, "absent.md")}\"")
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        Assert.True(p.WaitForExit(60_000));
        Assert.True(p.ExitCode == 0, output);

        var info = UpdateVerifier.Parse(File.ReadAllBytes(Path.Combine(_dir, "latest.json")),
            File.ReadAllBytes(Path.Combine(_dir, "latest.json.sig")), PublicKey());
        Assert.Equal(new FileInfo(exe).Length, info.Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe))).ToLowerInvariant(), info.Sha256);
        Assert.Equal(UpdateVerifier.ChannelPrefix + $"v{info.Version}/ForgeConflict.exe", info.Url);
    }
}
