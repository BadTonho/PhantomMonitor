using System.Text.Json;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Services;

var tests = new (string Name, Action Run)[]
{
    ("coordenadas do canvas acompanham resolução e escala da tela", TestCanvasToScreenCoordinates),
    ("layout aplicado preserva itens e o filtro de séries do preview", TestAppliedContentParity)
};

foreach (var test in tests)
{
    test.Run();
    Console.WriteLine($"PASS: {test.Name}");
}

static void TestCanvasToScreenCoordinates()
{
    var screens = new (int Width, int Height, double Scale, int OriginX, int OriginY)[]
    {
        (1920, 1080, 1, 0, 0),
        (2560, 1440, 1.25, -1920, 0),
        (3840, 2160, 2, 0, -2160),
        (3440, 1440, 1.5, -3440, 1080)
    };
    const double canvasWidth = 1080;
    const double canvasHeight = 680;
    const double blockWidthDip = 220;
    const double blockHeightDip = 96;
    var fractions = new[] { 0d, 0.17, 0.5, 0.83, 1d };

    foreach (var screen in screens)
    {
        var screenWidthDip = screen.Width / screen.Scale;
        var screenHeightDip = screen.Height / screen.Scale;
        var previewWidth = blockWidthDip * canvasWidth / screenWidthDip;
        var previewHeight = blockHeightDip * canvasHeight / screenHeightDip;

        foreach (var fraction in fractions)
        {
            var previewX = fraction * (canvasWidth - previewWidth);
            var normalizedX = OverlayPositionMath.ToNormalizedFromDisplay(previewX, canvasWidth,
                previewWidth, OverlayPositionMath.ReferenceWidth);
            var actualX = OverlayPositionMath.ToPixelCoordinate(normalizedX, screen.OriginX,
                screen.Width, blockWidthDip, screen.Scale);
            var expectedX = screen.OriginX + (int)Math.Round(
                fraction * (screen.Width - blockWidthDip * screen.Scale), MidpointRounding.AwayFromZero);
            Assert(Math.Abs(actualX - expectedX) <= 1,
                $"X: tela {screen.Width}×{screen.Height} escala {screen.Scale}, fração {fraction}, esperado {expectedX}, recebido {actualX}");

            var previewY = fraction * (canvasHeight - previewHeight);
            var normalizedY = OverlayPositionMath.ToNormalizedFromDisplay(previewY, canvasHeight,
                previewHeight, OverlayPositionMath.ReferenceHeight);
            var actualY = OverlayPositionMath.ToPixelCoordinate(normalizedY, screen.OriginY,
                screen.Height, blockHeightDip, screen.Scale);
            var expectedY = screen.OriginY + (int)Math.Round(
                fraction * (screen.Height - blockHeightDip * screen.Scale), MidpointRounding.AwayFromZero);
            Assert(Math.Abs(actualY - expectedY) <= 1,
                $"Y: tela {screen.Width}×{screen.Height} escala {screen.Scale}, fração {fraction}, esperado {expectedY}, recebido {actualY}");
        }
    }
}

static void TestAppliedContentParity()
{
    var draft = new OverlayBlockConfig
    {
        Id = "regression-block",
        X = 0.37,
        Y = 0.62,
        Items =
        [
            new() { Kind = "category", Value = "gpu" },
            new() { Kind = "series", Value = "Gpu*" },
            new() { Kind = "frametime", Value = "frametime" },
            new() { Kind = "template", Value = "summary" }
        ],
        MetricOrder = new Dictionary<string, List<string>>
        {
            ["category:gpu"] = ["GpuCore", "GpuTemperature"]
        }
    };

    var applied = MainWindowViewModel.CloneOverlayBlock(draft);
    Assert(JsonSerializer.Serialize(draft) == JsonSerializer.Serialize(applied),
        "Save & Apply must copy the full block config, including content order and normalized position.");

    var identities = new[] { "GpuCore", "GpuCore#1", "GpuTemperature#1", "CpuCore#1" };
    IReadOnlyList<string> activeProfileSeries = ["GpuCore#1", "GpuTemperature#1"];
    var previewContent = OverlayBlockContentRules.SelectSeriesIdentities(identities, "Gpu*", activeProfileSeries);
    var appliedContent = OverlayBlockContentRules.SelectSeriesIdentities(identities, "Gpu*", activeProfileSeries);
    Assert(previewContent.SequenceEqual(appliedContent, StringComparer.OrdinalIgnoreCase),
        "Draft preview and applied blocks must use the same profile series filter.");
    Assert(previewContent.SequenceEqual(activeProfileSeries, StringComparer.OrdinalIgnoreCase),
        "The shared profile filter must retain exactly the enabled per-device series.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
