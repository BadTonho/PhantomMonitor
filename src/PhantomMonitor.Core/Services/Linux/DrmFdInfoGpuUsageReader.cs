using System.Globalization;
using System.IO;
using System.Security;

namespace PhantomMonitor.Services.Linux;

/// <summary>
/// Obtém utilização por GPU a partir dos contadores DRM por cliente em
/// /proc/&lt;pid&gt;/fdinfo. Só usa drm-engine-* quando o driver os publica.
/// </summary>
internal sealed class DrmFdInfoGpuUsageReader
{
    private readonly string _procRoot;
    private readonly string _drmDevicePrefix;
    private readonly TimeSpan _minimumSampleInterval;
    private DateTime _lastSampleUtc = DateTime.MinValue;
    private Dictionary<string, Dictionary<string, ClientCounters>> _previous = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _deviceStatuses = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyDictionary<string, double> _cachedUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

    public string Status { get; private set; } = "fdinfo DRM ainda não consultado.";

    public string GetDeviceStatus(string? pciBusId) =>
        !string.IsNullOrWhiteSpace(pciBusId) &&
        _deviceStatuses.TryGetValue(NormalizePciBusId(pciBusId), out var status)
            ? status
            : Status;

    public DrmFdInfoGpuUsageReader(
        string procRoot = "/proc",
        TimeSpan? minimumSampleInterval = null,
        string drmDeviceRoot = "/dev/dri")
    {
        _procRoot = procRoot;
        _drmDevicePrefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(drmDeviceRoot)) + Path.DirectorySeparatorChar;
        _minimumSampleInterval = minimumSampleInterval ?? TimeSpan.FromMilliseconds(500);
    }

    public IReadOnlyDictionary<string, double> Sample(DateTime timestampUtc, IReadOnlyCollection<string> knownPciBusIds)
    {
        if (_lastSampleUtc != DateTime.MinValue && timestampUtc - _lastSampleUtc < _minimumSampleInterval)
            return _cachedUsage;

        var knownDevices = knownPciBusIds
            .Select(NormalizePciBusId)
            .Where(value => value.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        _deviceStatuses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var current = new Dictionary<string, Dictionary<string, ClientCounters>>(StringComparer.OrdinalIgnoreCase);
        var seenClients = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        if (knownDevices.Count == 0)
        {
            _previous = current;
            _cachedUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _lastSampleUtc = timestampUtc;
            Status = "nenhum dispositivo PCI DRM identificado para consultar fdinfo.";
            return _cachedUsage;
        }

        if (!Directory.Exists(_procRoot))
        {
            _previous = current;
            _cachedUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            _lastSampleUtc = timestampUtc;
            Status = $"{_procRoot} não está disponível para consultar os contadores DRM.";
            SetDeviceStatuses(knownDevices, Status);
            return _cachedUsage;
        }

        var accessibleClients = 0;
        var drmDescriptors = 0;
        var readableFdInfoFiles = 0;
        var matchingGpuDescriptors = 0;
        var gpuDescriptorsWithoutEngineCounters = 0;
        var descriptorsByDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var clientsByDevice = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var processDirectory in Directory.EnumerateDirectories(_procRoot))
            {
                var processId = Path.GetFileName(processDirectory);
                if (!int.TryParse(processId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                    continue;

                var fdDirectory = Path.Combine(processDirectory, "fd");
                IEnumerable<string> fdEntries;
                try { fdEntries = Directory.EnumerateFiles(fdDirectory).ToArray(); }
                catch (Exception ex) when (IsExpectedProcFailure(ex)) { continue; }

                foreach (var fdEntry in fdEntries)
                {
                    var descriptor = Path.GetFileName(fdEntry);
                    string? target;
                    try { target = new FileInfo(fdEntry).ResolveLinkTarget(returnFinalTarget: true)?.FullName; }
                    catch (Exception ex) when (IsExpectedProcFailure(ex)) { continue; }

                    if (target is null || !target.StartsWith(_drmDevicePrefix, StringComparison.Ordinal))
                        continue;
                    drmDescriptors++;

                    var fdInfoPath = Path.Combine(processDirectory, "fdinfo", descriptor);
                    string text;
                    try { text = File.ReadAllText(fdInfoPath); }
                    catch (Exception ex) when (IsExpectedProcFailure(ex)) { continue; }
                    readableFdInfoFiles++;

                    var hasCounters = TryParseFdInfo(text, processId, descriptor, out var pciBusId, out var client);

                    var normalizedPci = NormalizePciBusId(pciBusId);
                    if (!knownDevices.Contains(normalizedPci))
                        continue;
                    matchingGpuDescriptors++;
                    Increment(descriptorsByDevice, normalizedPci);
                    if (client.EngineNanoseconds.Count == 0)
                    {
                        gpuDescriptorsWithoutEngineCounters++;
                    }
                    if (!hasCounters)
                        continue;

                    if (!seenClients.TryGetValue(normalizedPci, out var clientIds))
                        seenClients[normalizedPci] = clientIds = new HashSet<string>(StringComparer.Ordinal);
                    if (!clientIds.Add(client.Identity))
                        continue; // fd duplicados podem descrever o mesmo cliente DRM.

                    if (!current.TryGetValue(normalizedPci, out var clients))
                        current[normalizedPci] = clients = new Dictionary<string, ClientCounters>(StringComparer.Ordinal);
                    clients[client.Identity] = client;
                    accessibleClients++;
                    Increment(clientsByDevice, normalizedPci);
                }
            }
        }
        catch (Exception ex) when (IsExpectedProcFailure(ex))
        {
            // /proc muda durante a enumeração; uma corrida ou permissão negada não
            // deve interromper a coleta dos demais sensores.
        }

        var elapsedNanoseconds = _lastSampleUtc == DateTime.MinValue
            ? 0d
            : (timestampUtc - _lastSampleUtc).TotalMilliseconds * 1_000_000d;
        var nextUsage = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (elapsedNanoseconds > 0)
        {
            foreach (var (pciBusId, clients) in current)
            {
                var busyByEngine = new Dictionary<string, ulong>(StringComparer.Ordinal);
                var capacityByEngine = new Dictionary<string, uint>(StringComparer.Ordinal);

                foreach (var (clientId, counters) in clients)
                {
                    if (!_previous.TryGetValue(pciBusId, out var previousClients) ||
                        !previousClients.TryGetValue(clientId, out var previousCounters))
                        continue;

                    foreach (var (engine, value) in counters.EngineNanoseconds)
                    {
                        if (!previousCounters.EngineNanoseconds.TryGetValue(engine, out var previousValue))
                            continue;

                        var delta = value >= previousValue ? value - previousValue : 0;
                        busyByEngine[engine] = busyByEngine.GetValueOrDefault(engine, 0UL) + delta;
                    }
                }

                foreach (var counters in clients.Values)
                {
                    foreach (var (engine, capacity) in counters.EngineCapacities)
                        capacityByEngine[engine] = Math.Max(capacityByEngine.GetValueOrDefault(engine, 1u), capacity);
                }

                var maximumEngineLoad = 0d;
                foreach (var (engine, busyNanoseconds) in busyByEngine)
                {
                    var capacity = Math.Max(1u, capacityByEngine.GetValueOrDefault(engine, 1u));
                    var load = busyNanoseconds / elapsedNanoseconds / capacity * 100d;
                    if (double.IsFinite(load)) maximumEngineLoad = Math.Max(maximumEngineLoad, load);
                }

                if (busyByEngine.Count > 0)
                    nextUsage[pciBusId] = Math.Clamp(maximumEngineLoad, 0d, 100d);
            }
        }

        _previous = current;
        _cachedUsage = nextUsage;
        _lastSampleUtc = timestampUtc;
        Status = accessibleClients > 0
            ? elapsedNanoseconds <= 0
                ? $"{accessibleClients} cliente(s) DRM com contadores; amostra inicial coletada, uso calculado no próximo intervalo."
                : nextUsage.Count > 0
                    ? $"{accessibleClients} cliente(s) DRM com contadores e intervalo comparável."
                    : $"{accessibleClients} cliente(s) DRM com contadores, mas sem delta comparável nesta amostra."
            : matchingGpuDescriptors > 0
                ? $"{gpuDescriptorsWithoutEngineCounters} fdinfo da GPU sem drm-engine-*; driver pode não publicar esses contadores."
                : drmDescriptors > readableFdInfoFiles
                    ? $"{drmDescriptors} descritor(es) DRM visíveis; {readableFdInfoFiles} fdinfo legíveis e os demais inacessíveis (permissão ou processo encerrado)."
                    : drmDescriptors > 0
                        ? $"{drmDescriptors} descritor(es) DRM visíveis e {readableFdInfoFiles} fdinfo legíveis, sem correspondência com as GPUs PCI conhecidas."
                        : "nenhum descritor DRM de processo visível em /proc; outros usuários ou namespaces podem ocultar processos.";

        foreach (var pciBusId in knownDevices)
        {
            _deviceStatuses[pciBusId] = clientsByDevice.TryGetValue(pciBusId, out var clientCount)
                ? elapsedNanoseconds <= 0
                    ? $"{clientCount} cliente(s) com drm-engine-*; amostra inicial coletada, uso calculado no próximo intervalo."
                    : nextUsage.ContainsKey(pciBusId)
                        ? $"{clientCount} cliente(s) com drm-engine-* e intervalo comparável."
                        : $"{clientCount} cliente(s) com drm-engine-*, mas sem delta comparável nesta amostra."
                : descriptorsByDevice.TryGetValue(pciBusId, out var descriptorCount)
                    ? $"{descriptorCount} fdinfo associado(s) à GPU sem drm-engine-* acessível."
                    : drmDescriptors > readableFdInfoFiles
                        ? "nenhum fdinfo legível associado a esta GPU; outros processos podem estar inacessíveis."
                        : "nenhum fdinfo DRM visível associado a esta GPU nesta amostra.";
        }
        return _cachedUsage;
    }

    private static void Increment(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private void SetDeviceStatuses(IEnumerable<string> pciBusIds, string status)
    {
        foreach (var pciBusId in pciBusIds)
            _deviceStatuses[pciBusId] = status;
    }

    private static bool TryParseFdInfo(
        string text,
        string processId,
        string descriptor,
        out string pciBusId,
        out ClientCounters client)
    {
        pciBusId = "";
        string? clientId = null;
        var engines = new Dictionary<string, ulong>(StringComparer.Ordinal);
        var capacities = new Dictionary<string, uint>(StringComparer.Ordinal);

        foreach (var line in text.Split('\n'))
        {
            var colon = line.IndexOf(':');
            if (colon <= 0) continue;

            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (key.Equals("drm-pdev", StringComparison.Ordinal))
            {
                pciBusId = value;
                continue;
            }

            if (key.Equals("drm-client-id", StringComparison.Ordinal) && value.Length > 0)
            {
                clientId = value;
                continue;
            }

            const string capacityPrefix = "drm-engine-capacity-";
            if (key.StartsWith(capacityPrefix, StringComparison.Ordinal))
            {
                var engine = key[capacityPrefix.Length..];
                if (engine.Length > 0 && TryParseUnsigned(value, out var capacity) && capacity > 0)
                    capacities[engine] = (uint)Math.Min(capacity, uint.MaxValue);
                continue;
            }

            const string enginePrefix = "drm-engine-";
            if (key.StartsWith(enginePrefix, StringComparison.Ordinal))
            {
                var engine = key[enginePrefix.Length..];
                if (engine.Length > 0 && TryParseUnsigned(value, out var nanoseconds))
                    engines[engine] = nanoseconds;
            }
        }

        if (pciBusId.Length == 0 || engines.Count == 0)
        {
            client = new ClientCounters(
                $"fd:{processId}:{descriptor}",
                new Dictionary<string, ulong>(StringComparer.Ordinal),
                new Dictionary<string, uint>(StringComparer.Ordinal));
            return false;
        }

        var identity = clientId is null ? $"fd:{processId}:{descriptor}" : $"client:{clientId}";
        client = new ClientCounters(identity, engines, capacities);
        return true;
    }

    private static bool TryParseUnsigned(string value, out ulong parsed)
    {
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        parsed = 0;
        return tokens.Length > 0 && ulong.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
    }

    private static string NormalizePciBusId(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        var components = trimmed.Split(':');
        if (components.Length != 3) return trimmed;
        var domain = uint.TryParse(components[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsedDomain)
            ? parsedDomain.ToString("x4", CultureInfo.InvariantCulture)
            : components[0].PadLeft(4, '0');
        return $"{domain}:{components[1].PadLeft(2, '0')}:{components[2]}";
    }

    private static bool IsExpectedProcFailure(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SecurityException;

    private sealed record ClientCounters(
        string Identity,
        IReadOnlyDictionary<string, ulong> EngineNanoseconds,
        IReadOnlyDictionary<string, uint> EngineCapacities);
}
