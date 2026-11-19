using System.Linq;
using System.Collections.Generic;

namespace Rhino.AI;

internal static class TokenEstimate
{

    public const int CharsPerToken = 4;

    // Flat placeholder: base64 size overstates image cost wildly, and the per-provider formula is unconfirmed.
    public const int TokensPerImage = 1_600;

    public static int FromText(string text) => (text.Length + CharsPerToken - 1) / CharsPerToken;

    public static int FromTurn(ITurn turn) => turn switch
    {
        MessageTurn message => FromText(message.Data) + FromImages(message.Content),
        ToolResultTurn result => FromText(result.Data) + FromImages(result.Return.Content),
        _ => FromText(turn.Data),
    };

    public static int FromTurnsPreferringReported(IEnumerable<ITurn> turns) => turns.Sum(t => t.TokenCount ?? FromTurn(t));

    private static int FromImages(IReadOnlyList<IMessageContent> content)
        => content.Count(c => c is ImageContent) * TokensPerImage;

}
