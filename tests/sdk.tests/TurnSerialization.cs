
using System.Text.Json;
using Rhino.AI;

namespace sdk.tests;

public class TurnSerialization
{
    
    [Test]
    public void Roundtrip()
    {
        List<ITurn> turns = new()
        {
            new TurnEnd(StopReason.MaxTokens, "oh no an error", DateTime.UtcNow, TimeSpan.FromMilliseconds(36), 120),
            new MessageTurn("It's a message!", RoleType.User, DateTime.UtcNow, TimeSpan.FromHours(0.2), 91),
            // new MessageTurn("It's a message!", RoleType.User, DateTime.UtcNow, TimeSpan.FromHours(0.2), 91),
            new ThinkingTurn("Hmmm", new ThoughtSignature("Me", "EyeDee", "It's a value", false)),
            new ToolTurn("2", "MyTool", new List<IToolArg>
            {
                new ToolBoolean("boolean", false),
                new ToolNumber("number", 2.0),
                new ToolInt("int", -8),
                new ToolPath("path", "C:/Users/Path"),
                new ToolUrl("url", "http://localhost:1234"),
                new ToolString("string", "Stringy"),
                new ToolSecret("secret", "I snap my spaghetti in half to cook it"),
            }, new ThoughtSignature("Them", "Eye", "Value", true)),
            new TurnStart(DateTime.UtcNow, TimeSpan.FromTicks(2), 15),
        };

        string json = JsonSerializer.Serialize(turns);
        List<ITurn>? turnsOut = JsonSerializer.Deserialize<List<ITurn>>(json);

        Assert.That(turnsOut, Is.Not.Null);
        Assert.That(turns.Count, Is.EqualTo(turnsOut.Count));
        
        for(int i = 0; i < turns.Count; i++)
        {
            ITurn a = turns[i];
            ITurn b = turnsOut[i];
            ;
        }
    }

}
