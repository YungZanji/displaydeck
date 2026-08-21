# Third-party notice

DisplayDeck uses NVIDIA NVAPI through the `nvapi_QueryInterface` entry point provided by the NVIDIA display driver installed on the user's PC.

No NVIDIA SDK DLL is redistributed with DisplayDeck. `NvDisplayEngine.exe` dynamically loads `nvapi64.dll` from the installed NVIDIA driver.

NVIDIA and NVAPI are trademarks or technologies of NVIDIA Corporation. DisplayDeck is not affiliated with or endorsed by NVIDIA.
