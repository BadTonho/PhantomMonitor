using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

public sealed partial class AppConfig
{
    private static List<OverlayBlockConfig> MigrateLegacyOverlayBlocks(OverlayConfig overlay)
    {
        var categories = new (string Key, bool Enabled)[]
        {
            ("cpu", overlay.ShowCpu), ("gpu", overlay.ShowGpu), ("ram", overlay.ShowRam),
            ("disk", overlay.ShowDisk), ("network", overlay.ShowNetwork), ("fans", overlay.ShowFans),
            ("motherboard", overlay.ShowMotherboard), ("fps", overlay.ShowFps)
        };
        var blocks = new Dictionary<string, OverlayBlockConfig>(StringComparer.OrdinalIgnoreCase);

        OverlayBlockConfig Ensure(string key)
        {
            if (blocks.TryGetValue(key, out var existing)) return existing;
            var index = Array.FindIndex(categories, item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (index < 0) index = categories.Length;
            overlay.Layout.TryGetValue(key, out var oldPosition);
            var block = new OverlayBlockConfig
            {
                Id = key,
                X = oldPosition?.X ?? 0.02,
                Y = oldPosition?.Y ?? 0.035 + index * 0.105
            };
            blocks[key] = block;
            return block;
        }

        foreach (var (key, enabled) in categories)
        {
            if (!enabled) continue;
            var block = Ensure(key);
            block.Items.Add(new OverlayBlockItemConfig { Kind = "category", Value = key });
            if (key == "fps" && overlay.ShowFrametimeGraph)
                block.Items.Add(new OverlayBlockItemConfig { Kind = "frametime", Value = "frametime" });
        }

        foreach (var pattern in overlay.ShownSeries ?? [])
        {
            var key = pattern.StartsWith("Gpu", StringComparison.OrdinalIgnoreCase) ? "gpu"
                : pattern.StartsWith("Fan", StringComparison.OrdinalIgnoreCase) ? "fans"
                : pattern.StartsWith("Disk", StringComparison.OrdinalIgnoreCase) ? "disk"
                : pattern.StartsWith("Cpu", StringComparison.OrdinalIgnoreCase) ? "cpu"
                : pattern.StartsWith("Ram", StringComparison.OrdinalIgnoreCase) ? "ram"
                : pattern.StartsWith("Network", StringComparison.OrdinalIgnoreCase) ? "network"
                : "extras";
            Ensure(key).Items.Add(new OverlayBlockItemConfig { Kind = "series", Value = pattern });
        }

        if ((overlay.Graphs?.Count ?? 0) > 0 || (overlay.Templates?.Count ?? 0) > 0)
        {
            var extras = Ensure("extras");
            foreach (var graph in overlay.Graphs ?? [])
                extras.Items.Add(new OverlayBlockItemConfig { Kind = "graph", Value = graph.Id });
            foreach (var template in overlay.Templates ?? [])
                extras.Items.Add(new OverlayBlockItemConfig { Kind = "template", Value = template.Id });
        }

        return blocks.Values.ToList();
    }
}
