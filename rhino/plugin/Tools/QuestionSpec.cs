using System.ComponentModel;

namespace RhinoAI.Tools;

// One question as the agent asks for it. The ask_user input schema is generated from this record's
// constructor, so these descriptions are what the agent actually reads.
public sealed record QuestionSpec(
    [Description("The question to show the user")] string Question,
    [Description("The options to choose from")] string[] Options,
    [Description("true = the user may pick several of these options (checkboxes); false = one choice (radio). Default false.")] bool MultiSelect = false);
