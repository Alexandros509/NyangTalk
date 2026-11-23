using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NyangTalk.Adapters.Metadata
{
    /// <summary>A type reference with the assembly it is resolved from ("" = same assembly).</summary>
    public sealed class TypeName
    {
        public string Assembly;     // assembly name of the scope, or the defining assembly
        public string FullName;     // e.g. "System.String", "Outer+Inner", "System.Collections.Generic.List`1<System.Int32>"
        public string Definition;   // FullName without generic arguments
        public bool IsArray;        // array or szarray (runtime-provided members)
        public override string ToString() { return FullName; }
    }

    public sealed class MethodInfoM
    {
        public string Name;
        public string Return;
        public List<string> Params = new List<string>();
        public bool IsStatic, IsAbstract, IsVirtual, IsNewSlot;
        public int GenericArity;
        public string Signature { get { return Name + "(" + string.Join(",", Params) + ")"; } }
    }

    public sealed class FieldInfoM { public string Name; public string Type; public bool IsStatic; }
    public sealed class PropertyInfoM { public string Name; public string Type; public bool IsStatic; public List<string> Params = new List<string>(); public MethodInfoM Getter, Setter; }
    public sealed class EventInfoM { public string Name; public string Type; public bool IsStatic; }

    public sealed class TypeInfoM
    {
        public string Assembly;
        public string FullName;
        public uint Flags;
        public TypeName BaseType;
        public readonly List<TypeName> Interfaces = new List<TypeName>();
        public readonly List<MethodInfoM> Methods = new List<MethodInfoM>();
        public readonly List<FieldInfoM> Fields = new List<FieldInfoM>();
        public readonly List<PropertyInfoM> Properties = new List<PropertyInfoM>();
        public readonly List<EventInfoM> Events = new List<EventInfoM>();
        /// <summary>Explicit interface implementations: declared method name (interface full name + "." + name).</summary>
        public readonly List<string> ExplicitImpls = new List<string>();
        public bool IsInterface { get { return (Flags & 0x20) != 0; } }
        public bool IsAbstract { get { return (Flags & 0x80) != 0; } }
        public override string ToString() { return FullName; }
    }

    public sealed class MemberRefM
    {
        public TypeName Parent;
        public string Name;
        public bool IsField;
        public string Return;
        public List<string> Params = new List<string>();
        public int GenericArity;
    }

    public sealed class AssemblyM
    {
        public string Name;
        public Version Version;
        public string Path;
        public readonly Dictionary<string, TypeInfoM> Types = new Dictionary<string, TypeInfoM>(StringComparer.Ordinal);
        /// <summary>Type forwarders: full name → target assembly name.</summary>
        public readonly Dictionary<string, string> Forwarders = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> References = new List<string>();
        public readonly List<MemberRefM> MemberRefs = new List<MemberRefM>();
    }

    /// <summary>
    /// Minimal ECMA-335 (6th ed., Partition II §22–24) metadata reader: reads types, members, signatures, interface
    /// implementations, member references and type forwarders from a managed DLL. Read-only; never loads or runs code.
    /// </summary>
    public static class MetadataReader
    {
        public static long BytesRead;

        public static AssemblyM Read(string path)
        {
            var data = File.ReadAllBytes(path);
            BytesRead += data.Length;
            var r = new Reader(data);
            var a = r.Parse();
            a.Path = path;
            return a;
        }

        private sealed class Reader
        {
            private readonly byte[] _d;
            private int _strings, _blob, _blobSize, _tables;
            private int _strSize = 2, _guidSize = 2, _blobIdx = 2;
            private readonly int[] _rows = new int[64];
            private readonly int[] _tableOffset = new int[64];
            private readonly int[] _rowSize = new int[64];
            private string _asmName = "";
            private string[] _typeDefNames;
            private int[] _enclosing;

            public Reader(byte[] d) { _d = d; }

            private ushort U16(int o) { return (ushort)(_d[o] | _d[o + 1] << 8); }
            private uint U32(int o) { return (uint)(_d[o] | _d[o + 1] << 8 | _d[o + 2] << 16 | _d[o + 3] << 24); }

            private int RvaToOffset(uint rva, int sectionTable, int sections)
            {
                for (int i = 0; i < sections; i++)
                {
                    int s = sectionTable + i * 40;
                    uint va = U32(s + 12), size = Math.Max(U32(s + 8), U32(s + 16)), raw = U32(s + 20);
                    if (rva >= va && rva < va + size) return (int)(rva - va + raw);
                }
                throw new BadImageFormatException("rva not in a section");
            }

            public AssemblyM Parse()
            {
                if (_d.Length < 0x80 || _d[0] != 'M' || _d[1] != 'Z') throw new BadImageFormatException("not a PE file");
                int pe = (int)U32(0x3C);
                if (U32(pe) != 0x4550) throw new BadImageFormatException("no PE signature");
                int coff = pe + 4;
                int sections = U16(coff + 2);
                int optSize = U16(coff + 16);
                int opt = coff + 20;
                bool pe32plus = U16(opt) == 0x20B;
                int dataDir = opt + (pe32plus ? 112 : 96);
                uint cliRva = U32(dataDir + 14 * 8);
                if (cliRva == 0) throw new BadImageFormatException("not a managed assembly");
                int sectionTable = opt + optSize;
                int cli = RvaToOffset(cliRva, sectionTable, sections);
                int meta = RvaToOffset(U32(cli + 8), sectionTable, sections);
                if (U32(meta) != 0x424A5342) throw new BadImageFormatException("no metadata signature");
                int verLen = (int)U32(meta + 12);
                int p = meta + 16 + verLen;
                int streams = U16(p + 2);
                p += 4;
                for (int i = 0; i < streams; i++)
                {
                    int off = (int)U32(p), size = (int)U32(p + 4);
                    p += 8;
                    int nameStart = p;
                    while (_d[p] != 0) p++;
                    var name = Encoding.ASCII.GetString(_d, nameStart, p - nameStart);
                    p = nameStart + ((p - nameStart + 4) & ~3);
                    switch (name)
                    {
                        case "#~": case "#-": _tables = meta + off; break;
                        case "#Strings": _strings = meta + off; break;
                        case "#Blob": _blob = meta + off; _blobSize = size; break;
                    }
                }
                ReadTableHeader();
                return Build();
            }

            private void ReadTableHeader()
            {
                byte heapSizes = _d[_tables + 6];
                if ((heapSizes & 1) != 0) _strSize = 4;
                if ((heapSizes & 2) != 0) _guidSize = 4;
                if ((heapSizes & 4) != 0) _blobIdx = 4;
                ulong valid = (ulong)U32(_tables + 8) | (ulong)U32(_tables + 12) << 32;
                int p = _tables + 24;
                for (int t = 0; t < 64; t++) if ((valid >> t & 1) != 0) { _rows[t] = (int)U32(p); p += 4; }
                bool extra = (heapSizes & 0x40) != 0;
                if (extra) p += 4;
                for (int t = 0; t < 64; t++)
                {
                    _rowSize[t] = RowSize(t);
                    _tableOffset[t] = p;
                    p += _rowSize[t] * _rows[t];
                }
            }

            // ---- index sizes ----
            private int Idx(int table) { return _rows[table] < 65536 ? 2 : 4; }
            private int Coded(int bits, params int[] tables)
            {
                int max = 0;
                foreach (var t in tables) if (t >= 0) max = Math.Max(max, _rows[t]);
                return max < (1 << (16 - bits)) ? 2 : 4;
            }
            private int TypeDefOrRef { get { return Coded(2, 2, 1, 0x1B); } }
            private int HasConstant { get { return Coded(2, 4, 8, 0x17); } }
            private int HasCustomAttribute { get { return Coded(5, 6, 4, 1, 2, 8, 9, 10, 0, 0x0E, 0x17, 0x14, 0x11, 0x1A, 0x1B, 0x20, 0x23, 0x26, 0x27, 0x28, 0x2A, 0x2C, 0x2B); } }
            private int HasFieldMarshal { get { return Coded(1, 4, 8); } }
            private int HasDeclSecurity { get { return Coded(2, 2, 6, 0x20); } }
            private int MemberRefParent { get { return Coded(3, 2, 1, 0x1A, 6, 0x1B); } }
            private int HasSemantics { get { return Coded(1, 0x14, 0x17); } }
            private int MethodDefOrRef { get { return Coded(1, 6, 10); } }
            private int MemberForwarded { get { return Coded(1, 4, 6); } }
            private int Implementation { get { return Coded(2, 0x26, 0x23, 0x27); } }
            private int CustomAttributeType { get { return Coded(3, -1, -1, 6, 10, -1); } }
            private int ResolutionScope { get { return Coded(2, 0, 0x1A, 0x23, 1); } }
            private int TypeOrMethodDef { get { return Coded(1, 2, 6); } }

            private int RowSize(int t)
            {
                int S = _strSize, G = _guidSize, B = _blobIdx;
                switch (t)
                {
                    case 0x00: return 2 + S + 3 * G;
                    case 0x01: return ResolutionScope + 2 * S;
                    case 0x02: return 4 + 2 * S + TypeDefOrRef + Idx(4) + Idx(6);
                    case 0x03: return Idx(4);
                    case 0x04: return 2 + S + B;
                    case 0x05: return Idx(6);
                    case 0x06: return 4 + 2 + 2 + S + B + Idx(8);
                    case 0x07: return Idx(8);
                    case 0x08: return 2 + 2 + S;
                    case 0x09: return Idx(2) + TypeDefOrRef;
                    case 0x0A: return MemberRefParent + S + B;
                    case 0x0B: return 2 + HasConstant + B;
                    case 0x0C: return HasCustomAttribute + CustomAttributeType + B;
                    case 0x0D: return HasFieldMarshal + B;
                    case 0x0E: return 2 + HasDeclSecurity + B;
                    case 0x0F: return 2 + 4 + Idx(2);
                    case 0x10: return 4 + Idx(4);
                    case 0x11: return B;
                    case 0x12: return Idx(2) + Idx(0x14);
                    case 0x13: return Idx(0x14);
                    case 0x14: return 2 + S + TypeDefOrRef;
                    case 0x15: return Idx(2) + Idx(0x17);
                    case 0x16: return Idx(0x17);
                    case 0x17: return 2 + S + B;
                    case 0x18: return 2 + Idx(6) + HasSemantics;
                    case 0x19: return Idx(2) + 2 * MethodDefOrRef;
                    case 0x1A: return S;
                    case 0x1B: return B;
                    case 0x1C: return 2 + MemberForwarded + S + Idx(0x1A);
                    case 0x1D: return 4 + Idx(4);
                    case 0x1E: return 8;
                    case 0x1F: return 4;
                    case 0x20: return 4 + 8 + 4 + B + 2 * S;
                    case 0x21: return 4;
                    case 0x22: return 12;
                    case 0x23: return 8 + 4 + B + 2 * S + B;
                    case 0x24: return 4 + Idx(0x23);
                    case 0x25: return 12 + Idx(0x23);
                    case 0x26: return 4 + S + B;
                    case 0x27: return 8 + 2 * S + Implementation;
                    case 0x28: return 8 + S + Implementation;
                    case 0x29: return 2 * Idx(2);
                    case 0x2A: return 4 + TypeOrMethodDef + S;
                    case 0x2B: return MethodDefOrRef + B;
                    case 0x2C: return Idx(0x2A) + TypeDefOrRef;
                    default: return 0;
                }
            }

            // ---- row access ----
            private int Row(int t, int rid) { return _tableOffset[t] + (rid - 1) * _rowSize[t]; }
            private int Read(ref int o, int size) { int v = size == 2 ? U16(o) : (int)U32(o); o += size; return v; }
            private string Str(int idx)
            {
                if (idx == 0) return "";
                int s = _strings + idx, e = s;
                while (_d[e] != 0) e++;
                return Encoding.UTF8.GetString(_d, s, e - s);
            }
            private int BlobStart(int idx, out int len)
            {
                int p = _blob + idx;
                len = (int)Compressed(ref p);
                return p;
            }
            private uint Compressed(ref int p)
            {
                byte b = _d[p];
                if ((b & 0x80) == 0) { p += 1; return b; }
                if ((b & 0xC0) == 0x80) { uint v = (uint)((b & 0x3F) << 8 | _d[p + 1]); p += 2; return v; }
                uint w = (uint)((b & 0x1F) << 24 | _d[p + 1] << 16 | _d[p + 2] << 8 | _d[p + 3]); p += 4; return w;
            }

            // ---- names ----
            private string TypeDefName(int rid)
            {
                if (rid <= 0 || rid > _typeDefNames.Length) return "?";
                return _typeDefNames[rid - 1];
            }

            private TypeName TypeRefName(int rid, int depth = 0)
            {
                int o = Row(1, rid);
                int scope = Read(ref o, ResolutionScope);
                string name = Str(Read(ref o, _strSize)), ns = Str(Read(ref o, _strSize));
                int tag = scope & 3, sid = scope >> 2;
                string full = ns.Length > 0 ? ns + "." + name : name;
                if (tag == 3 && sid > 0 && depth < 16) // nested in another TypeRef
                {
                    var outer = TypeRefName(sid, depth + 1);
                    return new TypeName { Assembly = outer.Assembly, FullName = outer.FullName + "+" + name, Definition = outer.FullName + "+" + name };
                }
                string asm = _asmName;
                if (tag == 2 && sid > 0) asm = AssemblyRefName(sid);
                return new TypeName { Assembly = asm, FullName = full, Definition = full };
            }

            private string AssemblyRefName(int rid)
            {
                int o = Row(0x23, rid) + 8 + 4 + _blobIdx;
                return Str(Read(ref o, _strSize));
            }

            private TypeName TypeDefOrRefName(int coded, bool encodedInSig)
            {
                int tag, rid;
                if (encodedInSig) { tag = coded & 3; rid = coded >> 2; }
                else { tag = coded & 3; rid = coded >> 2; }
                if (rid == 0) return null;
                if (tag == 0) { var n = TypeDefName(rid); return new TypeName { Assembly = _asmName, FullName = n, Definition = n }; }
                if (tag == 1) return TypeRefName(rid);
                int len;
                int p = BlobStart(TypeSpecBlob(rid), out len);
                return SigType(ref p);
            }

            private int TypeSpecBlob(int rid) { int o = Row(0x1B, rid); return Read(ref o, _blobIdx); }

            private static string Prim(byte et)
            {
                switch (et)
                {
                    case 0x01: return "System.Void"; case 0x02: return "System.Boolean"; case 0x03: return "System.Char";
                    case 0x04: return "System.SByte"; case 0x05: return "System.Byte"; case 0x06: return "System.Int16"; case 0x07: return "System.UInt16";
                    case 0x08: return "System.Int32"; case 0x09: return "System.UInt32"; case 0x0A: return "System.Int64"; case 0x0B: return "System.UInt64";
                    case 0x0C: return "System.Single"; case 0x0D: return "System.Double"; case 0x0E: return "System.String";
                    case 0x16: return "System.TypedReference"; case 0x18: return "System.IntPtr"; case 0x19: return "System.UIntPtr"; case 0x1C: return "System.Object";
                }
                return null;
            }

            /// <summary>Decodes one type from a signature blob (§23.2.12).</summary>
            private TypeName SigType(ref int p)
            {
                byte et = _d[p++];
                var prim = Prim(et);
                if (prim != null) return new TypeName { Assembly = "mscorlib", FullName = prim, Definition = prim };
                switch (et)
                {
                    case 0x0F: { var t = SigType(ref p); return Wrap(t, "*", false); }
                    case 0x10: { var t = SigType(ref p); return Wrap(t, "&", false); }
                    case 0x11: case 0x12: return TypeDefOrRefName((int)Compressed(ref p), true) ?? new TypeName { FullName = "?", Definition = "?", Assembly = _asmName };
                    case 0x13: { var n = "!" + Compressed(ref p); return new TypeName { FullName = n, Definition = n, Assembly = "" }; }
                    case 0x1E: { var n = "!!" + Compressed(ref p); return new TypeName { FullName = n, Definition = n, Assembly = "" }; }
                    case 0x1D: { var t = SigType(ref p); return Wrap(t, "[]", true); }
                    case 0x14:
                        {
                            var t = SigType(ref p);
                            uint rank = Compressed(ref p);
                            uint ns = Compressed(ref p); for (int i = 0; i < ns; i++) Compressed(ref p);
                            uint nl = Compressed(ref p); for (int i = 0; i < nl; i++) Compressed(ref p);
                            return Wrap(t, "[" + new string(',', (int)Math.Max(0, rank - 1)) + "]", true);
                        }
                    case 0x15:
                        {
                            var gen = SigType(ref p);
                            uint n = Compressed(ref p);
                            var args = new List<string>();
                            for (int i = 0; i < n; i++) args.Add(SigType(ref p).FullName);
                            return new TypeName { Assembly = gen.Assembly, FullName = gen.Definition + "<" + string.Join(",", args) + ">", Definition = gen.Definition };
                        }
                    case 0x1B: { var m = new MethodInfoM(); MethodSig(ref p, m); return new TypeName { FullName = "fnptr", Definition = "fnptr", Assembly = "" }; }
                    case 0x1F: case 0x20: { Compressed(ref p); return SigType(ref p); } // custom modifier: skip
                    case 0x45: return SigType(ref p); // pinned
                    case 0x41: return SigType(ref p); // sentinel
                }
                throw new BadImageFormatException("unknown element type 0x" + et.ToString("x2"));
            }

            private static TypeName Wrap(TypeName t, string suffix, bool array)
            {
                return new TypeName { Assembly = t.Assembly, FullName = t.FullName + suffix, Definition = t.FullName + suffix, IsArray = array || t.IsArray };
            }

            private void MethodSig(ref int p, MethodInfoM m)
            {
                byte cc = _d[p++];
                if ((cc & 0x10) != 0) m.GenericArity = (int)Compressed(ref p);
                uint count = Compressed(ref p);
                m.Return = SigType(ref p).FullName;
                for (int i = 0; i < count; i++)
                {
                    if (_d[p] == 0x41) p++; // vararg sentinel
                    m.Params.Add(SigType(ref p).FullName);
                }
                m.IsStatic = (cc & 0x20) == 0;
            }

            private string MethodNameOf(int coded)
            {
                int tag = coded & 1, rid = coded >> 1;
                if (tag == 0) { int o = Row(6, rid) + 8; return Str(Read(ref o, _strSize)); }
                int mo = Row(10, rid);
                int parent = Read(ref mo, MemberRefParent);
                string name = Str(Read(ref mo, _strSize));
                var pt = MemberParent(parent);
                return (pt == null ? "?" : pt.Definition) + "." + name;
            }

            private TypeName MemberParent(int coded)
            {
                int tag = coded & 7, rid = coded >> 3;
                switch (tag)
                {
                    case 0: { var n = TypeDefName(rid); return new TypeName { Assembly = _asmName, FullName = n, Definition = n }; }
                    case 1: return TypeRefName(rid);
                    case 4: { int len; int p = BlobStart(TypeSpecBlob(rid), out len); return SigType(ref p); }
                }
                return null; // ModuleRef / MethodDef (vararg) parents are not tracked
            }

            public AssemblyM Build()
            {
                var a = new AssemblyM();
                if (_rows[0x20] > 0)
                {
                    int o = Row(0x20, 1) + 4;
                    int maj = U16(o), min = U16(o + 2), bld = U16(o + 4), rev = U16(o + 6);
                    o += 8 + 4 + _blobIdx;
                    a.Name = Str(Read(ref o, _strSize));
                    a.Version = new Version(maj, min, bld, rev);
                }
                else a.Name = "";
                _asmName = a.Name;
                for (int i = 1; i <= _rows[0x23]; i++) a.References.Add(AssemblyRefName(i));

                // type names incl. nesting
                int nTypes = _rows[2];
                var raw = new string[nTypes];
                _enclosing = new int[nTypes + 1];
                for (int i = 1; i <= _rows[0x29]; i++)
                {
                    int o = Row(0x29, i);
                    int nested = Read(ref o, Idx(2)), encl = Read(ref o, Idx(2));
                    if (nested <= nTypes) _enclosing[nested] = encl;
                }
                for (int i = 1; i <= nTypes; i++)
                {
                    int o = Row(2, i) + 4;
                    string name = Str(Read(ref o, _strSize)), ns = Str(Read(ref o, _strSize));
                    raw[i - 1] = ns.Length > 0 ? ns + "." + name : name;
                }
                _typeDefNames = new string[nTypes];
                for (int i = 1; i <= nTypes; i++)
                {
                    var n = raw[i - 1]; int e = _enclosing[i], guard = 0;
                    while (e > 0 && guard++ < 32) { n = raw[e - 1] + "+" + n; e = _enclosing[e]; }
                    _typeDefNames[i - 1] = n;
                }

                // methods, fields
                var methods = new MethodInfoM[_rows[6] + 1];
                for (int i = 1; i <= _rows[6]; i++)
                {
                    int o = Row(6, i) + 4;
                    Read(ref o, 2); // impl flags
                    int flags = Read(ref o, 2);
                    var m = new MethodInfoM { Name = Str(Read(ref o, _strSize)) };
                    int len; int p = BlobStart(Read(ref o, _blobIdx), out len);
                    MethodSig(ref p, m);
                    m.IsStatic = (flags & 0x10) != 0; m.IsAbstract = (flags & 0x400) != 0; m.IsVirtual = (flags & 0x40) != 0; m.IsNewSlot = (flags & 0x100) != 0;
                    methods[i] = m;
                }
                var fields = new FieldInfoM[_rows[4] + 1];
                for (int i = 1; i <= _rows[4]; i++)
                {
                    int o = Row(4, i);
                    int flags = Read(ref o, 2);
                    var f = new FieldInfoM { Name = Str(Read(ref o, _strSize)), IsStatic = (flags & 0x10) != 0 };
                    int len; int p = BlobStart(Read(ref o, _blobIdx), out len);
                    p++; // FIELD 0x06
                    while (_d[p] == 0x1F || _d[p] == 0x20) { p++; Compressed(ref p); }
                    f.Type = SigType(ref p).FullName;
                    fields[i] = f;
                }
                var types = new TypeInfoM[nTypes + 1];
                for (int i = 1; i <= nTypes; i++)
                {
                    int o = Row(2, i);
                    uint flags = U32(o); o += 4 + 2 * _strSize;
                    int ext = Read(ref o, TypeDefOrRef);
                    int fieldStart = Read(ref o, Idx(4)), methodStart = Read(ref o, Idx(6));
                    int fieldEnd = _rows[4] + 1, methodEnd = _rows[6] + 1;
                    if (i < nTypes) { int o2 = Row(2, i + 1) + 4 + 2 * _strSize + TypeDefOrRef; fieldEnd = Read(ref o2, Idx(4)); methodEnd = Read(ref o2, Idx(6)); }
                    var t = new TypeInfoM { Assembly = a.Name, FullName = _typeDefNames[i - 1], Flags = flags, BaseType = TypeDefOrRefName(ext, false) };
                    for (int f = fieldStart; f < fieldEnd && f <= _rows[4]; f++) t.Fields.Add(fields[f]);
                    for (int m = methodStart; m < methodEnd && m <= _rows[6]; m++) t.Methods.Add(methods[m]);
                    types[i] = t;
                    if (!a.Types.ContainsKey(t.FullName)) a.Types[t.FullName] = t;
                }
                for (int i = 1; i <= _rows[9]; i++)
                {
                    int o = Row(9, i);
                    int cls = Read(ref o, Idx(2)); int itf = Read(ref o, TypeDefOrRef);
                    var n = TypeDefOrRefName(itf, false);
                    if (cls <= nTypes && n != null) types[cls].Interfaces.Add(n);
                }
                // properties and events through their maps; accessor methods through MethodSemantics
                var semantics = new Dictionary<long, int>(); // (assoc table<<32|rid) → getter/setter
                var propOwner = new int[_rows[0x17] + 2];
                for (int i = 1; i <= _rows[0x15]; i++)
                {
                    int o = Row(0x15, i);
                    int parent = Read(ref o, Idx(2)), start = Read(ref o, Idx(0x17));
                    int end = _rows[0x17] + 1;
                    if (i < _rows[0x15]) { int o2 = Row(0x15, i + 1) + Idx(2); end = Read(ref o2, Idx(0x17)); }
                    for (int pr = start; pr < end && pr <= _rows[0x17]; pr++) propOwner[pr] = parent;
                }
                var props = new PropertyInfoM[_rows[0x17] + 1];
                for (int i = 1; i <= _rows[0x17]; i++)
                {
                    int o = Row(0x17, i) + 2;
                    var pr = new PropertyInfoM { Name = Str(Read(ref o, _strSize)) };
                    int len; int p = BlobStart(Read(ref o, _blobIdx), out len);
                    byte cc = _d[p++];
                    pr.IsStatic = (cc & 0x20) == 0;
                    uint n = Compressed(ref p);
                    pr.Type = SigType(ref p).FullName;
                    for (int k = 0; k < n; k++) pr.Params.Add(SigType(ref p).FullName);
                    props[i] = pr;
                    if (propOwner[i] > 0 && propOwner[i] <= nTypes) types[propOwner[i]].Properties.Add(pr);
                }
                var evOwner = new int[_rows[0x14] + 2];
                for (int i = 1; i <= _rows[0x12]; i++)
                {
                    int o = Row(0x12, i);
                    int parent = Read(ref o, Idx(2)), start = Read(ref o, Idx(0x14));
                    int end = _rows[0x14] + 1;
                    if (i < _rows[0x12]) { int o2 = Row(0x12, i + 1) + Idx(2); end = Read(ref o2, Idx(0x14)); }
                    for (int ev = start; ev < end && ev <= _rows[0x14]; ev++) evOwner[ev] = parent;
                }
                var events = new EventInfoM[_rows[0x14] + 1];
                for (int i = 1; i <= _rows[0x14]; i++)
                {
                    int o = Row(0x14, i) + 2;
                    var ev = new EventInfoM { Name = Str(Read(ref o, _strSize)) };
                    var tn = TypeDefOrRefName(Read(ref o, TypeDefOrRef), false);
                    ev.Type = tn == null ? "?" : tn.FullName;
                    events[i] = ev;
                    if (evOwner[i] > 0 && evOwner[i] <= nTypes) types[evOwner[i]].Events.Add(ev);
                }
                for (int i = 1; i <= _rows[0x18]; i++)
                {
                    int o = Row(0x18, i);
                    int sem = Read(ref o, 2), method = Read(ref o, Idx(6)), assoc = Read(ref o, HasSemantics);
                    int tag = assoc & 1, rid = assoc >> 1;
                    if (method <= 0 || method > _rows[6]) continue;
                    if (tag == 1 && rid <= _rows[0x17])
                    {
                        if ((sem & 0x2) != 0) { props[rid].Getter = methods[method]; props[rid].IsStatic = methods[method].IsStatic; }
                        if ((sem & 0x1) != 0) { props[rid].Setter = methods[method]; props[rid].IsStatic = methods[method].IsStatic; }
                    }
                    else if (tag == 0 && rid <= _rows[0x14]) events[rid].IsStatic = methods[method].IsStatic;
                }
                for (int i = 1; i <= _rows[0x19]; i++)
                {
                    int o = Row(0x19, i);
                    int cls = Read(ref o, Idx(2)); Read(ref o, MethodDefOrRef); int decl = Read(ref o, MethodDefOrRef);
                    if (cls <= nTypes) types[cls].ExplicitImpls.Add(MethodNameOf(decl));
                }
                // member references made by this assembly's code
                for (int i = 1; i <= _rows[10]; i++)
                {
                    int o = Row(10, i);
                    int parent = Read(ref o, MemberRefParent);
                    var mr = new MemberRefM { Name = Str(Read(ref o, _strSize)) };
                    int len; int p = BlobStart(Read(ref o, _blobIdx), out len);
                    mr.Parent = MemberParent(parent);
                    if (mr.Parent == null) continue;
                    if (_d[p] == 0x06) { p++; while (_d[p] == 0x1F || _d[p] == 0x20) { p++; Compressed(ref p); } mr.IsField = true; mr.Return = SigType(ref p).FullName; }
                    else { var m = new MethodInfoM(); MethodSig(ref p, m); mr.Return = m.Return; mr.Params = m.Params; mr.GenericArity = m.GenericArity; }
                    a.MemberRefs.Add(mr);
                }
                // type forwarders
                for (int i = 1; i <= _rows[0x27]; i++)
                {
                    int o = Row(0x27, i) + 8;
                    string name = Str(Read(ref o, _strSize)), ns = Str(Read(ref o, _strSize));
                    int impl = Read(ref o, Implementation);
                    if ((impl & 3) == 1) a.Forwarders[ns.Length > 0 ? ns + "." + name : name] = AssemblyRefName(impl >> 2);
                }
                return a;
            }
        }
    }
}
