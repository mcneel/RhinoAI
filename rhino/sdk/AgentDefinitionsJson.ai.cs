using System;
using System.Collections.Generic;
using System.Text.Json;

using Rhino.AI.Models;

namespace Rhino.AI;

internal static class AgentDefinitionsJson
{

    private static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    // Invalid entries are skipped rather than failing the document, so one bad custom definition cannot hide the rest.
    public static IReadOnlyList<AgentDefinition>? Parse(string json)
    {
        DefinitionsDto? root;
        try
        {
            root = JsonSerializer.Deserialize<DefinitionsDto>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }

        if (root?.Definitions is null) return null;

        List<AgentDefinition> definitions = new(root.Definitions.Count);
        foreach (DefinitionDto dto in root.Definitions)
        {
            if (ToDefinition(dto) is not AgentDefinition definition) continue;
            definitions.Add(definition);
        }

        return definitions;
    }

    private static AgentDefinition? ToDefinition(DefinitionDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Name)) return null;
        if (string.IsNullOrWhiteSpace(dto.Company)) return null;
        if (ToPlatforms(dto.Platforms) is not Platform platforms) return null;

        AgentDefinition? definition = dto.Kind?.ToLowerInvariant() switch
        {
            "cli" => new CliAgentDefinition(dto.Company, dto.Name)
            {
                Id = dto.Id ?? Guid.NewGuid(),
                Platforms = platforms,
                WinSearchPaths = dto.SearchPaths?.Win ?? [],
                MacSearchPaths = dto.SearchPaths?.Mac ?? [],
            },
            "api" or "local" => ToHttp(dto, dto.Company, dto.Name, platforms),
            _ => null,
        };

        if (definition is null) return null;

        definition.DefaultPrompt = dto.DefaultPrompt ?? "";
        definition.SvgIcon = dto.SvgIcon ?? "";
        string? firstModelId = null;
        foreach (ModelDto model in dto.Models ?? [])
        {
            if (string.IsNullOrWhiteSpace(model.Id)) continue;
            definition.Add(new ModelDefinition(dto.Company, model.Display ?? model.Id, model.Id));
            firstModelId ??= model.Id;
        }

        definition.DefaultModelId = dto.DefaultModel ?? firstModelId ?? "";

        return definition;
    }

    private static HttpAgentDefinition? ToHttp(DefinitionDto dto, string vendor, string name, Platform platforms)
    {
        if (!Uri.TryCreate(dto.Host, UriKind.Absolute, out _)) return null;
        if (ToProtocol(dto.Protocol) is not ProtocolKind protocol) return null;

        return new HttpAgentDefinition(vendor, name)
        {
            Id = dto.Id ?? Guid.NewGuid(),
            Platforms = platforms,
            Host = dto.Host,
            Protocol = protocol,
            ModelsRoute = dto.ModelsRoute,
            ApiKeyEnvVar = dto.EnvVar,
        };
    }

    private static ProtocolKind? ToProtocol(string? protocol)
        => protocol?.ToLowerInvariant() switch
        {
            null => ProtocolKind.Default,
            "messages" => ProtocolKind.Messages,
            "chatcompletions" => ProtocolKind.Completions,
            "responses" => ProtocolKind.Responses,
            "generatecontent" => ProtocolKind.Default,
            _ => null,
        };

    private static Platform? ToPlatforms(List<string>? platforms)
    {
        if (platforms is null) return Platform.All;

        Platform result = Platform.None;
        foreach (string platform in platforms)
        {
            Platform? parsed = platform.ToLowerInvariant() switch
            {
                "win" => Platform.Windows,
                "mac" => Platform.OSX,
                _ => null,
            };

            if (parsed is not Platform flag) return null;
            result |= flag;
        }

        return result;
    }

    private sealed record DefinitionsDto(List<DefinitionDto>? Definitions);

    private sealed record DefinitionDto(
        Guid? Id,
        string? Kind,
        string? Name,
        string? Company,
        string? DefaultPrompt,
        string? DefaultModel,
        string? SvgIcon,
        List<string>? Platforms,
        SearchPathsDto? SearchPaths,
        string? Host,
        string? Protocol,
        string? EnvVar,
        string? ModelsRoute,
        List<ModelDto>? Models);

    private sealed record SearchPathsDto(List<string>? Win, List<string>? Mac);

    private sealed record ModelDto(string? Display, string? Id);

}
