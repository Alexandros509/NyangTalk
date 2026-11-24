using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NyangTalk.Shared;
using UnityEngine;

namespace NyangTalk.GameLink
{
    public sealed partial class GameLinkPlugin
    {
        // Runs before game Awake/Start localization requests. The catalog and the runtime's global string hash stay unchanged.
        private void PrepareCatalog()
        {
            try
            {
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "Unity.Addressables")
                    ?? Assembly.Load("Unity.Addressables");
                var path = Path.Combine(Application.dataPath, "StreamingAssets/aa/catalog.bin");
                var result = CatalogCompatibility.TryApplyToAssembly(assembly, path);
                if (result.Compatible)
                    Logger.LogInfo("catalog compatibility: " + result.State + " (" + result.Reason + ")");
                else
                {
                    _errors.Set("catalog compatibility", result.Reason);
                    Logger.LogWarning(_errors.Error + (result.Error == null ? "" : " " + result.Error.GetType().Name));
                }
            }
            catch (Exception ex)
            {
                _errors.Set("catalog compatibility", ex.GetType().Name);
                Logger.LogWarning(_errors.Error + " " + ex.Message);
            }
        }
    }
}
