using System;
using System.Collections.Generic;

namespace Rhino.AI.Statistics;

internal sealed record PlugInStatisticsDocument(IReadOnlyList<PlugInUsageEntry> PlugIns);

internal sealed record PlugInUsageEntry(Guid Id, string Name, IReadOnlyList<ModelUsageEntry> Models);

internal sealed record ModelUsageEntry(string Name, string Vendor, int TokenCount);
