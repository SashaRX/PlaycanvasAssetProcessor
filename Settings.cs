// Settings.cs
namespace TexTool.Properties {
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        public static Settings Default { get; } = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_project_id")]
        public string ProjectId {
            get => ((string)(this["ProjectId"]));
            set => this["ProjectId"] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_branch_id")]
        public string BranchId {
            get => ((string)(this["BranchId"]));
            set => this["BranchId"] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_api_key")]
        public string PlaycanvasApiKey {
            get => ((string)(this["PlaycanvasApiKey"]));
            set => this["PlaycanvasApiKey"] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("https://default.url")]
        public string BaseUrl {
            get => ((string)(this["BaseUrl"]));
            set => this["BaseUrl"] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public int SemaphoreLimit {
            get => ((int)(this["SemaphoreLimit"]));
            set => this["SemaphoreLimit"] = value;
        }
    }
}
