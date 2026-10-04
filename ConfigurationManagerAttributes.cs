using System;
using BepInEx.Configuration;

namespace InfiniteEverything
{
    /// <summary>
    /// Optional display hints for BepInEx.ConfigurationManager (the F12 settings window SPT ships).
    /// ConfigurationManager reads these fields by NAME via reflection, so this class must stay internal to your
    /// assembly, keep these exact field names, and needs no reference to ConfigurationManager.dll.
    /// Pass an instance as the last argument of new ConfigDescription(...). Unset (null) fields use defaults.
    /// Field list follows https://github.com/BepInEx/BepInEx.ConfigurationManager (ConfigurationManagerAttributes.cs);
    /// copy the full upstream file if you need the remaining hooks.
    /// </summary>
#pragma warning disable 0169, 0414, 0649
    internal sealed class ConfigurationManagerAttributes
    {
        /// <summary>Sort order inside the category; higher shows first.</summary>
        public int? Order;
        /// <summary>Hidden unless the user ticks "Advanced settings".</summary>
        public bool? IsAdvanced;
        /// <summary>Show the value but do not allow editing.</summary>
        public bool? ReadOnly;
        /// <summary>false hides the setting from the window entirely (it still exists in the .cfg).</summary>
        public bool? Browsable;
        /// <summary>Overrides the displayed name.</summary>
        public string DispName;
        /// <summary>Overrides the category (section) shown in the window.</summary>
        public string Category;
        /// <summary>Overrides the description tooltip.</summary>
        public string Description;
        /// <summary>Shows a range setting as a percentage.</summary>
        public bool? ShowRangeAsPercent;
        /// <summary>Hide the "Reset" button.</summary>
        public bool? HideDefaultButton;
        /// <summary>Hide the setting's name (useful with CustomDrawer).</summary>
        public bool? HideSettingName;
        /// <summary>Draw the setting yourself with IMGUI (GUILayout.*).</summary>
        public Action<ConfigEntryBase> CustomDrawer;
    }
#pragma warning restore 0169, 0414, 0649
}
