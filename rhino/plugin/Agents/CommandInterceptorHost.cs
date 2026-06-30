namespace RhMcp;

/// <summary>
/// Owns a <see cref="CommandInterceptor"/> per open document, wiring them up on construction and
/// tearing them down on <see cref="Dispose"/>. Tracks documents via Rhino's open/close events.
/// </summary>
internal sealed class CommandInterceptorHost : IDisposable
{
    private Dictionary<uint, CommandInterceptor> Interceptors { get; } = new();

    private bool Attached { get; set; }

    public CommandInterceptorHost()
    {
        Attached = true;
        RhinoDoc.BeginOpenDocument += OnBeginOpen;
        RhinoDoc.NewDocument += OnNewDocument;
        RhinoDoc.CloseDocument += OnClose;
        foreach (RhinoDoc doc in RhinoDoc.OpenDocuments())
            Add(doc);
    }

    public void Dispose()
    {
        if (!Attached)
            return;
        Attached = false;
        RhinoDoc.BeginOpenDocument -= OnBeginOpen;
        RhinoDoc.NewDocument -= OnNewDocument;
        RhinoDoc.CloseDocument -= OnClose;
        foreach (CommandInterceptor interceptor in Interceptors.Values)
            interceptor.Dispose();
        Interceptors.Clear();
    }

    private void OnBeginOpen(object? sender, DocumentOpenEventArgs e) => Add(e.Document);

    private void OnNewDocument(object? sender, DocumentEventArgs e) => Add(e.Document);

    private void OnClose(object? sender, DocumentEventArgs e)
    {
        if (Interceptors.Remove(e.DocumentSerialNumber, out CommandInterceptor? interceptor))
            interceptor.Dispose();
    }

    private void Add(RhinoDoc? doc)
    {
        if (doc is null || Interceptors.ContainsKey(doc.RuntimeSerialNumber))
            return;
        Interceptors[doc.RuntimeSerialNumber] = new CommandInterceptor(doc);
    }
}
