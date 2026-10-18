#pragma once
#include <cstdint>

#define RIVA_SHM_NAME "/riva_fps"
#define RIVA_FALLBACK_FILE "/tmp/riva_fps.bin"
#define RIVA_TELEMETRY_MAGIC 0x52495641 // "RIVA" em Little-Endian
#define RIVA_TELEMETRY_VERSION 1

#pragma pack(push, 1)
struct RivaVulkanTelemetryHeader {
    uint32_t magic;           // Deve ser RIVA_TELEMETRY_MAGIC (0x52495641)
    uint32_t version;         // RIVA_TELEMETRY_VERSION (1)
    uint32_t pid;             // PID do processo do jogo
    uint64_t frame_count;     // Contador monotônico de quadros
    uint64_t timestamp_ns;   // Timestamp monotônico em nanosegundos
    float frametime_ms;       // Duração do quadro em milissegundos
    float instant_fps;        // Taxa instantânea calculada (1000.0f / frametime_ms)
    char process_name[64];    // Nome do executável do jogo
};
#pragma pack(pop)
