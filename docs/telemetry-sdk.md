# SDK de Telemetria (memória compartilhada aposentada + HTTP)

> **Idioma:** Português (Brasil) · [English](en/telemetry-sdk.md)

Terceiros (OBS, AutoHotkey, dashboards, scripts, outros apps) consumiam a
telemetria do Phantom Monitor de duas formas, **sem tocar em arquivos de log**:

> **Status (Fase 2 da unificação visual):** o mapa de memória compartilhada
> (`TelemetrySdkWriter`) era exclusivo do frontend WPF e foi aposentado junto
> com ele (`6c4d2c0`) — o app único não o publica. As interfaces externas
> atuais são o **exportador HTTP** (`/values`, `/health`) e o
> `sensors_latest.json` (no Linux, também o canal POSIX da camada Vulkan para
> timings de frame). O restante do documento fica como **referência histórica**
> do formato e do leitor em `tools/TelemetrySdkReader/`.

1. **Memória compartilhada** (histórico do WPF, aposentado na Fase 2): publicava
   um snapshot estruturado a cada ciclo de monitoramento (mínimo 100 ms) —
   parse zero no consumidor. O app único não publica esse mapa; no Linux nunca
   foi publicado (só o canal POSIX da camada Vulkan para timings de frame).
2. **Exportador HTTP** (opt-in, `telemetryExport.enabled`): os dois sistemas
   podem servir o último snapshot como JSON em `GET /values` para dashboards de
   navegador e ferramentas externas.

> Quando existia (WPF), o mapa era criado quando o monitoramento iniciava e
> destruído quando parava. No app único, o exportador HTTP é a interface de
> telemetria externa; `sensors_latest.json` continua como fallback para
> integrações simples.

## Memória compartilhada (WPF aposentado — referência histórica)

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

- Cadência = cadência do monitoramento (`updateIntervalMs`, mínimo 100 ms).
- Um consumidor nunca deve confiar em `label` para identificar: identifique por
  `key` (contrato estável), `label` é humano/observacional.

## Exportador HTTP (`telemetryExport`, P6.3)

Complemento de rede para o mapa de memória — mesma cadência, payload maior:

```json
"telemetryExport": { "enabled": false, "port": 7317, "listenAll": false }
```

- **Endpoints:** `GET /values` (snapshot JSON, **mesmo payload do
  `sensors_latest.json`**: `timestamp`, `values`, `labels`, `series`,
  `seriesLabels`, `seriesStats`, `monitors`) e `GET /health`
  (`{"ok":true,"timestamp":"..."}` para health checks). Outros caminhos → 404 em texto.
- **Escuta padrão:** `http://127.0.0.1:7317/` — loopback não requer permissão de
  URL do http.sys nem firewall. `listenAll: true` vira `http://+:porta/`
  (todas as interfaces, para consumo em 2ª máquina) e pode exigir uma regra de
  firewall no Linux ou uma URL ACL/firewall no Windows; sem TLS e sem
  autenticação, então só use em rede confiável. `port` é travada em 1024–65535
  pelo `Normalize`.
- **Headers:** `Cache-Control: no-store` (painéis de browser buscam a cada 500 ms
  sem stale), `Content-Type: application/json`, `Content-Length` para keep-alive.
- **Ciclo de vida:** ligado somente com o monitoramento ativo (criado no início,
  descartado no stop); falha ao abrir a porta é registrada em
  `errors.log` e **não** impede o monitoramento. No Linux, o arquivo fica
  em `$XDG_DATA_HOME/PhantomMonitor/logs/` (normalmente `~/.local/share/PhantomMonitor/logs/`);
  no Windows, fica em `%LOCALAPPDATA%\PhantomMonitor\logs\` (o WPF aposentado gravava
  `logs/` ao lado do executável). Consulte [diagnóstico](funções/diagnostico.md)
  para ver os registros no aplicativo.
- **Ativo quando pausado (P6.7):** segue servindo o último snapshot válido
  durante a pausa — dashboards não "somem", congelando no valor de pausa.
- **Linux:** os campos `enabled`, `port` e `listenAll` podem ser editados
  na página **Configurações**. O dashboard mostra endpoint e estado do listener;
  falhas de bind aparecem ali e no log sem interromper a coleta. Se a porta ou
  o endereço forem alterados durante a pausa, o listener é reaberto e recebe o
  snapshot congelado antes de voltar a servir.
- **Anti-cheat por design:** ouvindo no loopback, nenhum processo de jogo é
  afetado; o exporter só responde, nunca consulta o sistema nem outros apps.
- **Por que HTTP e não UDP:** `HttpListener` dá descoberta (`/health`),
  Content-Length/keep-alive, headers e clientes universais (fetch de browser,
  curl, Invoke-RestMethod) sem escrever nenhum parser do lado do consumidor;
  UDP teria menos overhead, mas toda integração precisaria reimplementar
  framing/parse (~o que o SDK de memória resolve melhor em tempo real).

### Exemplo de consumo

```bash
curl http://127.0.0.1:7317/values
Invoke-RestMethod http://127.0.0.1:7317/values | % { "$($_.series['GpuCore'])" }
```

## Limites e versionamento

- Capacidade por ciclo: **256 séries**; chaves até 40 caracteres; rótulos até 32
  (excesso truncado; não-ASCII vira `*`).
- A mudança de layout quebra compatibilidade → **mapa novo** (`..._v2`) com
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

## Limites do exportador HTTP atual
O listener aceita no máximo 16 handlers simultâneos. O excesso recebe HTTP 503;
escritas assíncronas têm prazo de cinco segundos e respostas lentas são abortadas.
A publicação do snapshot não bloqueia a coleta. O acesso LAN continua opt-in e sem
autenticação/TLS, conforme o contrato existente.
