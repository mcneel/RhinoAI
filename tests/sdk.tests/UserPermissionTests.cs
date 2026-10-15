using Rhino.AI;
using Rhino.AI.Models;

namespace sdk.tests;

[NonParallelizable]
public class UserPermissionTests
{
    
    private static Rhino.AI.PlugIns.PlugInToken Token => Rhino.AI.PlugIns.PlugInToken.Invalid;

    [SetUp]
    public void SetUp()
    {
        Instances.Preferences.BlockAll = false;
        Instances.Preferences.BlockedVendors.Clear();
        Instances.Preferences.BlockedModels.Clear();
        Instances.Preferences.ApprovedVendors.Clear();
        Instances.Preferences.ApprovedModels.Clear();
    }

    // TODO : Use TestCase and switch out vendors/models

    [Test, CancelAfter(5000)]
    public void DenyVendor(CancellationToken token)
    {
        Agent? agent = new TestDefinition().GetAgent(Token);
        Assert.That(agent, Is.Not.Null);

        Instances.Preferences.BlockedVendors.Add(agent.Definition.Vendor);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", token));
    }

    [Test, CancelAfter(5000)]
    public void DenyModel(CancellationToken token)
    {
        Agent? agent = new TestDefinition().GetAgent(Token);
        Assert.That(agent, Is.Not.Null);

        Instances.Preferences.BlockedModels.Add(agent.Definition.CurrentModel!.Name);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", token));
    }

}
