using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Pgan.PoracleWebNet.Core.Abstractions.Services;
using Pgan.PoracleWebNet.Core.Models;

namespace Pgan.PoracleWebNet.Core.Services;

public partial class PoracleServerService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PoracleServerService> logger) : IPoracleServerService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PoracleServerService> _logger = logger;
    private readonly string _sshKeyPath = configuration["Poracle:SshKeyPath"] ?? "/app/ssh_key";

    private sealed record ServerConfig(string Name, string Host, string ApiAddress, string SshUser, string RestartCommand, string GroupMapPath);

    [GeneratedRegex(@"^[a-zA-Z0-9._\-]+$")]
    private static partial Regex SafeHostnameRegex();

    private List<ServerConfig> GetServers()
    {
        var servers = new List<ServerConfig>();
        var section = configuration.GetSection("Poracle:Servers");

        foreach (var child in section.GetChildren())
        {
            var name = child["Name"] ?? string.Empty;
            var host = child["Host"] ?? string.Empty;
            var apiAddress = child["ApiAddress"] ?? string.Empty;
            var sshUser = child["SshUser"] ?? "root";
            var restartCommand = child["RestartCommand"] ?? "pm2 restart all";
            var groupMapPath = child["GroupMapPath"] ?? "/source/PoracleJS/config/group_map.json";

            if (!string.IsNullOrWhiteSpace(host))
            {
                servers.Add(new ServerConfig(name, host, apiAddress, sshUser, restartCommand, groupMapPath));
            }
        }

        return servers;
    }

    private static void ValidateHostname(string value, string paramName)
    {
        if (!SafeHostnameRegex().IsMatch(value))
        {
            throw new ArgumentException($"Invalid characters in {paramName}: '{value}'", paramName);
        }
    }

    public async Task<List<PoracleServerStatus>> GetServersAsync()
    {
        var servers = this.GetServers();

        var tasks = servers.Select(async server =>
        {
            var status = new PoracleServerStatus
            {
                Name = server.Name,
                Host = server.Host,
            };

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var response = await this._httpClient.GetAsync(
                    $"{server.ApiAddress}/api/config/poracleWeb", cts.Token);
                // Any HTTP response means the server is running (even 401/403 auth errors)
                status.Online = true;
            }
            catch
            {
                status.Online = false;
            }

            status.CheckedAt = DateTime.UtcNow;
            return status;
        });

        var results = await Task.WhenAll(tasks);
        return [.. results];
    }

    public async Task<PoracleServerStatus> RestartServerAsync(string host)
    {
        var servers = this.GetServers();
        var server = servers.FirstOrDefault(s =>
            s.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Server with host '{host}' not found in configuration.");

        // Validate host and SSH user to prevent command injection
        ValidateHostname(server.Host, nameof(host));
        ValidateHostname(server.SshUser, "sshUser");

        var status = new PoracleServerStatus
        {
            Name = server.Name,
            Host = server.Host,
        };

        try
        {
            var sshArgs = $"-o StrictHostKeyChecking=no -o ConnectTimeout=10 -o SendEnv=none -i {this._sshKeyPath} {server.SshUser}@{server.Host} \"{server.RestartCommand}\"";

            LogExecutingSshRestart(this._logger, server.Host);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "ssh",
                Arguments = sshArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Clear environment to prevent Windows PATH from leaking to remote shell
            process.StartInfo.Environment.Clear();
            process.StartInfo.Environment["PATH"] = "/usr/bin:/usr/local/bin:/bin";
            process.StartInfo.Environment["HOME"] = "/home/appuser";

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var exited = await process.WaitForExitAsync(cts.Token)
                .ContinueWith(t => !t.IsCanceled);

            if (!exited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch { /* best effort */ }
                status.Online = false;
                status.Message = "SSH command timed out after 30 seconds";
                LogSshRestartTimedOut(this._logger, server.Host);
            }
            else
            {
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var output = $"{stdout}\n{stderr}".Trim();

                status.Online = process.ExitCode == 0;
                status.Message = output;

                LogSshRestartCompleted(this._logger, server.Host, process.ExitCode, output);
            }
        }
        catch (Exception ex)
        {
            status.Online = false;
            status.Message = $"Failed to execute SSH command: {ex.Message}";
            LogRestartFailed(this._logger, ex, server.Host);
        }

        status.CheckedAt = DateTime.UtcNow;
        return status;
    }

    public async Task<List<PoracleServerStatus>> RestartAllAsync()
    {
        var servers = this.GetServers();
        var statuses = new List<PoracleServerStatus>();

        foreach (var server in servers)
        {
            var status = await this.RestartServerAsync(server.Host);
            statuses.Add(status);
        }

        return statuses;
    }

    public async Task UpdateGroupMapAsync(string geofenceName, string group)
    {
        var servers = this.GetServers();

        foreach (var server in servers)
        {
            try
            {
                ValidateHostname(server.Host, "host");
                ValidateHostname(server.SshUser, "sshUser");

                // Encode the key/value as base64 JSON to avoid shell/Python injection
                var payloadJson = JsonSerializer.Serialize(new
                {
                    key = geofenceName,
                    value = group,
                    path = server.GroupMapPath
                });
                var payloadBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(payloadJson));
                var updateScript = $"python3 -c \\\"import json,base64,sys; p=json.loads(base64.b64decode('{payloadBase64}')); f=p['path']; d=json.load(open(f)); d[p['key']]=p['value']; json.dump(d,open(f,'w'),indent=2)\\\"";

                var sshArgs = $"-o StrictHostKeyChecking=no -o ConnectTimeout=10 -o SendEnv=none -i {this._sshKeyPath} {server.SshUser}@{server.Host} \"{updateScript}\"";

                using var process = new Process();
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "ssh",
                    Arguments = sshArgs,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                process.StartInfo.Environment.Clear();
                process.StartInfo.Environment["PATH"] = "/usr/bin:/usr/local/bin:/bin";
                process.StartInfo.Environment["HOME"] = "/home/appuser";

                process.Start();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await process.WaitForExitAsync(cts.Token);

                if (process.ExitCode == 0)
                {
                    LogGroupMapUpdated(this._logger, server.Host, geofenceName, group);
                }
                else
                {
                    var stderr = await process.StandardError.ReadToEndAsync();
                    LogGroupMapUpdateFailed(this._logger, server.Host, stderr);
                }
            }
            catch (Exception ex)
            {
                LogGroupMapUpdateException(this._logger, ex, server.Host);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Executing SSH restart command for server {Host}")]
    private static partial void LogExecutingSshRestart(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Warning, Message = "SSH restart timed out for server {Host}")]
    private static partial void LogSshRestartTimedOut(ILogger logger, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "SSH restart for server {Host} completed with exit code {ExitCode}: {Output}")]
    private static partial void LogSshRestartCompleted(ILogger logger, string host, int exitCode, string output);

    [LoggerMessage(Level = LogLevel.Error, Message = "Failed to restart server {Host} via SSH")]
    private static partial void LogRestartFailed(ILogger logger, Exception ex, string host);

    [LoggerMessage(Level = LogLevel.Information, Message = "Updated group_map.json on {Host}: {Name} -> {Group}")]
    private static partial void LogGroupMapUpdated(ILogger logger, string host, string name, string group);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to update group_map.json on {Host}: {Error}")]
    private static partial void LogGroupMapUpdateFailed(ILogger logger, string host, string error);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to update group_map.json on {Host}")]
    private static partial void LogGroupMapUpdateException(ILogger logger, Exception ex, string host);
}
