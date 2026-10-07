namespace Rhino.AI.UI
{
    /// <summary>
    /// The publisher in the Observable pattern
    /// </summary>
    internal static class UIMessagePublisher
    {
        private static Dictionary<Type, Delegate> Handlers { get; } = [];

        /// <summary>
        /// Subscribe to a type of message. Multiple handlers can be subsribed to a message type.
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="handler"></param>
        public static void Subscribe<TMessage>(Action<TMessage> handler) where TMessage : UIMessage
        {
            if (null == handler) throw new ArgumentNullException(nameof(handler));
            Handlers.TryGetValue(typeof(TMessage), out Delegate? existing);
            Handlers[typeof(TMessage)] = Delegate.Combine(existing, handler)!;
        }

        /// <summary>
        /// Unsubscribe a handler from a type of message. When multiple handlers are subcribed, only the handler supplied with be unsubscribed.
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="handler"></param>
        /// <exception cref="ArgumentNullException"></exception>
        public static void Unsubscribe<TMessage>(Action<TMessage> handler) where TMessage : UIMessage
        {
            if (null == handler) throw new ArgumentNullException(nameof(handler));

            if (!Handlers.TryGetValue(typeof(TMessage), out Delegate? existing))
                return;

            if (Delegate.Remove(existing, handler) is { } remaining)
                Handlers[typeof(TMessage)] = remaining;
            else
                Handlers.Remove(typeof(TMessage));
        }

        /// <summary>
        /// Notify all subscribed handlers.
        /// Dispatches on the static type, so publish the concrete message rather than a UIMessage-typed variable.
        /// </summary>
        /// <typeparam name="TMessage"></typeparam>
        /// <param name="message"></param>
        public static void Notify<TMessage>(TMessage message) where TMessage : UIMessage
        {
            if (Handlers.TryGetValue(typeof(TMessage), out Delegate? handlers))
                ((Action<TMessage>)handlers)(message);
        }
    }

    /// <summary>
    /// Abstract base class for messages/notifications between parts that hold no reference to each other
    /// </summary>
    internal abstract class UIMessage
    {

    }

    /// <summary>
    /// The <see cref="SettingsCommittedMessage"/> is sent from the <see cref="AISettingsPanel.TryCommit"/> when the settings are committed.
    /// The <see cref="AIPanelViewModel"/> is the intended receiver so that it can update the AI panel with new settings.
    /// </summary>
    internal sealed class SettingsCommittedMessage : UIMessage
    {

    }
}
