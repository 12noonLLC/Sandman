﻿//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

namespace Sandman.Properties {
    
    
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "17.10.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        
        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));
        
        public static Settings Default {
            get {
                return defaultInstance;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("00:00:10")]
        public global::System.TimeSpan DelayBeforeSuspending {
            get {
                return ((global::System.TimeSpan)(this["DelayBeforeSuspending"]));
            }
            set {
                this["DelayBeforeSuspending"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("00:30:00")]
        public global::System.TimeSpan TimeUserInactiveBeforeSuspending {
            get {
                return ((global::System.TimeSpan)(this["TimeUserInactiveBeforeSuspending"]));
            }
            set {
                this["TimeUserInactiveBeforeSuspending"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("00:10:00")]
        public global::System.TimeSpan DelayAfterResume {
            get {
                return ((global::System.TimeSpan)(this["DelayAfterResume"]));
            }
            set {
                this["DelayAfterResume"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("ehshell;epg123;epg123client;hdhr2mxf;epg123Transfer;WMC_Status;vlc")]
        public string BlacklistedProcesses {
            get {
                return ((string)(this["BlacklistedProcesses"]));
            }
            set {
                this["BlacklistedProcesses"] = value;
            }
        }
        
        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("00:01:00")]
        public global::System.TimeSpan DelayForElevatedProcess {
            get {
                return ((global::System.TimeSpan)(this["DelayForElevatedProcess"]));
            }
            set {
                this["DelayForElevatedProcess"] = value;
            }
        }
    }
}
