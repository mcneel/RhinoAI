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
        GeminiModel gemini = new ("gemini-3.5-flash-lite", "Google");
        GenericHarness harness = new();
        Agent agent = new(gemini, harness);

        UserPermissions.BlockedVendors.Add(gemini.Vendor);

        CancellationTokenSource source = new(10_000);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", source.Token));
    }

    [Test]
    public void DenyModel()
    {
        GeminiModel gemini = new ("gemini-3.5-flash-lite", "Google");
        GenericHarness harness = new();
        Agent agent = new(gemini, harness);

        UserPermissions.BlockedVendors.Add(gemini.Name);

        CancellationTokenSource source = new(10_000);

        Assert.ThrowsAsync<PermissionException>(() => agent.SendAsync("What is the value of PI?", source.Token));
    }

}
