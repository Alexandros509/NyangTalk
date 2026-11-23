using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NyangTalk.Shared;

namespace NyangTalk.GameLink
{
    /// <summary>
    /// The only place the game component uses reflection. Every lookup goes through a contract entry ID, so the names
    /// come from contract.json and nowhere else (10-SPEC 4장, 11장 9번).
    /// </summary>
    public sealed class ContractRuntime
    {
        private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        private readonly ContractFile _contract;
        private readonly Dictionary<string, MemberInfo> _members = new Dictionary<string, MemberInfo>();
        private readonly Dictionary<string, Type> _types = new Dictionary<string, Type>();
        public readonly Dictionary<string, bool> Areas = new Dictionary<string, bool>();
        public readonly List<string> Missing = new List<string>();

        public ContractRuntime(ContractFile contract) { _contract = contract; }

        private static Assembly FindAssembly(string name)
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                if (string.Equals(a.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) return a;
            return null;
        }

        /// <summary>Resolves all entries; plugin-side verdict per area wins over the app's static verdict.</summary>
        public void ResolveAll()
        {
            Missing.Clear();
            foreach (var e in _contract.Entries)
            {
                if (!Resolve(e)) Missing.Add(e.Id);
            }
            foreach (var area in ContractFile.Areas)
                Areas[area] = _contract.Entries.Where(e => e.Area == area && e.Required).All(e => _members.ContainsKey(e.Id) || _types.ContainsKey(e.Id));
        }

        private bool Resolve(ContractEntry e)
        {
            var asm = FindAssembly(e.Assembly);
            if (asm == null) return false;
            foreach (var tn in e.Types)
            {
                Type t;
                try { t = asm.GetType(tn); } catch (Exception) { t = null; }
                if (t == null) continue;
                if (e.Kind == "type") { _types[e.Id] = t; return true; }
                var flags = e.Inherited ? All | BindingFlags.FlattenHierarchy : All;
                foreach (var name in e.Names)
                {
                    MemberInfo m = null;
                    try
                    {
                        switch (e.Kind)
                        {
                            case "field": m = t.GetField(name, flags); break;
                            case "property": m = t.GetProperty(name, flags); break;
                            case "event": m = t.GetEvent(name, flags); break;
                            default:
                                m = t.GetMethods(flags).FirstOrDefault(x => x.Name == name && x.GetParameters().Length == e.Params.Count);
                                break;
                        }
                    }
                    catch (AmbiguousMatchException) { m = null; }
                    if (m != null) { _members[e.Id] = m; _types[e.Id] = t; return true; }
                }
            }
            return false;
        }

        public bool Has(string id) { return _members.ContainsKey(id) || _types.ContainsKey(id); }
        public Type TypeOf(string id) { Type t; return _types.TryGetValue(id, out t) ? t : null; }

        /// <summary>Reads a field or property (static when target is null). Returns null when unavailable.</summary>
        public object Get(string id, object target)
        {
            MemberInfo m;
            if (!_members.TryGetValue(id, out m)) return null;
            var f = m as FieldInfo;
            if (f != null)
            {
                // generic definitions (Bus`1) are resolved again on the closed runtime type of the target
                if (target != null && f.DeclaringType.IsGenericTypeDefinition) f = target.GetType().GetField(f.Name, All);
                return f == null ? null : f.GetValue(f.IsStatic ? null : target);
            }
            var p = m as PropertyInfo;
            if (p != null) return p.GetValue(p.GetGetMethod(true).IsStatic ? null : target, null);
            return null;
        }

        public object Call(string id, object target, params object[] args)
        {
            MemberInfo m;
            if (!_members.TryGetValue(id, out m)) throw new MissingMemberException(id);
            var mi = (MethodInfo)m;
            return mi.Invoke(mi.IsStatic ? null : target, args);
        }

        /// <summary>Calls a method and keeps the argument array, so out parameters can be read back.</summary>
        public object CallWithArgs(string id, object target, object[] args)
        {
            MemberInfo m;
            if (!_members.TryGetValue(id, out m)) throw new MissingMemberException(id);
            var mi = (MethodInfo)m;
            return mi.Invoke(mi.IsStatic ? null : target, args);
        }

        /// <summary>Builds Action&lt;TId, TValue&gt; for the game's id type out of a generic handler on <paramref name="owner"/>.</summary>
        public static Delegate MakeAction(object owner, string handlerName, Type idType, Type valueType)
        {
            var actionType = typeof(Action<,>).MakeGenericType(idType, valueType);
            var mi = owner.GetType().GetMethod(handlerName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (mi.IsGenericMethodDefinition) mi = mi.MakeGenericMethod(idType);
            return Delegate.CreateDelegate(actionType, owner, mi);
        }
    }
}
