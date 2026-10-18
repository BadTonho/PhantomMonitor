# SDK de Telemetria (memória compartilhada)

Terceiros (OBS, AutoHotkey, dashboards, scripts, outros apps) consomem a telemetria
do Phantom Monitor **sem parse de JSON/CSV**: o monitor publica um snapshot
estruturado numa memória compartilhada a cada ciclo de monitoramento (mesma
cadência do snapshot, mínimo 100 ms).

> O mapa é criado sempre que o monitoramento inicia e destruído quando para.
> `logs/sensors_latest.json` continua como fallback legado para integrações simples.

## Identificação

- **Nome do mapa:** `phantom_monitor_telemetry_v1` (namespace de sessão local;
  consumidores devem rodar no mesmo usuário/sessão do monitor — `Global\` exigiria
  elevação e recebeu o voto contra por segurança)
- **Tamanho:** 20.544 bytes (64 de cabeçalho + 256 entradas × 80 bytes)
- **Direitos:** `Read` é suficiente para leitores

## Layout

```
0x00  int32  magic        "PMTS" = 0x53544D50 (little-endian)
0x04  int32  version      1
0x08  int32  seq          contador de publicação (seqlock; veja protocolo)
0x0C  int32  seriesCount  nº de entradas válidas
0x10  int64  timestamp    UnixTimeMilliseconds do snapshot
0x18  40B    reservado    (zero)

--- Entradas (i = 0 .. seriesCount-1), cada uma em 64 + i*80: ---
+000  char[40]  key    chave da série, ASCII com preenchimento NUL ("GpuCore", "GpuCore#1")
+040  double    value  valor (NaN = série removida neste ciclo)
+048  char[32]  label  rótulo de origem, ASCII (`*` substitui não-ASCII)
```

A ordem das entradas segue `GetSeriesKeys()`: séries canônicas primeiro (ordem
declaração do enum `SensorType`), depois as instâncias ordenadas por chave —
o CSV usa a mesma ordem nas colunas.

## Chaves (`key`)

Mesmas chaves de `SensorData`: `Categoria` para a série canônica e
`Categoria#índice` para séries por instância (ex.: `GpuCore#1` = GPU dedicada,
`CpuTemperature#3` = núcleo 3, `FanSpeed#2`, `DiskRead#1`). O catálogo com as
fontes está em `docs/funções/hardware-monitor.md` e aplicação no
Inventário de Hardware.

## Protocolo de consistência (seqlock)

O escritor C# não usa locks de leitor:

1. escreve `seq` ímpar (`seq+1`) → leitores sabem que escrita começou/está em voo;
2. reescreve header + entradas válidas, marca as remanescentes do ciclo anterior com `NaN`;
3. escreve `seq` par (`+1`) → publicação íntegra.

Leitor válido:

```csharp
int seq1 = acc.ReadInt32(0x08);
if ((seq1 & 1) != 0) retry;                 // escrita em andamento
var magic = acc.ReadInt32(0x00);
var count = acc.ReadInt32(0x0C);
var ts    = acc.ReadInt64(0x10);
// ... ler entradas ...
if (acc.ReadInt32(0x08) != seq1) retry;     // snap mudou no meio → descarta e repete
```

C (mesmo protocolo): `uint32 seq = *(uint32*)(base+8); if (seq & 1) retry;
... memcpy das entradas ... if (*(uint32*)(base+8) != seq) retry;` — sem barrier
explicito necessário em x86/x64 para este uso; em ARM64, leia `seq` com
`__atomic_load_n(..., __ATOMIC_ACQUIRE)`.

## Limites e versionamento

- Capacidade por ciclo: **256 séries**; chaves até 40 caracteres; rótulos até 32
  (excesso truncado; não-ASCII vira `*`).- A mudança de layout quebra compatibilidade → **mapa novo** (`..._v2`) com
  `version` incrementada; o leitor checa `version` antes de parsear.
- Cadência = cadência do monitoramento (`updateIntervalMs`, mínimo 100 ms).
- Um consumidor nunca deve confiar em `label` para identificar: identifique por
  `key` (contrato estável), `label` é humano/observacional.

## Leitor de referência

`tools/TelemetrySdkReader/` (console .NET 10, fora da solução):

```bash
dotnet run --project tools/TelemetrySdkReader           # 500 ms
dotnet run --project tools/TelemetrySdkReader -- 250    # intervalo custom
```
