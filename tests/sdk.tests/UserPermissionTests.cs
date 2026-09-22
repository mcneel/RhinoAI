using Rhino.AI;
using Rhino.AI.Models;

namespace sdk.tests;

public class UserPermissionTests
{

    [SetUp]
    public void SetUp()
    {
        UserPermissions.BlockAll = false;
        UserPermissions.BlockedVendors.Clear();
        UserPermissions.BlockedModels.Clear();
        UserPermissions.ApprovedVendors.Clear();
        UserPermissions.ApprovedModels.Clear();
    }

    // TODO : Use TestCase and switch out vendors/models

    [TestCase]
    public void DenyVendor()
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        UserPermissions.BlockedVendors.Add(deepSeek.Vendor);

        CancellationTokenSource source = new(10_000);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", source.Token));
    }

    [Test]
    public void DenyModel()
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        UserPermissions.BlockedModels.Add(deepSeek.Name);

        CancellationTokenSource source = new(10_000);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", source.Token));
    }

}
