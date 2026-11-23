using System;
using System.Collections.Generic;
using NyangTalk.Domain.Conversation;
using NyangTalk.Domain.GameLink;

namespace NyangTalk.Application
{
    /// <summary>In-process domain event bus (01-OVERVIEW 2.1). Contexts talk through events, not direct references.</summary>
    public sealed class EventBus
    {
        private readonly Dictionary<Type, List<Delegate>> _handlers = new Dictionary<Type, List<Delegate>>();

        public void Subscribe<T>(Action<T> handler)
        {
            List<Delegate> l;
            if (!_handlers.TryGetValue(typeof(T), out l)) _handlers[typeof(T)] = l = new List<Delegate>();
            l.Add(handler);
        }

        public void Publish<T>(T evt)
        {
            List<Delegate> l;
            if (!_handlers.TryGetValue(typeof(T), out l)) return;
            foreach (var d in l.ToArray()) ((Action<T>)d)(evt);
        }
    }

    public sealed class MessageReceived { public ChatMessage Message; public bool IsInitialLoad; }
    public sealed class ParticipantsChanged { }
    public sealed class LobbyChanged { public string LobbyId; }
    public sealed class LinkStateChanged { public LinkState State; }
    public sealed class OutboxChanged { }
    public sealed class GameBuildChanged { public string FingerprintId; }
    public sealed class NoticeRaised { public string Key; public string Text; public string ActionKey; }
}
