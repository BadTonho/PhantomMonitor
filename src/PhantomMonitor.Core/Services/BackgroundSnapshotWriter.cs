using System.Threading.Channels;
using PhantomMonitor.Models;

namespace PhantomMonitor.Services;

/// <summary>
/// Bomba de escrita para os consumidores pesados por snapshot (CSV, JSON, journal
/// JSONL, SDK de telemetria e exportador HTTP). Cada tick serializava/grava arquivo
/// na UI thread (até 10 Hz); com a bomba, o produtor apenas enfileira sem bloquear
/// e uma thread dedicada executa os writers em FIFO — a ordem dos snapshots é
/// preservada porque o canal tem leitor único.
/// </summary>
/// <remarks>
/// Ciclo de vida: chamadores devem dispôr a bomba <em>antes</em> de dispor os
/// writers registrados nela, garantindo a drenagem das amostras enfileiradas. Os
/// writers usados (CsvLogger, JsonLogger, JsonJournalLogger) possuem lock interno
/// e tratam as próprias exceções; a bomba também absorve exceções por writer para
/// não abortar a fila. O snapshot não é mutado após a publicação pelo serviço de
/// sensores (contrato de snapshot imutável), então é seguro lê-lo em segunda thread.
/// </remarks>
public sealed class BackgroundSnapshotWriter : IDisposable
{
    private readonly Channel<SensorData> _channel =
        Channel.CreateUnbounded<SensorData>(new UnboundedChannelOptions { SingleReader = true });
    private readonly Thread _thread;
    private readonly List<Action<SensorData>> _writers = [];

    public BackgroundSnapshotWriter()
    {
        _thread = new Thread(Run)
        {
            Name = "PhantomMonitor.SnapshotWriter",
            IsBackground = true, // nunca segura o processo na saída
        };
        _thread.Start();
    }

    /// <summary>Registra um consumidor na fila compartilhada. Chame antes do uso.</summary>
    public void AddWriter(Action<SensorData> writer) => _writers.Add(writer);

    public void Write(SensorData data) => _channel.Writer.TryWrite(data);

    private void Run()
    {
        while (true)
        {
            try
            {
                // ReadAllAsync usaria IAsyncEnumerable; aqui a espera é convertida
                // para sincronia porque a thread dedicada não precisa de pool.
                if (!_channel.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                    return; // fila completa: fim do ciclo

                while (_channel.Reader.TryRead(out var data))
                {
                    foreach (var writer in _writers)
                    {
                        try
                        {
                            writer(data);
                        }
                        catch (Exception ex)
                        {
                            ErrorLog.Write("SnapshotWriter", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("SnapshotWriter.Loop", ex);
            }
        }
    }

    public void Dispose()
    {
        // Completa o canal e espera drenar tudo que já foi enfileirado. Teto de 3 s
        // para não prender o fechamento da janela se o disco travar; o que sobrar
        // é descartado (falhas contínuas de escrita já são registradas pelos writers).
        _channel.Writer.TryComplete();
        if (!_thread.Join(TimeSpan.FromSeconds(3)))
            ErrorLog.Write("SnapshotWriter", "Drenagem não concluiu no teto de 3 s ao encerrar.");
    }
}
