namespace TexTool.Properties {
    [global::System.Runtime.CompilerServices.CompilerGeneratedAttribute()]
    [global::System.CodeDom.Compiler.GeneratedCodeAttribute("Microsoft.VisualStudio.Editors.SettingsDesigner.SettingsSingleFileGenerator", "16.5.0.0")]
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {

        private static Settings defaultInstance = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));

        public static Settings Default {
            get {
                return defaultInstance;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("1054788")]
        public string ProjectId {
            get {
                return ((string)(this["ProjectId"]));
            }
            set {
                this["ProjectId"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("55d4b774-8ecf-4a72-9798-9ca0e83304f0")]
        public string BranchId {
            get {
                return ((string)(this["BranchId"]));
            }
            set {
                this["BranchId"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("o5lPWdvxh6lCMtw6jlvlF8jqnhq1RjGd")]
        public string PlaycanvasApiKey {
            get {
                return ((string)(this["PlaycanvasApiKey"]));
            }
            set {
                this["PlaycanvasApiKey"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("https://playcanvas.com")]
        public string BaseUrl {
            get {
                return ((string)(this["BaseUrl"]));
            }
            set {
                this["BaseUrl"] = value;
            }
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("4")]
        public int SemaphoreLimit {
            get {
                return ((int)(this["SemaphoreLimit"]));
            }
            set {
                this["SemaphoreLimit"] = value;
            }
        }
    }
}
