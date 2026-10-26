#pragma once
#include <cstdint>
#include <cstddef>

// Objeto por processo: o nome completo é "<prefixo><pid>" para isolar dois jogos
// com a layer ativa (antes, o objeto único era last-writer-wins entre processos) e
// "fantasma" pós-encerramento é evitado com shm_unlink no cleanup — o leitor C#
// enumera os objetos e valida se o PID autoral ainda existe (em /proc/<pid>).
#define PHANTOM_SHM_PREFIX "/phantom_fps_"          // + PID → /dev/shm/phantom_fps_<pid>
#define PHANTOM_FALLBACK_PREFIX "/tmp/phantom_fps_" // + PID.bin → /tmp/phantom_fps_<pid>.bin
#define PHANTOM_TELEMETRY_MAGIC 0x50484E54 // "PHNT" em Little-Endian
#define PHANTOM_TELEMETRY_VERSION 2

#pragma pack(push, 1)
struct PhantomVulkanTelemetryHeader {
    uint32_t magic;           // Deve ser PHANTOM_TELEMETRY_MAGIC (0x50484E54)
    uint32_t version;         // PHANTOM_TELEMETRY_VERSION (1)
    uint32_t pid;             // PID do processo do jogo
    uint64_t frame_count;     // Contador monotônico de quadros
    uint64_t timestamp_ns;    // Timestamp monotônico em nanosegundos
    float frametime_ms;       // Duração do quadro em milissegundos
    float instant_fps;        // Taxa instantânea calculada (1000.0f / frametime_ms)
    char process_name[64];    // Nome do executável do jogo (UTF-8 de /proc/self/comm)
};
#pragma pack(pop)

// Leitura interprocesso eventual (campos podem combinar quadros adjacentes; o
// leitor filtra valores plausíveis). Um seqlock traria consistência total pelo
// custo de 4 bytes e revalidação de toda a ABI.
static_assert(sizeof(PhantomVulkanTelemetryHeader) == 100,
              "ABI da telemetria deve ser de 100 bytes (leitor C# com Pack=1)");

// v2 keeps the legacy prefix and adds a sequence-protected per-frame ring.
constexpr uint32_t PHANTOM_FRAME_CAPACITY = 4096;
struct PhantomVulkanFrame {
    uint64_t frame;
    uint64_t timestamp_ns;
    float frametime_ms;
    float fps;
};
struct PhantomVulkanTelemetry {
    PhantomVulkanTelemetryHeader header;
    uint32_t capacity;
    uint64_t sequence;
    uint64_t session;
    PhantomVulkanFrame frames[PHANTOM_FRAME_CAPACITY];
};
static_assert(offsetof(PhantomVulkanTelemetry, sequence) == 104);
static_assert(offsetof(PhantomVulkanTelemetry, frames) == 120);
