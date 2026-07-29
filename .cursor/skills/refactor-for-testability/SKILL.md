---
name: refactor-for-testability
description: >-
  Refactor static dependencies into injectable interfaces for testability.
  Use when extracting an interface from a static class, making a class testable,
  creating test fakes, or applying dependency injection to an existing component.
---

# Refactor Static Dependencies for Testability

This is the pattern used throughout the codebase to replace static method calls with injectable interfaces. Follow these steps exactly.

## Workflow

### 1. Identify the static dependency

Find direct calls to static methods that make a class hard to test:

```csharp
// Before — untestable
KeyboardInjector.SetKey(vk, true);
MouseInjector.Move(dx, dy);
```

### 2. Extract an interface

Create an interface in the same namespace with only the methods consumed:

```csharp
// src/Host/Mapping/IKeyboardSink.cs
public interface IKeyboardSink
{
    void SetKey(int virtualKey, bool down);
    void SetModifier(KeyModifiers modifiers, bool down);
}
```

### 3. Create the production implementation

Wrap the existing static calls in a sealed class with a singleton `Instance`:

```csharp
// src/Host/Mapping/Win32KeyboardSink.cs
public sealed class Win32KeyboardSink : IKeyboardSink
{
    public static readonly Win32KeyboardSink Instance = new();

    public void SetKey(int virtualKey, bool down)
    {
        // existing SendInput P/Invoke logic
    }
}
```

### 4. Add constructor injection with default fallback

The consuming class takes the interface as a nullable parameter, falling back to the production singleton:

```csharp
public sealed class MappingEngine
{
    readonly IKeyboardSink _keyboard;

    public MappingEngine(IKeyboardSink? keyboard = null)
    {
        _keyboard = keyboard ?? Win32KeyboardSink.Instance;
    }
}
```

### 5. Create a test fake

Place in `tests/.../Fakes/` using the naming convention:

- **`Recording*`** — records calls for assertion (most common):

```csharp
public sealed class RecordingKeyboardSink : IKeyboardSink
{
    public List<(int Vk, bool Down)> Keys { get; } = new();

    public void SetKey(int virtualKey, bool down)
        => Keys.Add((virtualKey, down));
}
```

- **`Fake*`** — configurable behavior for integration-style tests.
- **`Test*`** — simple state holder (e.g. `TestSteamState { bool IsSteamRunning }`).

### 6. Delete the old static class

Remove the original static class file (e.g. `KeyboardInjector.cs`).

### 7. Update tests

Inject the fake into the SUT and assert on recorded calls:

```csharp
readonly RecordingKeyboardSink _keyboard = new();
readonly MappingEngine _engine;

public MyTests()
{
    _engine = new MappingEngine(keyboard: _keyboard);
}

[Fact]
public void Key_action_presses_virtual_key()
{
    // arrange + act
    _engine.Map(frame, profile);

    // assert
    _keyboard.Keys.Should().Contain((0x41, true));
}
```

## Checklist

- [ ] Interface extracted with minimal surface area
- [ ] Production implementation is `sealed` with `static readonly Instance`
- [ ] Constructor uses `T? param = null` with `?? Impl.Instance` fallback
- [ ] Old static class deleted
- [ ] Test fake created in `Fakes/` with correct naming prefix
- [ ] Existing tests updated to use the fake
- [ ] Build passes with `TreatWarningsAsErrors`

## Prior Art in This Repo

| Static class | Interface | Implementation | Fake |
|---|---|---|---|
| `KeyboardInjector` | `IKeyboardSink` | `Win32KeyboardSink` | `RecordingKeyboardSink` |
| `MouseInjector` | `IMouseSink` | `Win32MouseSink` | `RecordingMouseSink` |
| `ViiperHealth` (static) | `IViiperHealth` | `ViiperHealth` | `FakeViiperHealth` |
| `ViiperXbox360Client` | `IViiperClient` | `ViiperXbox360Client` | `FakeViiperClient` |
| `SteamWatcher` | `ISteamState` | `SteamWatcher` | `TestSteamState` |
| `ForegroundWatcher` | `IForegroundState` | `ForegroundWatcher` | `TestForegroundState` |
