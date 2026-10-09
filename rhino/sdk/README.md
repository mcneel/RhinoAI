# RhinoAI SDK

## Agents

For the SDK you can create an Agent for each problem or task.

``` cs
IModel model = DeepSeekModel.Completions("deepseek-flash");
GenericHarness harness = new(Token);
Agent agent = new(Token, model, harness);

IEnumerable<ITurn> turns = await agent.SendAsync("Hello! What is the weather today in Florida?", token);
```

## Models

The brains of the Agent is the model. API Models, Desktop models and local Models are available.

## Harnesses

Desktop Models have their own harness and will use them. For API Models, Local Models or any non-hosted models the GenericHarness (or a custom harness) can be used.

## MCPs

MCPs are toolboxes for Agents to use in their harness.

## Permissions

When using the Agent you'll need to pass in a PlugInToken. The Token should be registered from the PlugInLoad method.

``` cs
PlugInToken token = PlugInRegistry.Register();
```
