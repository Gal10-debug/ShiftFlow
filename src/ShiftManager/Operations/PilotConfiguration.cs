using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Npgsql;

namespace ShiftManager.Operations;

public sealed record PilotConfiguration(Guid DeploymentId, string DatabaseName, string Connection,
    Uri Origin, IPAddress Proxy, string DirectoryPath, X509Certificate2 Certificate)
{
    public string Role(string purpose) => $"sf_{DeploymentId:N}_{purpose}";
    public string Confirmation => $"{DeploymentId:N}/{DatabaseName}";

    public static PilotConfiguration? Configure(WebApplicationBuilder builder, string? command, bool demo)
    {
        if (builder.Configuration["PILOT_MODE"] != "1") return null;
        if (!builder.Environment.IsProduction() || demo) throw Invalid("environment");
        string Required(string key) => !string.IsNullOrWhiteSpace(builder.Configuration[key]) ? builder.Configuration[key]! : throw Invalid(key);
        if (!Guid.TryParseExact(Required("PILOT_DEPLOYMENT_ID"), "N", out var id)) throw Invalid("PILOT_DEPLOYMENT_ID");
        var database = Required("PILOT_DATABASE_NAME");
        if (!Regex.IsMatch(database, "^shiftflow_pilot_" + id.ToString("N") + "(?:_restore_[a-f0-9]{6})?$")) throw Invalid("PILOT_DATABASE_NAME");
        if (!Uri.TryCreate(Required("PILOT_PUBLIC_ORIGIN"), UriKind.Absolute, out var origin) || origin.Scheme != "https"
            || origin.AbsolutePath != "/" || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0) throw Invalid("PILOT_PUBLIC_ORIGIN");
        if (!IPAddress.TryParse(Required("PILOT_TRUSTED_PROXY"), out var proxy) || proxy.Equals(IPAddress.Any)
            || proxy.Equals(IPAddress.IPv6Any) || proxy.Equals(IPAddress.None) || proxy.Equals(IPAddress.IPv6None)) throw Invalid("PILOT_TRUSTED_PROXY");
        var connection = Secret(Required("PILOT_DATABASE_CONNECTION_FILE"));
        NpgsqlConnectionStringBuilder settings;
        try { settings = new(connection); } catch { throw Invalid("PILOT_DATABASE_CONNECTION_FILE"); }
        var role = $"sf_{id:N}_{(command == "migrate" ? "migrator" : "runtime")}";
        if (settings.Database != database || settings.Username != role || settings.Host != Required("PILOT_DATABASE_HOST")
            || string.IsNullOrWhiteSpace(settings.Password) || settings.Password.Length < 24 || settings.MaxPoolSize > 20)
            throw Invalid("database identity or pool limit");
        if (settings.Host != "postgres" && settings.SslMode != SslMode.VerifyFull) throw Invalid("database TLS");
        var keys = Required("PILOT_KEYS_DIRECTORY");
        if (!Path.IsPathFullyQualified(keys) || new DirectoryInfo(keys).LinkTarget is not null) throw Invalid("PILOT_KEYS_DIRECTORY");
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(keys, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(keys) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0) throw Invalid("key directory permissions");
        }
        else Directory.CreateDirectory(keys);
        X509Certificate2 certificate;
        try { certificate = X509CertificateLoader.LoadPkcs12FromFile(Required("PILOT_KEY_CERTIFICATE_FILE"),
            Secret(Required("PILOT_KEY_CERTIFICATE_PASSWORD_FILE")), OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet); }
        catch { throw Invalid("Data Protection certificate"); }
        if (!certificate.HasPrivateKey || certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow
            || certificate.NotBefore.ToUniversalTime() > DateTime.UtcNow) throw Invalid("Data Protection certificate validity");
        builder.Configuration["ConnectionStrings:Database"] = connection;
        builder.Configuration["AllowedHosts"] = origin.Host;
        if (command == "bootstrap")
            builder.Configuration["BOOTSTRAP_ADMIN_PASSWORD"] = Secret(Required("PILOT_BOOTSTRAP_PASSWORD_FILE"));
        builder.Services.AddDataProtection().SetApplicationName("ShiftFlow.Pilot." + id.ToString("N"))
            .PersistKeysToFileSystem(new DirectoryInfo(keys)).ProtectKeysWithCertificate(certificate);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1; options.RequireHeaderSymmetry = true;
            options.KnownProxies.Clear(); options.KnownIPNetworks.Clear(); options.KnownProxies.Add(proxy);
        });
        builder.Services.AddHttpsRedirection(options => options.HttpsPort = origin.Port);
        builder.Services.AddHsts(options => { options.MaxAge = TimeSpan.FromDays(30); options.IncludeSubDomains = false; options.Preload = false; });
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options => { options.UseUtcTimestamp = true; options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ"; });
        // SQL text and framework exception payloads are not part of pilot logs.
        builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.None);
        builder.Logging.AddFilter("Microsoft.AspNetCore.Diagnostics", LogLevel.None);
        var result = new PilotConfiguration(id, database, connection, origin, proxy, keys, certificate);
        builder.Services.AddSingleton(result);
        return result;
    }

    private static string Secret(string path)
    {
        try
        {
            if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw Invalid("secret file");
            if (!OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                throw Invalid("secret file permissions");
            var value = File.ReadAllText(path).TrimEnd('\r', '\n');
            return value.Length is > 0 and <= 16384 ? value : throw Invalid("secret file length");
        }
        catch { throw Invalid("required secret file"); }
    }

    private static InvalidOperationException Invalid(string setting) => new("Pilot configuration invalid: " + setting + ".");
}
