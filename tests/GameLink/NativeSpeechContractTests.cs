using System;
using System.CodeDom.Compiler;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CSharp;
using NyangTalk.Shared;
using NyangTalk.App.Rendering;
using NyangTalk.Domain.Sending;

namespace NyangTalk.Tests
{
    public static class NativeSpeechContractTests
    {
        [Test("40-3,50-7,send")]
        public static void NativeSpeechContractUpdatesTheLocalBubbleAndKeepsTheFilterGate()
        {
            var contract = ContractFile.Parse(File.ReadAllText(Path.Combine(Ctx.Root, "src", "Shared", "contract.json")));
            var ids = new[] { "mp.sendSpeech", "speech.type", "speech.isMain", "speech.send" };
            var selected = new ContractFile { Entries = contract.Entries.Where(e => ids.Contains(e.Id)).ToList() };
            A.Eq(ids.Length, selected.Entries.Count, "all speech contract entries exist");
            A.True(selected.Get("speech.send").Required && !selected.Get("speech.send").Static, "native local path is a required instance method");
            A.True(selected.Get("speech.type").Required && selected.Get("speech.isMain").Required, "main-cat selection is required");
            var dir = Ctx.NewDir("native-speech-contract");
            var path = Path.Combine(dir, "SpeechFixture" + Guid.NewGuid().ToString("N") + ".dll");
            const string fixture = @"
namespace BongoCat.Multiplayer {
 public static class SteamMultiplayer { public static int Sent; public static void SendSpeech(string speech) { Sent++; } }
}
namespace Vfx {
 public sealed class CatSpeech {
  private bool _isMainCat = true;
  public string Bubble;
  public void SendSpeech(string speech) { BongoCat.Multiplayer.SteamMultiplayer.SendSpeech(speech); Bubble=speech; }
 }
}";
            using (var compiler = new CSharpCodeProvider())
            {
                var options = new CompilerParameters { GenerateExecutable = false, OutputAssembly = path };
                options.ReferencedAssemblies.Add("System.Core.dll");
                options.ReferencedAssemblies.Add(typeof(ContractFile).Assembly.Location);
                var build = compiler.CompileAssemblyFromSource(options,
                    File.ReadAllText(Path.Combine(Ctx.Root, "src", "Plugin", "ContractRuntime.cs")), fixture);
                A.False(build.Errors.HasErrors, string.Join("; ", build.Errors.Cast<CompilerError>().Select(e => e.ErrorText)));
            }
            var assembly = Assembly.LoadFrom(path);
            foreach (var entry in selected.Entries) entry.Assembly = assembly.GetName().Name;
            var runtimeType = assembly.GetType("NyangTalk.GameLink.ContractRuntime");
            var runtime = Activator.CreateInstance(runtimeType, selected);
            runtimeType.GetMethod("ResolveAll").Invoke(runtime, null);
            var targetType = assembly.GetType("Vfx.CatSpeech");
            var target = Activator.CreateInstance(targetType);
            var call = runtimeType.GetMethod("Call");
            Action<string, object, string> invoke = (id, receiver, text) => call.Invoke(runtime, new object[] { id, receiver, new object[] { text } });
            invoke("mp.sendSpeech", null, "network only");
            A.Eq<object>(null, targetType.GetField("Bubble").GetValue(target), "network-only path does not update my own cat");
            invoke("speech.send", target, "from messenger");
            A.Eq("from messenger", (string)targetType.GetField("Bubble").GetValue(target), "actual contract runtime invokes the complete local entry point");
            var sent = assembly.GetType("BongoCat.Multiplayer.SteamMultiplayer").GetField("Sent");
            A.Eq(2, (int)sent.GetValue(null), "native entry point also transmits once");
            A.False(FilteredSend.Run(true, () => invoke("speech.send", target, "blocked")), "game-filter rejection blocks the whole native path");
            A.Eq(2, (int)sent.GetValue(null), "filtered text never reaches network");
            A.Eq("from messenger", (string)targetType.GetField("Bubble").GetValue(target), "filtered text does not alter local bubble");
            using (var measurer = new Measurer())
            {
                var style = new RenderStyle();
                var row = new ChatLayout(measurer).OutgoingRow(new OutgoingMessage { Text = "test", State = OutgoingState.Rejected, Reason = "speech-not-ready" }, 500, style);
                A.True(row.Parts.Any(p => p.Kind == PartKind.Status && p.Text.Contains(style.L("send.speechNotReady"))), "not-ready error is understandable in the UI");
            }
        }
    }
}
