#pragma once

#ifdef SECURITYCORE_EXPORTS
#define SECURITY_API __declspec(dllexport)
#else
#define SECURITY_API __declspec(dllimport)
#endif

#include <windows.h>

extern "C" {
    SECURITY_API BOOL WINAPI SetWindowCaptureProtection(HWND hwnd, BOOL enable);
    SECURITY_API void WINAPI SecureZeroMemoryBuffer(void* ptr, size_t size);
    SECURITY_API BOOL WINAPI ProtectMemoryRegion(void* ptr, size_t size, BOOL lock);
}
