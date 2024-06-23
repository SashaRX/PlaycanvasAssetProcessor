namespace TexTool.Properties {
    internal sealed partial class Settings : global::System.Configuration.ApplicationSettingsBase {
        public static Settings Default { get; } = ((Settings)(global::System.Configuration.ApplicationSettingsBase.Synchronized(new Settings())));

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_user")]
        public string? UserName {
            get => ((string)(this[nameof(UserName)]));
            set => this[nameof(UserName)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_projects_folder")]
        public string? ProjectsFolderPath {
            get => ((string)(this[nameof(ProjectsFolderPath)]));
            set => this[nameof(ProjectsFolderPath)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_project_id")]
        public string? ProjectId {
            get => ((string)(this[nameof(ProjectId)]));
            set => this[nameof(ProjectId)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_branch_id")]
        public string? BranchId {
            get => ((string)(this[nameof(BranchId)]));
            set => this[nameof(BranchId)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("default_api_key")]
        public string? PlaycanvasApiKey {
            get => ((string)(this[nameof(PlaycanvasApiKey)]));
            set => this[nameof(PlaycanvasApiKey)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("https://default.url")]
        public string? BaseUrl {
            get => ((string)(this[nameof(BaseUrl)]));
            set => this[nameof(BaseUrl)] = value;
        }

        [global::System.Configuration.UserScopedSettingAttribute()]
        [global::System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [global::System.Configuration.DefaultSettingValueAttribute("5")]
        public int? SemaphoreLimit {
            get => ((int)(this[nameof(SemaphoreLimit)]));
            set => this[nameof(SemaphoreLimit)] = value;
        }
    }
}
