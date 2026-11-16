using System.Linq;

using Rhino.AI;
using Rhino.AI.Models;

namespace sdk.tests;

/// <summary>
/// Local models are attributed to the server the user runs, not hardcoded to LM Studio.
/// None of these touch the network.
/// </summary>
public class LocalModelTests
{

    [TestCase("Local")]
    [TestCase("LM Studio")]
    [TestCase("Apple")]
    public void ModelKeepsTheVendorOfItsDefinition(string vendor)
    {
        HttpAgentDefinition definition = new(vendor, vendor)
        {
            Protocol = ProtocolKind.Completions,
            Host = "http://localhost:1234",
            ModelsRoute = "/v1/models",
        };
        definition.Add(new ModelDefinition(vendor, "Loaded model", "default"));

        IModel? model = definition.GetModel("default");

        Assert.That(model, Is.Not.Null);
        Assert.That(model!.Vendor, Is.EqualTo(vendor));
    }

    [Test]
    public void ShippedLocalPresetNeedsNoConfiguration()
    {
        Agents agents = new();
        Assert.That(agents.LoadAgentsFromJson(), Is.True);

        HttpAgentDefinition? local = agents.Definitions.OfType<HttpAgentDefinition>().FirstOrDefault(d => d.Vendor == "Local");

        Assert.That(local, Is.Not.Null);
        Assert.That(local!.Host, Is.EqualTo("http://localhost:1234"));
        Assert.That(local.Available, Is.True);
        Assert.That(local.CurrentModel, Is.Not.Null);
        Assert.That(local.CurrentModel!.Vendor, Is.EqualTo("Local"));
    }

    [Test]
    public void LmStudioPresetIsStillShipped()
    {
        Agents agents = new();
        Assert.That(agents.LoadAgentsFromJson(), Is.True);

        Assert.That(agents.Definitions.Any(d => d.Vendor == "LM Studio"), Is.True);
    }

}
