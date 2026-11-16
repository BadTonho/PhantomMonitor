using System.Collections.ObjectModel;
using PhantomMonitor.App.ViewModels;
using PhantomMonitor.Models;
using PhantomMonitor.Services;

namespace PhantomMonitor.OverlayRegressionTests;

public sealed class InventoryAndUiTests
{
    private static InventoryRowViewModel CreateRow(string key, string source, bool available, string value = "42", string unit = "°C")
    {
        var row = new InventoryRowViewModel(key);
        row.Apply(source, value, unit, available, "40", "45", "42");
        return row;
    }

    [Fact]
    public void InventoryFilter_EmptyFilter_ShowsAllGroupsAndRows()
    {
        var cpuTemp = CreateRow("CpuTemperature", "CPU Package", true);
        var gpuHotspot = CreateRow("GpuHotspot", "—", false, "—", "");
        var cpuGroup = new InventoryGroupViewModel("CPU", new ObservableCollection<InventoryRowViewModel>([cpuTemp]));
        var gpuGroup = new InventoryGroupViewModel("GPU", new ObservableCollection<InventoryRowViewModel>([gpuHotspot]));

        var visibleGroups = InventoryFilter.Apply([cpuGroup, gpuGroup], "", onlyAvailable: false);

        Assert.Equal(2, visibleGroups);
        Assert.True(cpuGroup.IsVisible);
        Assert.True(gpuGroup.IsVisible);
        Assert.True(cpuTemp.IsVisible);
        Assert.True(gpuHotspot.IsVisible);
    }

    [Fact]
    public void InventoryFilter_OnlyAvailable_HidesUnavailableRowsAndEmptyGroups()
    {
        var cpuTemp = CreateRow("CpuTemperature", "CPU Package", true);
        var gpuHotspot = CreateRow("GpuHotspot", "—", false, "—", "");
        var cpuGroup = new InventoryGroupViewModel("CPU", new ObservableCollection<InventoryRowViewModel>([cpuTemp]));
        var gpuGroup = new InventoryGroupViewModel("GPU", new ObservableCollection<InventoryRowViewModel>([gpuHotspot]));

        var visibleGroups = InventoryFilter.Apply([cpuGroup, gpuGroup], null, onlyAvailable: true);

        Assert.Equal(1, visibleGroups);
        Assert.True(cpuGroup.IsVisible);
        Assert.True(cpuTemp.IsVisible);
        Assert.False(gpuGroup.IsVisible);
        Assert.False(gpuHotspot.IsVisible);
    }

    [Fact]
    public void InventoryFilter_SearchQuery_MatchesRowOrChildOrGroupTitle()
    {
        var core0 = CreateRow("CpuCoreUsage#0", "Core #0", true, "15", "%");
        var core1 = CreateRow("CpuCoreUsage#1", "Core #1", true, "20", "%");
        var coresParent = CreateRow("CpuCoreUsage", "Núcleos", true);
        coresParent.Children.Add(core0);
        coresParent.Children.Add(core1);

        var ramRow = CreateRow("MemoryUsed", "RAM Usada", true, "8.2", "GB");
        var cpuGroup = new InventoryGroupViewModel("Processador", new ObservableCollection<InventoryRowViewModel>([coresParent]));
        var ramGroup = new InventoryGroupViewModel("Memória", new ObservableCollection<InventoryRowViewModel>([ramRow]));

        var visibleGroups = InventoryFilter.Apply([cpuGroup, ramGroup], "core #1", onlyAvailable: false);

        Assert.Equal(1, visibleGroups);
        Assert.True(cpuGroup.IsVisible);
        Assert.True(coresParent.IsVisible);
        Assert.False(core0.IsVisible);
        Assert.True(core1.IsVisible);
        Assert.False(ramGroup.IsVisible);
        Assert.False(ramRow.IsVisible);
    }

    [Fact]
    public void InventoryFilter_NoMatch_ReturnsZeroGroups()
    {
        var row = CreateRow("CpuTemperature", "CPU Package", true);
        var group = new InventoryGroupViewModel("CPU", new ObservableCollection<InventoryRowViewModel>([row]));

        var visibleGroups = InventoryFilter.Apply([group], "inexistente-xyz", onlyAvailable: false);

        Assert.Equal(0, visibleGroups);
        Assert.False(group.IsVisible);
        Assert.False(row.IsVisible);
    }

    [Fact]
    public void InventoryRowViewModel_FormatsCurrentAndSensorNameCorrectly()
    {
        var available = CreateRow("CpuTemperature", "Package", true, "63.5", "°C");
        Assert.Equal("Package", available.SensorName);
        Assert.Equal("63.5 °C", available.Current);

        var unavailable = CreateRow("GpuFanSpeed", "—", false, "—", "RPM");
        Assert.Equal("GpuFanSpeed", unavailable.SensorName);
        Assert.Equal("—", unavailable.Current);
    }

    [Fact]
    public void SensorTuningEngine_AppliesSmoothingAndPreservesDisabledRules()
    {
        var now = DateTime.UtcNow;
        var engine = new SensorTuningEngine(
            [new SensorTuningConfig { Series = "Gpu*", IntervalMs = 0, Smoothing = 2 }],
            () => now);

        var first = new SensorData();
        first.SetValue(SensorType.GpuTemperature, 50);
        engine.Apply(first);
        Assert.Equal(50, first.GetValue(SensorType.GpuTemperature));

        now = now.AddSeconds(1);
        var second = new SensorData();
        second.SetValue(SensorType.GpuTemperature, 60);
        engine.Apply(second);
        Assert.Equal(55, second.GetValue(SensorType.GpuTemperature));
    }
}
