#include <windows.h>

#include <algorithm>
#include <cstdint>
#include <cstring>
#include <iostream>
#include <string>
#include <vector>

namespace {

constexpr std::uint32_t kStatusMagic = 0x53504850;
constexpr std::uint32_t kStatusVersion = 1;
constexpr std::size_t kStatusCapacity = 292;

std::wstring hook_ready_name(DWORD process_id) {
    return L"Local\\PhantomMonitor.InGameOverlay.HookReady." + std::to_wstring(process_id);
}

std::uint64_t read_sequence(const void* mapping) {
    auto* sequence = reinterpret_cast<const volatile LONG64*>(
        static_cast<const std::uint8_t*>(mapping) + 8);
    return static_cast<std::uint64_t>(ReadAcquire64(sequence));
}

bool read_initialization_status(DWORD process_id, std::wstring& error) {
    const auto name = L"Local\\PhantomMonitor.InGameOverlay.Status.v1."
        + std::to_wstring(process_id) + L".windows";
    HANDLE mapping = OpenFileMappingW(FILE_MAP_READ, FALSE, name.c_str());
    if (!mapping) {
        error = L"A DLL não publicou o canal de diagnóstico nativo.";
        return false;
    }
    void* view = MapViewOfFile(mapping, FILE_MAP_READ, 0, 0, kStatusCapacity);
    if (!view) {
        error = L"Não foi possível ler o diagnóstico nativo.";
        CloseHandle(mapping);
        return false;
    }

    std::vector<std::uint8_t> snapshot(kStatusCapacity);
    const auto before = read_sequence(view);
    if (before == 0 || (before & 1U) != 0) {
        error = L"O diagnóstico nativo ainda não está consistente.";
        UnmapViewOfFile(view);
        CloseHandle(mapping);
        return false;
    }
    std::memcpy(snapshot.data(), view, snapshot.size());
    MemoryBarrier();
    const auto after = read_sequence(view);
    UnmapViewOfFile(view);
    CloseHandle(mapping);

    std::uint32_t magic = 0, version = 0, pid = 0, state = 0;
    std::uint64_t captured = 0;
    std::memcpy(&magic, snapshot.data(), sizeof(magic));
    std::memcpy(&version, snapshot.data() + 4, sizeof(version));
    std::memcpy(&captured, snapshot.data() + 8, sizeof(captured));
    std::memcpy(&pid, snapshot.data() + 16, sizeof(pid));
    std::memcpy(&state, snapshot.data() + 24, sizeof(state));
    if (before != after || after != captured || (after & 1U) != 0
        || magic != kStatusMagic || version != kStatusVersion || pid != process_id) {
        error = L"O diagnóstico nativo está incompleto ou usa uma versão desconhecida.";
        return false;
    }

    const char* message = reinterpret_cast<const char*>(snapshot.data() + 36);
    const auto message_end = std::find(message, message + 256, '\0');
    const auto message_length = static_cast<int>(message_end - message);
    if (state == 5) {
        const auto wide_length = MultiByteToWideChar(CP_UTF8, 0, message,
            message_length, nullptr, 0);
        error = L"Falha ao instalar o hook: ";
        if (wide_length > 0) {
            const auto offset = error.size();
            error.resize(offset + static_cast<std::size_t>(wide_length));
            MultiByteToWideChar(CP_UTF8, 0, message, message_length,
                error.data() + offset, wide_length);
        } else {
            error += L"erro nativo sem descrição.";
        }
        return false;
    }
    if (state != 0 && state != 1 && state != 2 && state != 3 && state != 4) {
        error = L"O renderer publicou um estado desconhecido.";
        return false;
    }
    return true;
}

bool wait_for_native_initialization(HANDLE ready_event, DWORD process_id, std::wstring& error) {
    const auto wait = WaitForSingleObject(ready_event, 15000);
    if (wait != WAIT_OBJECT_0) {
        error = wait == WAIT_TIMEOUT
            ? L"O hook não terminou a inicialização em 15 segundos; feche e reinicie o jogo pelo PhantomMonitor e verifique bloqueios."
            : L"Falha ao aguardar a inicialização do hook.";
        return false;
    }
    return read_initialization_status(process_id, error);
}

bool inject_library(DWORD process_id, const std::wstring& library) {
    const auto ready_name = hook_ready_name(process_id);
    HANDLE ready_event = CreateEventW(nullptr, TRUE, FALSE, ready_name.c_str());
    if (!ready_event) {
        std::wcerr << L"Could not create hook initialization event: " << GetLastError() << L"\n";
        return false;
    }
    HANDLE process = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION
        | PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, process_id);
    if (!process) {
        std::wcerr << L"OpenProcess failed: " << GetLastError() << L"\n";
        CloseHandle(ready_event);
        return false;
    }
    const auto bytes = (library.size() + 1) * sizeof(wchar_t);
    void* remote_path = VirtualAllocEx(process, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remote_path) {
        std::wcerr << L"VirtualAllocEx failed: " << GetLastError() << L"\n";
        CloseHandle(process);
        CloseHandle(ready_event);
        return false;
    }
    SIZE_T written = 0;
    const bool copied = WriteProcessMemory(process, remote_path, library.c_str(), bytes, &written)
        && written == bytes;
    const auto load_library = reinterpret_cast<LPTHREAD_START_ROUTINE>(
        GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW"));
    HANDLE thread = copied && load_library
        ? CreateRemoteThread(process, nullptr, 0, load_library, remote_path, 0, nullptr)
        : nullptr;
    bool loaded = false;
    if (thread) {
        const auto wait = WaitForSingleObject(thread, 15000);
        DWORD module = 0;
        loaded = wait == WAIT_OBJECT_0 && GetExitCodeThread(thread, &module) && module != 0;
        if (!loaded) std::wcerr << L"Remote LoadLibrary failed or timed out: " << GetLastError() << L"\n";
        CloseHandle(thread);
    } else {
        std::wcerr << L"CreateRemoteThread failed: " << GetLastError() << L"\n";
    }
    VirtualFreeEx(process, remote_path, 0, MEM_RELEASE);
    CloseHandle(process);
    if (!loaded) {
        CloseHandle(ready_event);
        return false;
    }
    std::wstring initialization_error;
    const bool initialized = wait_for_native_initialization(ready_event, process_id, initialization_error);
    CloseHandle(ready_event);
    if (!initialized) std::wcerr << initialization_error << L"\n";
    return initialized;
}

std::wstring quote_argument(const std::wstring& value) {
    std::wstring result = L"\"";
    std::size_t slashes = 0;
    for (const wchar_t ch : value) {
        if (ch == L'\\') { ++slashes; continue; }
        if (ch == L'"') {
            result.append(slashes * 2 + 1, L'\\');
            result.push_back(ch);
            slashes = 0;
            continue;
        }
        result.append(slashes, L'\\');
        slashes = 0;
        result.push_back(ch);
    }
    result.append(slashes * 2, L'\\');
    result.push_back(L'"');
    return result;
}

int launch_target(const std::wstring& library, const std::wstring& executable,
                  const std::wstring& arguments) {
    std::wstring command_line = quote_argument(executable);
    if (!arguments.empty()) { command_line.push_back(L' '); command_line += arguments; }
    STARTUPINFOW startup{};
    startup.cb = sizeof(startup);
    PROCESS_INFORMATION process{};
    const auto working_directory = executable.substr(0, executable.find_last_of(L"\\/"));
    if (!CreateProcessW(executable.c_str(), command_line.data(), nullptr, nullptr, FALSE,
            CREATE_SUSPENDED, nullptr, working_directory.empty() ? nullptr : working_directory.c_str(),
            &startup, &process)) {
        std::wcerr << L"CreateProcess failed: " << GetLastError() << L"\n";
        return 2;
    }

    const bool injected = inject_library(process.dwProcessId, library);
    const auto resumed = ResumeThread(process.hThread) != static_cast<DWORD>(-1);
    if (!resumed) {
        std::wcerr << L"ResumeThread failed: " << GetLastError() << L"\n";
        TerminateProcess(process.hProcess, 3);
        CloseHandle(process.hThread);
        CloseHandle(process.hProcess);
        return 5;
    } else if (injected) {
        std::wcout << L"Started PID " << process.dwProcessId << L" with the in-game overlay hook.\n";
    } else {
        std::wcerr << L"The game was started without the overlay because hook injection failed.\n";
    }
    CloseHandle(process.hThread);
    CloseHandle(process.hProcess);
    return injected ? 0 : 4;
}

} // namespace

int wmain(int argc, wchar_t** argv) {
    if (argc == 4 && std::wstring(argv[1]) == L"--inject") {
        wchar_t* end = nullptr;
        const auto parsed = wcstoul(argv[2], &end, 10);
        if (!end || *end != L'\0' || parsed == 0 || parsed > 0xFFFFFFFFUL) return 2;
        if (!inject_library(static_cast<DWORD>(parsed), argv[3])) return 3;
        std::wcout << L"Injected PID " << parsed << L".\n";
        return 0;
    }
    if (argc == 5 && std::wstring(argv[1]) == L"--launch")
        return launch_target(argv[2], argv[3], argv[4]);
    std::wcerr << L"Usage: PhantomOverlayLauncher --inject <pid> <dll> | --launch <dll> <exe> <args>\n";
    return 1;
}
