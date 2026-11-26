#if R9
using Eto.Drawing;

using Rhino.UI;

using Rhino.AI.UI.Settings;

namespace Rhino.AI.UI.Settings;

internal sealed class PlugInItemViewModel : SimpleViewModel, IRowItem
{
    private bool isTrusted;

    public PlugInItemViewModel(Guid id, string name, bool isTrusted, bool isLoaded, int toolCount, IReadOnlyList<SkillInfo> skills, Image? icon)
    {
        Id = id;
        Name = name;
        SavedTrust = isTrusted;
        this.isTrusted = isTrusted;
        IsLoaded = isLoaded;
        ToolCount = toolCount;
        Skills = skills;
        Icon = icon;
    }

    public Guid Id { get; }

    public string Name { get; }

    private bool SavedTrust { get; }

    private bool IsLoaded { get; }

    private int ToolCount { get; }

    private IReadOnlyList<SkillInfo> Skills { get; }

    private Image? Icon { get; }

    public bool IsTrusted
    {
        get => isTrusted;
        set => SetProperty(ref isTrusted, value);
    }

    public bool IsChanged => IsTrusted != SavedTrust;

    public PlugInPermissionEntry Entry => new(Id, Name, IsTrusted);

    public RowText Text => new(Name, Detail, false, null, Icon is null ? new RowIcon.Initial(Initial) : new RowIcon.Picture(Icon), false);

    public string? ToolTip => Skills.Count == 0
        ? null
        : string.Format(LOC.STR("Skills: {0}"), string.Join(", ", Skills.Select(skill => skill.Name)));

    private string Detail
    {
        get
        {
            if (!IsLoaded)
                return LOC.STR("Not loaded in this session");

            string tools = Phrase.Count(ToolCount, LOC.STR("1 tool"), LOC.STR("{0} tools"));
            string skills = Phrase.Count(Skills.Count, LOC.STR("1 skill"), LOC.STR("{0} skills"));
            return (ToolCount, Skills.Count) switch
            {
                (0, 0) => LOC.STR("No tools or skills"),
                (_, 0) => tools,
                (0, _) => skills,
                _ => tools + ", " + skills,
            };
        }
    }

    private string Initial => Name.Where(char.IsLetterOrDigit).Select(letter => (char?)letter).FirstOrDefault() is char first
        ? char.ToUpperInvariant(first).ToString()
        : "?";
}
#endif
