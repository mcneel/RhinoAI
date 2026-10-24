# RhinoAI SDK

## PlugIns

Using the SDK you can register a 3rd party plugin and hook into Rhino's Agentic system.
Once registered you receive a permission token that must be used for every call.

``` cs
PlugInToken token = PlugInRegistry.Register();
```

Skills can be registered to the AI Panel to help RhinoAI better know your plugin.

``` cs
PlugInRegistry.RegisterPanelSkill(token, skill);
```

MCPs can also be registered to the AI Panel to give RhinoAI more toolsets.

``` cs
MemoryMcp mcp = new ("MyPluginsMcp");
mcp.RegisterTool(MyTool);
PlugInRegistry.RegisterPanelMcp(mcp, skill);
```

## Agents

With the SDK you can create an AI Agent for any problem or task.
`GetPreferredAgent` will give you the users preferred Agent.
This Agent is **not** the same as the AI Panel's and can operate concurrently to it.

``` cs
Agent agent = Agent.GetPreferredAgent(token, "");
IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?");
```

## Models

The brains of the Agent is the model. API Models, Desktop models and local Models are available.

## Harnesses

Desktop Models have their own harness and will use them. For API Models, Local Models or any non-hosted models the GenericHarness (or a custom harness) can be used.

## MCPs

MCPs are toolboxes for Agents to use in their harness.

## Permissions

The PlugInToken registered earlier is keyed to a permission set the user approves.
Before the first `agent.SendAsync` call can succeed, the user **must** approve your plugin in the AI Settings.

After that various calls run by the agents will be approved by the user.

``` cs
PlugInToken token = PlugInRegistry.Register();
```
