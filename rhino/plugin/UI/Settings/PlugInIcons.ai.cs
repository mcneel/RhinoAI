using System.IO;

using Eto.Drawing;

using Rhino.Runtime;

using Rhino.AI.PlugIns;

namespace Rhino.AI.UI.Settings;

internal static class PlugInIcons
{
    public static Image? Of(Guid id)
    {
        foreach (KeyValuePair<PlugInToken, PlugInRegistry.AgenticPlugIn> pair in AIInstances.Registry.PlugIns)
        {
            if (pair.Key.Id == id)
                return Load(pair.Value);
        }

        return null;
    }

    private static Image? Load(PlugInRegistry.AgenticPlugIn plugIn)
    {
        if (string.IsNullOrEmpty(plugIn.IconResource))
            return null;

        try
        {
            using Stream? stream = plugIn.Assembly.GetManifestResourceStream(plugIn.IconResource);
            if (stream is null)
                return null;

            MemoryStream copy = new();
            stream.CopyTo(copy);
            copy.Position = 0;
            return new Icon(copy);
        }
        catch (Exception ex)
        {
            HostUtils.LogDebugEvent(string.Format("[rhino-ai] AI settings could not read the icon '{0}': {1}\n", plugIn.IconResource, ex.Message));
            return null;
        }
    }
}
