using System;
using System.Collections.Generic;
using System.Linq;
using NyangTalk.Domain.Conversation;

namespace NyangTalk.Domain.Lobby
{
    public sealed class Participant
    {
        public string SteamId;
        public string Name;
        public bool IsOwner;
        public bool IsMe;
    }

    public sealed class LobbyInfo
    {
        public string LobbyId;
        public string Name;
        public string Code;          // as read by the game component
        public bool CodeHidden;      // HIDE_LOBBY_CODE
        public int MemberCount;

        /// <summary>Show the code only if the game has one and the owner did not hide it (40-SPEC 2장).</summary>
        public string DisplayCode { get { return CodeHidden || string.IsNullOrEmpty(Code) ? null : Code; } }

        /// <summary>Unknown name: "이름 미확인 · 방 ID 끝 6자리" (90-SPEC 3장). Returns the suffix; the caller localises.</summary>
        public static string IdSuffix(string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId)) return "??????";
            return lobbyId.Length <= 6 ? lobbyId : lobbyId.Substring(lobbyId.Length - 6);
        }
    }

    /// <summary>
    /// Who to list in the participant panel (60-SPEC 1장). When the game could not provide the list, only people who
    /// actually sent messages are shown and the caller shows "참여자 정보를 읽지 못했어요"; nothing is guessed.
    /// </summary>
    public static class ParticipantList
    {
        public static List<Participant> Resolve(IList<Participant> fromGame, bool gameListReadable, IEnumerable<ChatMessage> seen)
        {
            if (gameListReadable && fromGame != null) return fromGame.ToList();
            var r = new List<Participant>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in seen)
            {
                if (m.SenderId == null || !ids.Add(m.SenderId)) continue;
                r.Add(new Participant { SteamId = m.SenderId, Name = m.SenderName, IsMe = m.IsMine });
            }
            return r;
        }

        /// <summary>Header face stack: at most 3 faces plus the member count.</summary>
        public static List<Participant> HeaderFaces(IList<Participant> all) { return all.Take(3).ToList(); }
    }

    /// <summary>
    /// User-chosen grouping of saved rooms whose lobby ID changed across sessions (95-SPEC 2.7).
    /// Never merges rooms automatically by name; only rooms explicitly grouped by the user are linked.
    /// </summary>
    public sealed class RoomGroup
    {
        public string Name;
        public readonly List<string> LobbyIds = new List<string>();
    }

    public sealed class RoomGroupBook
    {
        private readonly List<RoomGroup> _groups = new List<RoomGroup>();
        public IList<RoomGroup> All { get { return _groups; } }

        public void Load(IEnumerable<RoomGroup> groups)
        {
            _groups.Clear();
            if (groups == null) return;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in groups)
            {
                if (g == null) continue;
                var ids = g.LobbyIds.Where(x => !string.IsNullOrEmpty(x) && seen.Add(x)).ToList();
                if (ids.Count == 0) continue;
                var copy = new RoomGroup { Name = g.Name };
                copy.LobbyIds.AddRange(ids);
                _groups.Add(copy);
            }
        }

        public RoomGroup GroupOf(string lobbyId)
        {
            if (string.IsNullOrEmpty(lobbyId)) return null;
            return _groups.FirstOrDefault(g => g.LobbyIds.Contains(lobbyId, StringComparer.Ordinal));
        }

        public List<string> MembersOf(string lobbyId)
        {
            var g = GroupOf(lobbyId);
            return g != null ? g.LobbyIds.ToList() : (string.IsNullOrEmpty(lobbyId) ? new List<string>() : new List<string> { lobbyId });
        }

        /// <summary>Links two or more rooms into one group (merging any existing groups they belonged to).</summary>
        public RoomGroup Group(IEnumerable<string> lobbyIds, string name = null)
        {
            if (lobbyIds == null) return null;
            var picked = lobbyIds.Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal).ToList();
            if (picked.Count < 2) return null;
            var merged = new List<string>();
            string existingName = null;
            foreach (var id in picked)
            {
                var g = GroupOf(id);
                if (g != null)
                {
                    if (existingName == null && !string.IsNullOrEmpty(g.Name)) existingName = g.Name;
                    foreach (var m in g.LobbyIds) if (!merged.Contains(m, StringComparer.Ordinal)) merged.Add(m);
                    _groups.Remove(g);
                }
                else if (!merged.Contains(id, StringComparer.Ordinal)) merged.Add(id);
            }
            var created = new RoomGroup { Name = !string.IsNullOrEmpty(name) ? name : existingName };
            created.LobbyIds.AddRange(merged);
            _groups.Add(created);
            return created;
        }

        /// <summary>Dissolves the group containing <paramref name="lobbyId"/> so its rooms are shown separately again.</summary>
        public bool Ungroup(string lobbyId)
        {
            var g = GroupOf(lobbyId);
            return g != null && _groups.Remove(g);
        }
    }
}
