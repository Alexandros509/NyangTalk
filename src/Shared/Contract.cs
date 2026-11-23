using System;
using System.Collections.Generic;
using System.Linq;

namespace NyangTalk.Shared
{
    /// <summary>One game member a feature area needs (10-SPEC 4장). Candidates are tried in order.</summary>
    public sealed class ContractEntry
    {
        public string Id;
        public string Area;
        public string Assembly;
        public List<string> Types = new List<string>();   // candidate full type names ('+' for nested)
        public string Kind;                               // method | field | property | event | type
        public List<string> Names = new List<string>();   // candidate member names
        public string Returns;                            // field/property type, method return type
        public List<string> Params = new List<string>();
        public bool Static;
        public bool Required = true;
        /// <summary>Member is declared on a base class (e.g. static Instance of a generic singleton base).</summary>
        public bool Inherited;
        public string Note;
    }

    public sealed class ContractFile
    {
        public int Version;
        public List<string> VerifiedFingerprints = new List<string>();
        public List<ContractEntry> Entries = new List<ContractEntry>();

        public ContractEntry Get(string id) { return Entries.FirstOrDefault(e => e.Id == id); }

        public static ContractFile Parse(string json)
        {
            var d = Json.Obj(Json.Parse(json));
            var c = new ContractFile { Version = (int)Json.Long(d, "contract_version", 0) };
            var vf = Json.List(d, "verified_fingerprints");
            if (vf != null) foreach (var f in vf) if (f is string) c.VerifiedFingerprints.Add((string)f);
            foreach (var o in Json.List(d, "entries") ?? new List<object>())
            {
                var e = Json.Obj(o);
                if (e == null) continue;
                var entry = new ContractEntry
                {
                    Id = Json.Str(e, "id"), Area = Json.Str(e, "area"), Assembly = Json.Str(e, "assembly"), Kind = Json.Str(e, "kind"),
                    Returns = Json.Str(e, "returns"), Static = Json.Bool(e, "static", false), Required = Json.Bool(e, "required", true), Inherited = Json.Bool(e, "inherited", false), Note = Json.Str(e, "note")
                };
                entry.Types = Strings(e, "type");
                entry.Names = Strings(e, "name");
                entry.Params = Strings(e, "params");
                c.Entries.Add(entry);
            }
            return c;
        }

        private static List<string> Strings(Dictionary<string, object> e, string key)
        {
            object v;
            if (!e.TryGetValue(key, out v) || v == null) return new List<string>();
            if (v is string) return new List<string> { (string)v };
            var l = v as List<object>;
            var r = new List<string>();
            if (l != null) foreach (var x in l) { var str = x as string; if (str != null) r.Add(str); } // no LINQ OfType: missing in the game's stripped System.Core
            return r;
        }

        public static readonly string[] Areas = { "ChatReceive", "ChatSend", "LobbyInfo", "Participants", "Appearance", "Chest" };
    }
}
