using System.Runtime.CompilerServices;

// Lets the test project reach private/internal helpers (e.g. ProxyDispatcher's
// pure parsing routines) without exposing them on the public API surface.
[assembly: InternalsVisibleTo("Router.Tests")]
