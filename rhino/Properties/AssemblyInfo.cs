using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Rhino.PlugIns;

// Plug-in Description Attributes - all of these are optional.
// These will show in Rhino's option dialog, in the tab Plug-ins.
[assembly: PlugInDescription(DescriptionType.Address, "146 Canal St Suite 320\nSeattle WA 98103")]
[assembly: PlugInDescription(DescriptionType.Country, "USA")]
[assembly: PlugInDescription(DescriptionType.Email, "tech@mcneel.com")]
[assembly: PlugInDescription(DescriptionType.Phone, "206-545-7000")]
[assembly: PlugInDescription(DescriptionType.Organization, "Robert McNeel & Associates")]
[assembly: PlugInDescription(DescriptionType.UpdateUrl, "http://www.updates.mcneel.com")]
[assembly: PlugInDescription(DescriptionType.WebSite, "http://www.mcneel.com")]

// The following GUID is for the ID of the typelib if this project is exposed to COM
// This will also be the Guid of the Rhino plug-in
[assembly: Guid("2668d7ed-f507-4a68-8295-8172147a0e39")]
