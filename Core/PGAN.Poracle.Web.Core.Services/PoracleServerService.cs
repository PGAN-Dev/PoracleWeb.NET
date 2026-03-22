using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

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
        ValidateHostname(server.Host, "host");
        ValidateHostname(server.SshUser, "sshUser");

        var status = new PoracleServerStatus
        {
            Name = server.Name,
            Host = server.Host,
        };

        try
        {
            var sshArgs = $"-o StrictHostKeyChecking=no -o ConnectTimeout=10 -o SendEnv=none -i {this._sshKeyPath} {server.SshUser}@{server.Host} \"{server.RestartCommand}\"";

            this._logger.LogInformation("Executing SSH restart command for server {Host}", server.Host);

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
                this._logger.LogWarning("SSH restart timed out for server {Host}", server.Host);
            }
            else
            {
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                var output = $"{stdout}\n{stderr}".Trim();

                status.Online = process.ExitCode == 0;
                status.Message = output;

                this._logger.LogInformation(
                    "SSH restart for server {Host} completed with exit code {ExitCode}: {Output}",
                    server.Host, process.ExitCode, output);
            }
        }
        catch (Exception ex)
        {
            status.Online = false;
            status.Message = $"Failed to execute SSH command: {ex.Message}";
            this._logger.LogError(ex, "Failed to restart server {Host} via SSH", server.Host);
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

                // Use python3/node to safely update the JSON file on the remote server
                var escapedName = geofenceName.Replace("\"", "\\\"");
                var escapedGroup = group.Replace("\"", "\\\"");
                var updateScript = $"python3 -c \\\"import json; f='{server.GroupMapPath}'; d=json.load(open(f)); d['{escapedName}']='{escapedGroup}'; json.dump(d,open(f,'w'),indent=2)\\\"";

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
                    this._logger.LogInformation("Updated group_map.json on {Host}: {Name} -> {Group}", server.Host, geofenceName, group);
                }
                else
                {
                    var stderr = await process.StandardError.ReadToEndAsync();
                    this._logger.LogWarning("Failed to update group_map.json on {Host}: {Error}", server.Host, stderr);
                }
            }
            catch (Exception ex)
            {
                this._logger.LogWarning(ex, "Failed to update group_map.json on {Host}", server.Host);
            }
        }
    }
}
