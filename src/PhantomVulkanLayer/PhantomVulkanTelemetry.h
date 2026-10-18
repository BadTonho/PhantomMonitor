#pragma once
#include <cstdint>

#define PHANTOM_SHM_NAME "/phantom_fps"
#define PHANTOM_FALLBACK_FILE "/tmp/phantom_fps.bin"
#define PHANTOM_TELEMETRY_MAGIC 0x50484E54 // "PHNT" em Little-Endian
#define PHANTOM_TELEMETRY_VERSION 1

#pragma pack(push, 1)
struct PhantomVulkanTelemetryHeader {
    uint32_t magic;           // Deve ser PHANTOM_TELEMETRY_MAGIC (0x50484E54)
    uint32_t version;         // PHANTOM_TELEMETRY_VERSION (1)
    uint32_t pid;             // PID do processo do jogo
    uint64_t frame_count;     // Contador monotônico de quadros
    uint64_t timestamp_ns;   // Timestamp monotônico em nanosegundos
    float frametime_ms;       // Duração do quadro em milissegundos
    float instant_fps;        // Taxa instantânea calculada (1000.0f / frametime_ms)
    char process_name[64];    // Nome do executável do jogo
};
#pragma pack(pop)
