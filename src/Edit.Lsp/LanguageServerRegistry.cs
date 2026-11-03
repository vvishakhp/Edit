using System.Text.Json;
using System.Text.Json.Serialization;
using Edit.Core;
using Edit.Platform;
using Microsoft.Extensions.Logging;

namespace Edit.Lsp;

public interface ILanguageServerRegistry
{
    IReadOnlyList<LanguageServerSettings> Servers { get; }
    void Reload();
}

public sealed class LanguageServerRegistry : ILanguageServerRegistry
{
    private readonly ILogger<LanguageServerRegistry> _logger;
    private readonly List<LanguageServerSettings> _servers = new();

    public LanguageServerRegistry(ILogger<LanguageServerRegistry> logger)
    {
        _logger = logger;
        Reload();
    }

    public IReadOnlyList<LanguageServerSettings> Servers => _servers;

    public void Reload()
    {
        _servers.Clear();
        var path = AppPaths.LanguageServersPath;
        try
        {
            if (!File.Exists(path))
            {
                _logger.LogWarning("Language servers config not found at {Path}", path);
                return;
            }

            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<LanguageServersFile>(json, JsonOptions);
            if (file?.Servers is null) return;

            foreach (var s in file.Servers)
            {
                if (string.IsNullOrWhiteSpace(s.Id)) continue;
                if (string.IsNullOrWhiteSpace(s.DisplayName))
                    s.DisplayName = s.Id;
                if (!string.Equals(s.Protocol, "lsp", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Skipping server {Id}: unsupported protocol {Protocol}", s.Id, s.Protocol);
                    continue;
                }
                _servers.Add(s);
            }

            _logger.LogInformation("Loaded {Count} language server(s) from {Path}", _servers.Count, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load language servers from {Path}", path);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}
