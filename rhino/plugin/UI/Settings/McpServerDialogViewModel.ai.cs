using System.Text.Json.Nodes;

using Rhino.UI;

namespace Rhino.AI.UI.Settings;

internal sealed class McpServerDialogViewModel : SimpleViewModel
{
    public const int FormTab = 0;
    public const int JsonTab = 1;

    private string name = string.Empty;
    private McpServerType type = McpServerType.Command;
    private string command = string.Empty;
    private string arguments = string.Empty;
    private string environment = string.Empty;
    private string url = string.Empty;
    private string headers = string.Empty;
    private JsonObject extra = [];
    private string json = string.Empty;
    private int selectedTab = FormTab;
    private string? formNote;
    private string? error;

    public McpServerDialogViewModel(IReadOnlyCollection<string> takenNames, IMcpDefinition? editing)
    {
        TakenNames = takenNames;
        IsEditing = editing is not null;
        IsServerEnabled = editing?.Enabled ?? true;
        if (editing is null)
            return;

        NamedServer server = McpServerEntry.ToServer(editing);
        json = McpServerEntry.Format([server]);
        if (McpServerEntry.ToFields(server) is McpServerFields fields)
        {
            Fill(fields);
        }
        else
        {
            formNote = LOC.STR("This server has settings the form cannot show. Edit it in JSON.");
            selectedTab = JsonTab;
        }
    }

    private IReadOnlyCollection<string> TakenNames { get; }

    private bool IsServerEnabled { get; }

    public bool IsEditing { get; }

    public string Title => IsEditing ? LOC.STR("Edit MCP Server") : LOC.STR("Add MCP Server");

    public string ConfirmText => IsEditing ? LOC.STR("Save") : LOC.STR("Add");

    public string Name
    {
        get => name;
        set => Edit(ref name, value);
    }

    public int TypeIndex
    {
        get => (int)type;
        set
        {
            if (value < 0)
                return;

            McpServerType next = value switch
            {
                0 => McpServerType.Command,
                1 => McpServerType.Url,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
            };
            if (next == type)
                return;

            type = next;
            SetError(null);
            RaisePropertyChanged(nameof(TypeIndex));
            RaisePropertyChanged(nameof(IsCommand));
            RaisePropertyChanged(nameof(IsUrl));
        }
    }

    public bool IsCommand => type == McpServerType.Command;

    public bool IsUrl => type == McpServerType.Url;

    public string Command
    {
        get => command;
        set => Edit(ref command, value);
    }

    public string Arguments
    {
        get => arguments;
        set => Edit(ref arguments, value);
    }

    public string Environment
    {
        get => environment;
        set => Edit(ref environment, value);
    }

    public string Url
    {
        get => url;
        set => Edit(ref url, value);
    }

    public string Headers
    {
        get => headers;
        set => Edit(ref headers, value);
    }

    public string Json
    {
        get => json;
        set => Edit(ref json, value);
    }

    public bool IsFormEnabled => formNote is null;

    public bool HasFormNote => formNote is not null;

    public string FormNote => formNote ?? string.Empty;

    public bool HasExtraNote => extra.Count > 0;

    public string ExtraNote => string.Format(LOC.STR("These settings are not supported: {0}. Remove them on the JSON tab to save."), string.Join(", ", extra.Select(pair => pair.Key)));

    public bool HasError => error is not null;

    public string ErrorText => error ?? string.Empty;

    public int SelectedTab
    {
        get => selectedTab;
        set
        {
            if (selectedTab == value)
                return;

            int from = selectedTab;
            selectedTab = value;
            SetError(null);
            if (from == FormTab && value == JsonTab)
                FormToJson();
            else if (from == JsonTab && value == FormTab)
                JsonToForm();
            RaisePropertyChanged(nameof(SelectedTab));
        }
    }

    private McpServerFields Fields => new(name, type, command, arguments, environment, url, headers, extra);

    public IReadOnlyList<IMcpDefinition>? Confirm()
    {
        ServerResult<IReadOnlyList<NamedServer>> read = selectedTab == FormTab && IsFormEnabled ? FromForm() : McpServerEntry.Read(json);
        ServerResult<IReadOnlyList<IMcpDefinition>> result = read switch
        {
            ServerResult<IReadOnlyList<NamedServer>>.Ok ok => McpServerEntry.ToDefinitions(ok.Value, TakenNames, IsServerEnabled),
            ServerResult<IReadOnlyList<NamedServer>>.Failed failed => new ServerResult<IReadOnlyList<IMcpDefinition>>.Failed(failed.Problem),
            _ => throw new InvalidOperationException("Unknown server result."),
        };

        switch (result)
        {
            case ServerResult<IReadOnlyList<IMcpDefinition>>.Failed failed:
                SetError(Describe(failed.Problem));
                return null;

            case ServerResult<IReadOnlyList<IMcpDefinition>>.Ok ok:
                return ok.Value;

            default:
                throw new InvalidOperationException("Unknown server result.");
        }
    }

    private ServerResult<IReadOnlyList<NamedServer>> FromForm()
    {
        if (name.Trim().Length == 0)
            return new ServerResult<IReadOnlyList<NamedServer>>.Failed(new ServerProblem.NameMissing());

        return McpServerEntry.FromFields(Fields) switch
        {
            ServerResult<NamedServer>.Ok ok => new ServerResult<IReadOnlyList<NamedServer>>.Ok([ok.Value]),
            ServerResult<NamedServer>.Failed failed => new ServerResult<IReadOnlyList<NamedServer>>.Failed(failed.Problem),
            _ => throw new InvalidOperationException("Unknown server result."),
        };
    }

    private void FormToJson()
    {
        if (!IsFormEnabled || Fields.IsBlank)
            return;

        switch (McpServerEntry.FromFields(Fields))
        {
            case ServerResult<NamedServer>.Ok ok:
                SetJson(McpServerEntry.Format([ok.Value]));
                break;

            case ServerResult<NamedServer>.Failed failed:
                SetError(Describe(failed.Problem));
                break;

            default:
                throw new InvalidOperationException("Unknown server result.");
        }
    }

    private void JsonToForm()
    {
        if (json.Trim().Length == 0)
        {
            SetFormNote(null);
            return;
        }

        switch (McpServerEntry.Read(json))
        {
            case ServerResult<IReadOnlyList<NamedServer>>.Failed failed:
                SetFormNote(string.Format(LOC.STR("The form cannot show this JSON. {0}"), Describe(failed.Problem)));
                break;

            case ServerResult<IReadOnlyList<NamedServer>>.Ok { Value.Count: > 1 } many:
                SetFormNote(string.Format(LOC.STR("The JSON has {0} servers, and the form shows one at a time. Add them from the JSON tab."), many.Value.Count));
                break;

            case ServerResult<IReadOnlyList<NamedServer>>.Ok ok when McpServerEntry.ToFields(ok.Value[0]) is McpServerFields fields:
                Fill(fields);
                SetFormNote(null);
                break;

            case ServerResult<IReadOnlyList<NamedServer>>.Ok:
                SetFormNote(LOC.STR("This server has settings the form cannot show. Edit it in JSON."));
                break;

            default:
                throw new InvalidOperationException("Unknown server result.");
        }
    }

    private void Fill(McpServerFields fields)
    {
        name = fields.Name;
        type = fields.Type;
        command = fields.Command;
        arguments = fields.Arguments;
        environment = fields.Environment;
        url = fields.Url;
        headers = fields.Headers;
        extra = fields.Extra;
        foreach (string property in new[] { nameof(Name), nameof(TypeIndex), nameof(IsCommand), nameof(IsUrl), nameof(Command), nameof(Arguments), nameof(Environment), nameof(Url), nameof(Headers), nameof(HasExtraNote), nameof(ExtraNote) })
            RaisePropertyChanged(property);
    }

    private void SetError(string? value)
    {
        if (error == value)
            return;

        error = value;
        RaisePropertyChanged(nameof(HasError));
        RaisePropertyChanged(nameof(ErrorText));
    }

    private void SetJson(string value)
    {
        json = value;
        RaisePropertyChanged(nameof(Json));
    }

    private void SetFormNote(string? value)
    {
        formNote = value;
        RaisePropertyChanged(nameof(IsFormEnabled));
        RaisePropertyChanged(nameof(HasFormNote));
        RaisePropertyChanged(nameof(FormNote));
    }

    private void Edit(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string property = "")
    {
        if (SetProperty(ref field, value, property))
            SetError(null);
    }

    private static string Describe(ServerProblem problem) => problem switch
    {
        ServerProblem.NameMissing => LOC.STR("Enter a name for the server."),
        ServerProblem.NameTaken taken => string.Format(LOC.STR("There is already a server named \"{0}\"."), taken.Name),
        ServerProblem.NameInvalid invalid => string.Format(LOC.STR("\"{0}\" can only use letters, digits, - and _, and cannot contain __."), invalid.Name),
        ServerProblem.CommandMissing { Name.Length: 0 } => LOC.STR("Enter the command that starts the server."),
        ServerProblem.CommandMissing missing => string.Format(LOC.STR("\"{0}\" needs a command."), missing.Name),
        ServerProblem.UrlInvalid { Name.Length: 0 } => LOC.STR("Enter a URL that starts with http or https."),
        ServerProblem.UrlInvalid invalid => string.Format(LOC.STR("\"{0}\" needs a URL that starts with http or https."), invalid.Name),
        ServerProblem.ValueInvalid invalid => string.Format(LOC.STR("\"{0}\" has a \"{1}\" value that cannot be read."), invalid.Name, invalid.Key),
        ServerProblem.TypeUnsupported unsupported => string.Format(LOC.STR("\"{0}\" has the type \"{1}\". Only \"stdio\" and \"http\" servers are supported."), unsupported.Name, unsupported.Type),
        ServerProblem.SettingsUnused unused => string.Format(LOC.STR("\"{0}\" has settings that are not supported: {1}. Remove them to save."), unused.Name, string.Join(", ", unused.Keys)),
        ServerProblem.BadLine { Field: McpServerField.Environment } line => string.Format(LOC.STR("Environment line {0} needs the form KEY=value."), line.Line),
        ServerProblem.BadLine line => string.Format(LOC.STR("Headers line {0} needs the form Name: value."), line.Line),
        ServerProblem.JsonInvalid invalid => string.Format(LOC.STR("The JSON is not valid: {0}"), invalid.Message),
        ServerProblem.JsonNotObject => LOC.STR("The JSON needs to be an object."),
        ServerProblem.NoServers => LOC.STR("The JSON has no servers."),
        ServerProblem.Unnamed => LOC.STR("Give the server a name by putting its settings inside a named key."),
        ServerProblem.NotAnObject notObject => string.Format(LOC.STR("\"{0}\" needs to be a JSON object."), notObject.Name),
        _ => throw new InvalidOperationException("Unknown server problem."),
    };
}
