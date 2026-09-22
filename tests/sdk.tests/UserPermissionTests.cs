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

    [Test, CancelAfter(5000)]
    public void DenyVendor(CancellationToken token)
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        UserPermissions.BlockedVendors.Add(deepSeek.Vendor);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", token));
    }

    [Test, CancelAfter(5000)]
    public void DenyModel(CancellationToken token)
    {
        DeepSeekModel deepSeek = DeepSeekModel.Default();
        GenericHarness harness = new();
        Agent agent = new(deepSeek, harness);

        UserPermissions.BlockedModels.Add(deepSeek.Name);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", token));
    }

}
