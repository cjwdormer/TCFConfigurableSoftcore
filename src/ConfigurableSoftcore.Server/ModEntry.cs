using SPTarkov.Server.Core.Models.Utils;
using ConfigurableSoftcore.Server.Config;

namespace ConfigurableSoftcore.Server;

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader)]
public class ModEntry : IOnLoad
{
    private readonly ISptLogger<ModEntry> _logger;
    private readonly SoftcoreConfigService _configService;

    public ModEntry(ISptLogger<ModEntry> logger, SoftcoreConfigService configService)
    {
        _logger = logger;
        _configService = configService;
    }

    public Task OnLoad()
    {
        var cfg = _configService.LoadOrCreate();
        _logger.Success(
            $"[ConfigurableSoftcore] Loaded. Enabled={cfg.Enabled} DefaultMode={cfg.DefaultMode} " +
            $"ProfileOverrides={cfg.ProfileOverrides.Count}");
        return Task.CompletedTask;
    }
}
