using System.Collections.Generic;
using Rhino.PlugIns;

namespace RhMcp;

// Persists conversation transcripts under the AISettings Conversations child node, one JSON
// string per session keyed by its session id. Rewritten on every CompleteTurn so a crash never
// loses more than the in-flight turn. PersistentSettings has no transactional bulk write, so a
// flat key-per-conversation layout keeps each Save a single SetString.
internal static class ConversationStore
{
    // Cap to bound the user's chosen PersistentSettings backing store (PLAN risk: bloat).
    private const int MaxConversations = 50;

    private static PersistentSettings Node => AISettings.Conversations;

    // Serializes the read-modify-write on the shared Conversations node. Save runs off-thread
    // from multiple agents (CliAgent, AgentRunner), one per doc; without this, one Save's SetString
    // or DeleteItem can interleave with another's Keys enumeration. Monitor is reentrant, so the
    // Prune -> List nesting inside a held Save is fine.
    private static readonly object Gate = new();

    // Snapshot the live graph on the caller's thread (where the conversation is owned), then marshal
    // the PersistentSettings write onto the UI thread: every touch of the settings tree (this node
    // and its siblings read elsewhere on the UI thread) must happen on one thread, since
    // PersistentSettings is not thread-safe. PersistTurn is already fire-and-forget, so the marshal
    // is invisible to the turn; the Gate still orders concurrent Saves with List/TryLoad.
    public static void Save(Conversation conversation)
    {
        ConversationDto dto = Snapshot(conversation);
        RhinoApp.InvokeOnUiThread(new Action(() =>
        {
            lock (Gate)
            {
                Node.SetString(dto.SessionId, JsonSerializer.Serialize(dto, McpSerializer.Options));
                Prune();
            }
        }), null);
    }

    // Recents first (newest StartedAt). Corrupt slots are skipped, never thrown.
    public static IReadOnlyList<ConversationDto> List()
    {
        List<ConversationDto> all = [];
        lock (Gate)
        {
            foreach (string key in KeysSnapshot())
            {
                if (TryRead(key, out ConversationDto dto))
                    all.Add(dto);
            }
        }
        all.Sort(static (a, b) => b.StartedAt.CompareTo(a.StartedAt));
        return all;
    }

    public static bool TryLoad(string sessionId, out ConversationDto conversation)
    {
        lock (Gate)
            return TryRead(sessionId, out conversation);
    }

    private static bool TryRead(string key, out ConversationDto conversation)
    {
        conversation = default!;
        if (!Node.TryGetString(key, out string json) || string.IsNullOrWhiteSpace(json))
            return false;
        try
        {
            if (JsonSerializer.Deserialize<ConversationDto>(json, McpSerializer.Options) is ConversationDto dto)
            {
                conversation = dto;
                return true;
            }
        }
        catch (JsonException)
        {
        }
        return false;
    }

    private static void Prune()
    {
        IReadOnlyList<ConversationDto> recents = List();
        for (int i = MaxConversations; i < recents.Count; i++)
            Node.DeleteItem(recents[i].SessionId);
    }

    // ChildKeys mutates under DeleteItem; copy first so enumeration never trips on a live edit.
    private static IReadOnlyList<string> KeysSnapshot()
    {
        List<string> keys = [];
        foreach (string key in Node.Keys)
            keys.Add(key);
        return keys;
    }

    private static ConversationDto Snapshot(Conversation conversation)
    {
        List<TurnEventDto> lifecycle = [];
        foreach (TurnEvent ev in conversation.Lifecycle)
            lifecycle.Add(ToDto(ev));

        List<TurnDto> turns = [];
        foreach (Turn turn in conversation.Turns)
        {
            List<TurnEventDto> events = [];
            foreach (TurnEvent ev in turn.Events)
                events.Add(ToDto(ev));
            turns.Add(new TurnDto(turn.Prompt, turn.StartedAt, turn.CompletedAt, events, turn.Usage));
        }

        return new ConversationDto(
            conversation.AgentSessionId.ToString(),
            conversation.AgentName,
            conversation.DocTitle,
            conversation.StartedAt,
            lifecycle,
            turns);
    }

    private static TurnEventDto ToDto(TurnEvent ev) =>
        new(ev.Kind, ev.Text, ev.At, ev.Args, ev.Result, ev.Id);
}
