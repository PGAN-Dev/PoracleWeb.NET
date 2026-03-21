using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PGAN.Poracle.Web.Core.Abstractions.Services;
using PGAN.Poracle.Web.Core.Models;

namespace PGAN.Poracle.Web.Core.Services;

public class PoracleServerService(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<PoracleServerService> logger) : IPoracleServerService
{
    private readonly HttpClient _httpClient = httpClient;
    private readonly ILogger<PoracleServerService> _logger = logger;
    private readonly string _sshKeyPath = configuration["Poracle:SshKeyPath"] ?? "/app/ssh_key";

    private record ServerConfig(string Name, string Host, string ApiAddress, string SshUser);

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

            if (!string.IsNullOrWhiteSpace(host))
            {
                servers.Add(new ServerConfig(name, host, apiAddress, sshUser));
            }
        }

        return servers;
    }

    public async Task<List<PoracleServerStatus>> GetServersAsync()
    {
        var servers = this.GetServers();
        var statuses = new List<PoracleServerStatus>();

        foreach (var server in servers)
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
                status.Online = response.IsSuccessStatusCode;
            }
            catch
            {
                status.Online = false;
            }

            status.CheckedAt = DateTime.UtcNow;
            statuses.Add(status);
        }

        return statuses;
    }

    public async Task<PoracleServerStatus> RestartServerAsync(string host)
    {
        var servers = this.GetServers();
        var server = servers.FirstOrDefault(s =>
            s.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Server with host '{host}' not found in configuration.");

        var status = new PoracleServerStatus
        {
            Name = server.Name,
            Host = server.Host,
        };

        try
        {
            var sshArgs = $"-o StrictHostKeyChecking=no -o ConnectTimeout=10 -i {this._sshKeyPath} {server.SshUser}@{server.Host} \"pm2 restart all\"";

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

            process.Start();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            var exited = await process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token)
                .ContinueWith(t => !t.IsCanceled);

            if (!exited)
            {
                try { process.Kill(); } catch { /* best effort */ }
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
}
