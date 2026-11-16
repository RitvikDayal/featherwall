using System.Runtime.CompilerServices;

// The shell assembly (Engine, widgets, tray UI) is the only consumer of the engine's raw
// interop: WASAPI structs for the spectrum meter, DComp factories for composition surfaces.
// Keeping those types internal to PrismOS.Core avoids publishing a COM surface as public API
// while still letting the one assembly that needs them use them.
[assembly: InternalsVisibleTo("prismos")]
