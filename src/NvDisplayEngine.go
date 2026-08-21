//go:build windows

package main

import (
    "encoding/json"
    "errors"
    "fmt"
    "os"
    "path/filepath"
    "runtime"
    "sort"
    "strings"
    "syscall"
    "time"
    "unsafe"
)

const (
    iidInitialize       = 0x0150e828
    iidUnload           = 0xd22bdd7e
    iidGetErrorMessage  = 0x6c2d048c
    iidGetDisplayConfig = 0x11abccf8
    iidSetDisplayConfig = 0x5d8cf8de

    nvapiOK = 0

    flagValidateOnly        = 0x00000001
    flagSaveToPersistence   = 0x00000002
    flagDriverReloadAllowed = 0x00000004
    flagForceModeEnumeration = 0x00000008
    flagForceCommitVidPn     = 0x00000010
)

type nvResolution struct {
    Width      uint32
    Height     uint32
    ColorDepth uint32
}

type nvPosition struct {
    X int32
    Y int32
}

type nvSourceMode struct {
    Resolution  nvResolution
    ColorFormat uint32
    Position    nvPosition
    Spanning    uint32
    Flags       uint32
}

type nvTimingExt struct {
    Flag   uint32
    RR     uint16
    RRx1k  uint32
    Aspect uint32
    Rep    uint16
    Status uint32
    Name   [40]byte
}

type nvTiming struct {
    HVisible    uint16
    HBorder     uint16
    HFrontPorch uint16
    HSyncWidth  uint16
    HTotal      uint16
    HSyncPol    uint8

    VVisible    uint16
    VBorder     uint16
    VFrontPorch uint16
    VSyncWidth  uint16
    VTotal      uint16
    VSyncPol    uint8

    Interlaced uint16
    Pclk       uint32
    Etc        nvTimingExt
}

type nvAdvancedTargetInfo struct {
    Version        uint32
    Rotation       uint32
    Scaling        uint32
    RefreshRate1K  uint32
    Flags          uint32
    Connector      uint32
    TVFormat       uint32
    TimingOverride uint32
    Timing         nvTiming
}

type nvTargetInfo struct {
    DisplayID uint32
    Details   uintptr
    TargetID  uint32
}

type nvPathInfo struct {
    Version         uint32
    SourceID        uint32
    TargetInfoCount uint32
    TargetInfo      uintptr
    SourceModeInfo  uintptr
    Flags           uint32
    OSAdapterID     uintptr
}

type profile struct {
    Format    string        `json:"format"`
    CreatedAt string        `json:"createdAt"`
    Paths     []profilePath `json:"paths"`
}

type profilePath struct {
    SourceMode profileSource   `json:"sourceMode"`
    Targets    []profileTarget `json:"targets"`
}

type profileSource struct {
    Width      uint32 `json:"width"`
    Height     uint32 `json:"height"`
    ColorDepth uint32 `json:"colorDepth"`
    ColorFormat uint32 `json:"colorFormat"`
    X          int32  `json:"x"`
    Y          int32  `json:"y"`
    Spanning   uint32 `json:"spanning"`
    Flags      uint32 `json:"flags"`
}

type profileTarget struct {
    DisplayID uint32 `json:"displayId"`
    TargetID  uint32 `json:"targetId"`
    Details   []byte `json:"details,omitempty"`
}

type nvapi struct {
    dll       *syscall.LazyDLL
    query     *syscall.LazyProc
    initFn    uintptr
    unloadFn  uintptr
    errorFn   uintptr
    getCfgFn  uintptr
    setCfgFn  uintptr
}

func makeVersion(size uintptr, version uint32) uint32 {
    return uint32(size) | (version << 16)
}

func pathVersion() uint32 { return makeVersion(unsafe.Sizeof(nvPathInfo{}), 2) }
func advancedVersion() uint32 { return makeVersion(unsafe.Sizeof(nvAdvancedTargetInfo{}), 1) }

func statusCode(r uintptr) int32 { return int32(uint32(r)) }

func openNVAPI() (*nvapi, error) {
    if unsafe.Sizeof(nvPathInfo{}) != 48 || unsafe.Sizeof(nvTargetInfo{}) != 24 || unsafe.Sizeof(nvSourceMode{}) != 32 || unsafe.Sizeof(nvAdvancedTargetInfo{}) != 128 {
        return nil, fmt.Errorf("unexpected NVAPI structure sizes: path=%d target=%d source=%d details=%d", unsafe.Sizeof(nvPathInfo{}), unsafe.Sizeof(nvTargetInfo{}), unsafe.Sizeof(nvSourceMode{}), unsafe.Sizeof(nvAdvancedTargetInfo{}))
    }

    dll := syscall.NewLazyDLL("nvapi64.dll")
    query := dll.NewProc("nvapi_QueryInterface")
    if err := query.Find(); err != nil {
        return nil, fmt.Errorf("nvapi64.dll / nvapi_QueryInterface not available: %w", err)
    }

    api := &nvapi{dll: dll, query: query}
    var err error
    if api.initFn, err = api.lookup(iidInitialize); err != nil { return nil, err }
    if api.unloadFn, err = api.lookup(iidUnload); err != nil { return nil, err }
    if api.errorFn, err = api.lookup(iidGetErrorMessage); err != nil { return nil, err }
    if api.getCfgFn, err = api.lookup(iidGetDisplayConfig); err != nil { return nil, err }
    if api.setCfgFn, err = api.lookup(iidSetDisplayConfig); err != nil { return nil, err }

    r, _, _ := syscall.SyscallN(api.initFn)
    if code := statusCode(r); code != nvapiOK {
        return nil, fmt.Errorf("NvAPI_Initialize failed: %s", api.statusText(code))
    }
    return api, nil
}

func (a *nvapi) lookup(id uint32) (uintptr, error) {
    r, _, _ := a.query.Call(uintptr(id))
    if r == 0 {
        return 0, fmt.Errorf("NVAPI interface 0x%08x is unavailable", id)
    }
    return r, nil
}

func (a *nvapi) close() {
    if a != nil && a.unloadFn != 0 {
        syscall.SyscallN(a.unloadFn)
    }
}

func (a *nvapi) statusText(code int32) string {
    if a == nil || a.errorFn == 0 {
        return fmt.Sprintf("NVAPI status %d", code)
    }
    var buf [64]byte
    r, _, _ := syscall.SyscallN(a.errorFn, uintptr(uint32(code)), uintptr(unsafe.Pointer(&buf[0])))
    if statusCode(r) != nvapiOK {
        return fmt.Sprintf("NVAPI status %d", code)
    }
    n := 0
    for n < len(buf) && buf[n] != 0 { n++ }
    msg := strings.TrimSpace(string(buf[:n]))
    if msg == "" { return fmt.Sprintf("NVAPI status %d", code) }
    return fmt.Sprintf("%s (%d)", msg, code)
}

func (a *nvapi) getProfile() (*profile, error) {
    var count uint32
    r, _, _ := syscall.SyscallN(a.getCfgFn, uintptr(unsafe.Pointer(&count)), 0)
    if code := statusCode(r); code != nvapiOK {
        return nil, fmt.Errorf("NvAPI_DISP_GetDisplayConfig pass 1 failed: %s", a.statusText(code))
    }
    if count == 0 || count > 16 {
        return nil, fmt.Errorf("NVAPI reported an unexpected active path count: %d", count)
    }

    paths := make([]nvPathInfo, count)
    for i := range paths { paths[i].Version = pathVersion() }

    r, _, _ = syscall.SyscallN(a.getCfgFn, uintptr(unsafe.Pointer(&count)), uintptr(unsafe.Pointer(&paths[0])))
    if code := statusCode(r); code != nvapiOK {
        return nil, fmt.Errorf("NvAPI_DISP_GetDisplayConfig pass 2 failed: %s", a.statusText(code))
    }

    targets := make([][]nvTargetInfo, count)
    details := make([][]nvAdvancedTargetInfo, count)
    sources := make([]nvSourceMode, count)

    for i := range paths {
        tc := paths[i].TargetInfoCount
        if tc == 0 || tc > 16 {
            return nil, fmt.Errorf("path %d has an unexpected target count: %d", i, tc)
        }
        targets[i] = make([]nvTargetInfo, tc)
        details[i] = make([]nvAdvancedTargetInfo, tc)
        for j := range targets[i] {
            details[i][j].Version = advancedVersion()
            targets[i][j].Details = uintptr(unsafe.Pointer(&details[i][j]))
        }
        paths[i].TargetInfo = uintptr(unsafe.Pointer(&targets[i][0]))
        paths[i].SourceModeInfo = uintptr(unsafe.Pointer(&sources[i]))
    }

    r, _, _ = syscall.SyscallN(a.getCfgFn, uintptr(unsafe.Pointer(&count)), uintptr(unsafe.Pointer(&paths[0])))
    runtime.KeepAlive(paths)
    runtime.KeepAlive(targets)
    runtime.KeepAlive(details)
    runtime.KeepAlive(sources)
    if code := statusCode(r); code != nvapiOK {
        return nil, fmt.Errorf("NvAPI_DISP_GetDisplayConfig pass 3 failed: %s", a.statusText(code))
    }

    out := &profile{Format: "display-modes-nvapi-v1", CreatedAt: time.Now().Format(time.RFC3339)}
    for i := range paths {
        if paths[i].Flags&1 != 0 {
            return nil, errors.New("a non-NVIDIA display path is active; this NVAPI engine currently supports NVIDIA-driven displays only")
        }
        src := sources[i]
        pp := profilePath{SourceMode: profileSource{
            Width: src.Resolution.Width, Height: src.Resolution.Height, ColorDepth: src.Resolution.ColorDepth,
            ColorFormat: src.ColorFormat, X: src.Position.X, Y: src.Position.Y, Spanning: src.Spanning, Flags: src.Flags,
        }}
        for j := range targets[i] {
            raw := make([]byte, unsafe.Sizeof(nvAdvancedTargetInfo{}))
            copy(raw, unsafe.Slice((*byte)(unsafe.Pointer(&details[i][j])), int(unsafe.Sizeof(nvAdvancedTargetInfo{}))))
            pp.Targets = append(pp.Targets, profileTarget{DisplayID: targets[i][j].DisplayID, TargetID: targets[i][j].TargetID, Details: raw})
        }
        out.Paths = append(out.Paths, pp)
    }
    return out, nil
}

func (a *nvapi) applyProfile(p *profile) error {
    if p == nil || p.Format != "display-modes-nvapi-v1" || len(p.Paths) == 0 {
        return errors.New("invalid or unsupported NVAPI display profile")
    }
    if len(p.Paths) > 16 { return errors.New("profile contains too many display paths") }

    count := len(p.Paths)
    paths := make([]nvPathInfo, count)
    targets := make([][]nvTargetInfo, count)
    details := make([][]nvAdvancedTargetInfo, count)
    sources := make([]nvSourceMode, count)

    for i, pp := range p.Paths {
        if len(pp.Targets) == 0 || len(pp.Targets) > 16 {
            return fmt.Errorf("profile path %d has an invalid target count", i)
        }
        src := &sources[i]
        src.Resolution = nvResolution{Width: pp.SourceMode.Width, Height: pp.SourceMode.Height, ColorDepth: pp.SourceMode.ColorDepth}
        src.ColorFormat = pp.SourceMode.ColorFormat
        src.Position = nvPosition{X: pp.SourceMode.X, Y: pp.SourceMode.Y}
        src.Spanning = pp.SourceMode.Spanning
        src.Flags = pp.SourceMode.Flags

        targets[i] = make([]nvTargetInfo, len(pp.Targets))
        details[i] = make([]nvAdvancedTargetInfo, len(pp.Targets))
        for j, pt := range pp.Targets {
            targets[i][j].DisplayID = pt.DisplayID
            targets[i][j].TargetID = pt.TargetID
            if len(pt.Details) == int(unsafe.Sizeof(nvAdvancedTargetInfo{})) {
                copy(unsafe.Slice((*byte)(unsafe.Pointer(&details[i][j])), int(unsafe.Sizeof(nvAdvancedTargetInfo{}))), pt.Details)
                details[i][j].Version = advancedVersion()
                details[i][j].TimingOverride = 1
                targets[i][j].Details = uintptr(unsafe.Pointer(&details[i][j]))
            }
        }

        paths[i].Version = pathVersion()
        paths[i].SourceID = 0
        paths[i].TargetInfoCount = uint32(len(targets[i]))
        paths[i].TargetInfo = uintptr(unsafe.Pointer(&targets[i][0]))
        paths[i].SourceModeInfo = uintptr(unsafe.Pointer(src))
        paths[i].Flags = 0
        paths[i].OSAdapterID = 0
    }

    r, _, _ := syscall.SyscallN(a.setCfgFn, uintptr(uint32(count)), uintptr(unsafe.Pointer(&paths[0])), uintptr(flagValidateOnly))
    runtime.KeepAlive(paths); runtime.KeepAlive(targets); runtime.KeepAlive(details); runtime.KeepAlive(sources)
    if code := statusCode(r); code != nvapiOK {
        return fmt.Errorf("NVAPI rejected the saved topology during validation: %s", a.statusText(code))
    }

    flags := uintptr(flagSaveToPersistence | flagForceModeEnumeration | flagForceCommitVidPn)
    r, _, _ = syscall.SyscallN(a.setCfgFn, uintptr(uint32(count)), uintptr(unsafe.Pointer(&paths[0])), flags)
    runtime.KeepAlive(paths); runtime.KeepAlive(targets); runtime.KeepAlive(details); runtime.KeepAlive(sources)
    if code := statusCode(r); code != nvapiOK {
        retryFlags := uintptr(flagSaveToPersistence | flagDriverReloadAllowed | flagForceModeEnumeration | flagForceCommitVidPn)
        r, _, _ = syscall.SyscallN(a.setCfgFn, uintptr(uint32(count)), uintptr(unsafe.Pointer(&paths[0])), retryFlags)
        runtime.KeepAlive(paths); runtime.KeepAlive(targets); runtime.KeepAlive(details); runtime.KeepAlive(sources)
        if code2 := statusCode(r); code2 != nvapiOK {
            return fmt.Errorf("NvAPI_DISP_SetDisplayConfig failed: %s; retry: %s", a.statusText(code), a.statusText(code2))
        }
    }

    time.Sleep(900 * time.Millisecond)
    current, err := a.getProfile()
    if err != nil { return fmt.Errorf("topology applied but verification failed: %w", err) }
    if err := compareProfiles(p, current); err != nil {
        return fmt.Errorf("NVAPI returned success, but Windows did not settle into the captured topology: %w", err)
    }
    return nil
}

func compareProfiles(want, got *profile) error {
    if len(want.Paths) != len(got.Paths) {
        return fmt.Errorf("expected %d active paths, got %d", len(want.Paths), len(got.Paths))
    }

    type sig struct { Display uint32; X, Y int32; W, H uint32; Primary bool }
    flatten := func(p *profile) []sig {
        var out []sig
        for _, path := range p.Paths {
            primary := (path.SourceMode.Flags & 1) != 0
            for _, t := range path.Targets {
                out = append(out, sig{Display: t.DisplayID, X: path.SourceMode.X, Y: path.SourceMode.Y, W: path.SourceMode.Width, H: path.SourceMode.Height, Primary: primary})
            }
        }
        sort.Slice(out, func(i,j int) bool { return out[i].Display < out[j].Display })
        return out
    }
    a, b := flatten(want), flatten(got)
    if len(a) != len(b) { return fmt.Errorf("expected %d active targets, got %d", len(a), len(b)) }
    for i := range a {
        if a[i].Display != b[i].Display || a[i].X != b[i].X || a[i].Y != b[i].Y || a[i].W != b[i].W || a[i].H != b[i].H || a[i].Primary != b[i].Primary {
            return fmt.Errorf("display state mismatch after apply")
        }
    }
    return nil
}

func writeProfile(path string, p *profile) error {
    if err := os.MkdirAll(filepath.Dir(path), 0755); err != nil { return err }
    data, err := json.MarshalIndent(p, "", "  ")
    if err != nil { return err }
    tmp := path + ".tmp"
    if err := os.WriteFile(tmp, data, 0644); err != nil { return err }
    return os.Rename(tmp, path)
}

func readProfile(path string) (*profile, error) {
    data, err := os.ReadFile(path)
    if err != nil { return nil, err }
    var p profile
    if err := json.Unmarshal(data, &p); err != nil { return nil, err }
    return &p, nil
}

func usage() {
    fmt.Fprintln(os.Stderr, "DisplayDeck NVAPI Engine")
    fmt.Fprintln(os.Stderr, "usage: NvDisplayEngine.exe probe | capture <profile.json> | apply <profile.json> | dump")
}

func main() {
    if len(os.Args) < 2 { usage(); os.Exit(2) }
    api, err := openNVAPI()
    if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(10) }
    defer api.close()

    switch strings.ToLower(os.Args[1]) {
    case "probe":
        p, err := api.getProfile()
        if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(11) }
        fmt.Printf("NVAPI ready; active paths: %d\n", len(p.Paths))
    case "capture":
        if len(os.Args) != 3 { usage(); os.Exit(2) }
        p, err := api.getProfile()
        if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(12) }
        if err := writeProfile(os.Args[2], p); err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(13) }
        fmt.Printf("Captured %d active path(s)\n", len(p.Paths))
    case "apply":
        if len(os.Args) != 3 { usage(); os.Exit(2) }
        p, err := readProfile(os.Args[2])
        if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(14) }
        if err := api.applyProfile(p); err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(15) }
        fmt.Println("Display topology applied and verified")
    case "dump":
        p, err := api.getProfile()
        if err != nil { fmt.Fprintln(os.Stderr, err); os.Exit(16) }
        data, _ := json.MarshalIndent(p, "", "  ")
        fmt.Println(string(data))
    default:
        usage(); os.Exit(2)
    }
}
