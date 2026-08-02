#define SECURITYCORE_EXPORTS
#include "SecurityCore.h"

BOOL WINAPI SetWindowCaptureProtection(HWND hwnd, BOOL enable) {
    if (!hwnd || !IsWindow(hwnd)) return FALSE;

    // WDA_EXCLUDEFROMCAPTURE = 0x00000011 (Windows 10 2004+)
    // WDA_MONITOR = 0x00000001
    DWORD affinity = enable ? 0x00000011 : 0x00000000;
    
    BOOL result = SetWindowDisplayAffinity(hwnd, affinity);
    if (!result && enable) {
        // Fallback to WDA_MONITOR for older Windows versions
        result = SetWindowDisplayAffinity(hwnd, 0x00000001);
    }
    return result;
}

void WINAPI SecureZeroMemoryBuffer(void* ptr, size_t size) {
    if (ptr && size > 0) {
        SecureZeroMemory(ptr, size);
    }
}

BOOL WINAPI ProtectMemoryRegion(void* ptr, size_t size, BOOL lock) {
    if (!ptr || size == 0) return FALSE;

    if (lock) {
        return VirtualLock(ptr, size);
    } else {
        return VirtualUnlock(ptr, size);
    }
}
